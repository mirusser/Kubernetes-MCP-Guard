using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using InfraGate.AgentGuardrails;
using InfraGate.AgentLlm;
using InfraGate.Observer.Contracts;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Cycle.Workflow;
using InfraGate.Planner.Decision;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using InfraGate.AgentMcp;
using ModelContextProtocol.Protocol;
using InfraGate.Remediation.Contracts;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using InfraGate.Planner.Handoff;
using InfraGate.Planner.Llm;

namespace InfraGate.Planner.Tests.UnitTests;

/// <summary>
/// Focused executor-level tests for the five Planner workflow executors.
/// These tests exercise individual executors in isolation, complementing the
/// end-to-end BatchProcessorTests that exercise the complete workflow graph.
/// </summary>
public sealed class WorkflowExecutorTests
{
    // ------------------------------------------------------------------ //
    // FilterExecutor
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task FilterExecutor_Resolved_DropsSilentlyWithoutAudit()
    {
        var report = CreateAnomaly(AnomalyStatus.Resolved, AnomalyKind.DeploymentUnavailable);
        var auditOutbox = new FakePlannerAuditOutbox();
        var context = new FakeWorkflowContext();

        var executor = new FilterExecutor("filter-0", new PlannerDedupeStore(), auditOutbox, NullLogger.Instance);
        await executor.HandleAsync(report, context, CancellationToken.None);

        Assert.Empty(auditOutbox.Entries);
        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task FilterExecutor_UnsupportedKind_EmitsAuditAndDrops()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, (AnomalyKind)999);
        var auditOutbox = new FakePlannerAuditOutbox();
        var context = new FakeWorkflowContext();

        var executor = new FilterExecutor("filter-0", new PlannerDedupeStore(), auditOutbox, NullLogger.Instance);
        await executor.HandleAsync(report, context, CancellationToken.None);

        var entry = Assert.Single(auditOutbox.Entries);
        Assert.Equal(PlannerAuditEvents.ProposalSkipped, entry.EventName);
        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task FilterExecutor_ActiveAllowedKind_ForwardsReport()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var context = new FakeWorkflowContext();

        var executor = new FilterExecutor("filter-0", new PlannerDedupeStore(), null, NullLogger.Instance);
        await executor.HandleAsync(report, context, CancellationToken.None);

        var forwarded = Assert.Single(context.SentMessages);
        Assert.Same(report, forwarded);
    }

    // ------------------------------------------------------------------ //
    // DedupeGateExecutor
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task DedupeGateExecutor_ActivePlan_EmitsProposalSkippedAndDrops()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var dedupeStore = new PlannerDedupeStore();
        var expiry = DateTimeOffset.UtcNow.AddHours(1);
        dedupeStore.TrackActivePlan(report.AnomalyId, "plan-existing", DateTimeOffset.UtcNow, expiry);

        var auditOutbox = new FakePlannerAuditOutbox();
        var context = new FakeWorkflowContext();

        var executor = new DedupeGateExecutor("dedupe-0", dedupeStore, auditOutbox, NullLogger.Instance);
        await executor.HandleAsync(report, context, CancellationToken.None);

        var entry = Assert.Single(auditOutbox.Entries);
        Assert.Equal(PlannerAuditEvents.ProposalSkipped, entry.EventName);
        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task DedupeGateExecutor_NoActivePlan_ForwardsReport()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var context = new FakeWorkflowContext();

        var executor = new DedupeGateExecutor("dedupe-0", new PlannerDedupeStore(), null, NullLogger.Instance);
        await executor.HandleAsync(report, context, CancellationToken.None);

        var forwarded = Assert.Single(context.SentMessages);
        Assert.Same(report, forwarded);
    }

    // ------------------------------------------------------------------ //
    // DecideExecutor — tools must exclude propose_plan
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task DecideExecutor_UsesFactory_AgentToolsExcludeProposePlan()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": { "name": "nginx-demo", "namespace": "mcp-nginx-demo" }
        }
        """);

        var context = new FakeWorkflowContext();
        var agentFactory = new ToolCallingAgentFactory(chatClient);

        var readOnlyTools = new List<AITool>
        {
            AIFunctionFactory.Create(() => "status", PlannerConventions.ToolNames.GetK8sStatus),
        };

        var executor = new DecideExecutor(
            "decide-0", agentFactory, "system prompt", readOnlyTools,
            4, 30, null, NullLogger.Instance);

        await executor.HandleAsync(report, context, CancellationToken.None);

        var agentTools = chatClient.LastOptions?.Tools ?? [];
        Assert.DoesNotContain(agentTools, t => string.Equals(t.Name, PlannerConventions.ToolNames.ProposePlan, StringComparison.Ordinal));

        var forwarded = Assert.Single(context.SentMessages);
        Assert.IsType<DecisionContext>(forwarded);
    }

    [Fact]
    public async Task DecideExecutor_ResponseWithoutBraces_NoDecisionForwarded()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var chatClient = new FixtureChatClient("just some text without JSON braces");
        var context = new FakeWorkflowContext();
        var agentFactory = new ToolCallingAgentFactory(chatClient);

        var executor = new DecideExecutor(
            "decide-1", agentFactory, "system prompt", [],
            4, 30, null, NullLogger.Instance);

        await executor.HandleAsync(report, context, CancellationToken.None);

        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task DecideExecutor_InvalidJson_NoDecisionForwarded()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var chatClient = new FixtureChatClient("{ invalid json here }");
        var context = new FakeWorkflowContext();
        var agentFactory = new ToolCallingAgentFactory(chatClient);

        var executor = new DecideExecutor(
            "decide-2", agentFactory, "system prompt", [],
            4, 30, null, NullLogger.Instance);

        await executor.HandleAsync(report, context, CancellationToken.None);

        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task DecideExecutor_MissingOperationType_NoDecisionForwarded()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var chatClient = new FixtureChatClient("""{"arguments": {"name": "test"}}""");
        var context = new FakeWorkflowContext();
        var agentFactory = new ToolCallingAgentFactory(chatClient);

        var executor = new DecideExecutor(
            "decide-3", agentFactory, "system prompt", [],
            4, 30, null, NullLogger.Instance);

        await executor.HandleAsync(report, context, CancellationToken.None);

        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task DecideExecutor_ArgumentsWithBooleanValue_ConvertsCorrectly()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": { "name": "test", "force": true, "dryRun": false }
        }
        """);
        var context = new FakeWorkflowContext();
        var agentFactory = new ToolCallingAgentFactory(chatClient);

        var executor = new DecideExecutor(
            "decide-4", agentFactory, "system prompt", [],
            4, 30, null, NullLogger.Instance);

        await executor.HandleAsync(report, context, CancellationToken.None);

        var forwarded = Assert.Single(context.SentMessages);
        var decisionCtx = Assert.IsType<DecisionContext>(forwarded);
        Assert.True((bool?)decisionCtx.Decision.Arguments["force"]);
        Assert.False((bool?)decisionCtx.Decision.Arguments["dryRun"]);
    }

    [Fact]
    public async Task DecideExecutor_ArgumentsWithNullValue_ConvertsCorrectly()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": { "name": "test", "optionalField": null }
        }
        """);
        var context = new FakeWorkflowContext();
        var agentFactory = new ToolCallingAgentFactory(chatClient);

        var executor = new DecideExecutor(
            "decide-5", agentFactory, "system prompt", [],
            4, 30, null, NullLogger.Instance);

        await executor.HandleAsync(report, context, CancellationToken.None);

        var forwarded = Assert.Single(context.SentMessages);
        Assert.IsType<DecisionContext>(forwarded);
    }

    [Fact]
    public async Task DecideExecutor_ArgumentsWithLargeInteger_ConvertsToLong()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var chatClient = new FixtureChatClient("""
        {
          "operationType": "restart_deployment",
          "arguments": { "name": "test", "replicas": 5000000000 }
        }
        """);
        var context = new FakeWorkflowContext();
        var agentFactory = new ToolCallingAgentFactory(chatClient);

        var executor = new DecideExecutor(
            "decide-6", agentFactory, "system prompt", [],
            4, 30, null, NullLogger.Instance);

        await executor.HandleAsync(report, context, CancellationToken.None);

        var forwarded = Assert.Single(context.SentMessages);
        Assert.IsType<DecisionContext>(forwarded);
    }

    [Fact]
    public async Task DecideExecutor_WithAskObserverTool_ToolCanBeInvoked()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);

        // ChatClient yields a tool call on first invocation, and a valid JSON decision on second
        var chatClient = new FixtureChatClient((messages) =>
        {
            if (!messages.Skip(2).Any()) // System + User
            {
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, [
                    new FunctionCallContent("call1", "ask_observer_to_inspect", new Dictionary<string, object?> { ["toolName"] = "get_k8s_pods", ["argumentsJson"] = "{}" })
                ]));
            }

            // Second invocation (System + User + Assistant(tool_call) + Tool(result))
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, """
            {
              "operationType": "restart_deployment",
              "arguments": { "name": "nginx-demo", "namespace": "mcp-nginx-demo" }
            }
            """));
        });

        var context = new FakeWorkflowContext();
        var agentFactory = new ToolCallingAgentFactory(chatClient);

        var observerChannel = new FakeObserverChannel();
        var askObserverTool = AskObserverTool.Create(observerChannel, "cycle-1");

        var executor = new DecideExecutor(
            "decide-7", agentFactory, "system prompt", [askObserverTool],
            4, 30, null, NullLogger.Instance);

        await executor.HandleAsync(report, context, CancellationToken.None);

        Assert.Equal(1, observerChannel.SendToolRequestCallCount);

        var forwarded = Assert.Single(context.SentMessages);
        var decisionCtx = Assert.IsType<DecisionContext>(forwarded);
        Assert.Equal("restart_deployment", decisionCtx.Decision.OperationType);
    }

    [Fact]
    public async Task DecideExecutor_WithAskObserverTool_GuardsToolResultThroughSharedFactory()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        const string redactedToolResult = "[safe observer result]";
        string? capturedToolResult = null;

        var chatClient = new FixtureChatClient((messages) =>
        {
            if (!messages.Skip(2).Any())
            {
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, [
                    new FunctionCallContent("call1", AskObserverTool.FunctionName, new Dictionary<string, object?>
                    {
                        ["toolName"] = "get_k8s_pods",
                        ["argumentsJson"] = "{}"
                    })
                ]));
            }

            capturedToolResult = messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionResultContent>()
                .FirstOrDefault()?.Result as string;

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, """
            {
              "operationType": "restart_deployment",
              "arguments": { "name": "nginx-demo", "namespace": "mcp-nginx-demo" }
            }
            """));
        });
        var toolResultGuard = new FakeDecideContentGuard(
            ModelVisibleContentAction.Redact,
            replacementText: redactedToolResult);
        var agentFactory = new ToolCallingAgentFactory(chatClient, contentGuard: toolResultGuard);
        var observerChannel = new FakeObserverChannel();
        var askObserverTool = AskObserverTool.Create(observerChannel, "cycle-1");
        var context = new FakeWorkflowContext();

        var executor = new DecideExecutor(
            "decide-7-tool-guard", agentFactory, "system prompt", [askObserverTool],
            4, 30, null, NullLogger.Instance);

        await executor.HandleAsync(report, context, CancellationToken.None);

        Assert.Equal("simulated-response", toolResultGuard.LastSeenText);
        Assert.Equal(ModelVisibleContentSource.AgentToolResult, toolResultGuard.LastSeenSource);
        Assert.Equal(AskObserverTool.FunctionName, toolResultGuard.LastSeenToolName);
        Assert.Equal(redactedToolResult, capturedToolResult);
    }

    // ------------------------------------------------------------------ //
    // DecideExecutor — content guard
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task DecideExecutor_ContentGuardAllows_OriginalAnomalyTextReachesAgent()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        string? capturedUserContent = null;
        var chatClient = new FixtureChatClient(messages =>
        {
            capturedUserContent = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, """
            { "operationType": "restart_deployment", "arguments": { "name": "nginx", "namespace": "default" } }
            """));
        });

        var guard = new FakeDecideContentGuard(ModelVisibleContentAction.Allow, passthrough: true);
        var executor = new DecideExecutor(
            "decide-guard-allow", new ToolCallingAgentFactory(chatClient), "prompt", [], 4, 30,
            null, NullLogger.Instance, contentGuard: guard);

        var context = new FakeWorkflowContext();
        await executor.HandleAsync(report, context, CancellationToken.None);

        Assert.NotNull(guard.LastSeenText);
        Assert.Equal(guard.LastSeenText, capturedUserContent);
        Assert.Single(context.SentMessages);
    }

    [Fact]
    public async Task DecideExecutor_ContentGuardRedacts_PlaceholderReachesAgent_NotAnomalyJson()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        const string redactedText = "[CONTENT REDACTED: potential injection pattern]";
        string? capturedUserContent = null;
        var chatClient = new FixtureChatClient(messages =>
        {
            capturedUserContent = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"));
        });

        var guard = new FakeDecideContentGuard(ModelVisibleContentAction.Redact, replacementText: redactedText);
        var executor = new DecideExecutor(
            "decide-guard-redact", new ToolCallingAgentFactory(chatClient), "prompt", [], 4, 30,
            null, NullLogger.Instance, contentGuard: guard);

        var context = new FakeWorkflowContext();
        await executor.HandleAsync(report, context, CancellationToken.None);

        Assert.Equal(redactedText, capturedUserContent);
        Assert.DoesNotContain(guard.LastSeenText!, capturedUserContent!);
    }

    [Fact]
    public async Task DecideExecutor_ContentGuardQuarantines_QuarantinePlaceholderReachesAgent()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        string? capturedUserContent = null;
        var chatClient = new FixtureChatClient(messages =>
        {
            capturedUserContent = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"));
        });

        var guard = new FakeDecideContentGuard(
            ModelVisibleContentAction.Quarantine,
            replacementText: AgentGuardrailConventions.DefaultQuarantinePlaceholder);
        var executor = new DecideExecutor(
            "decide-guard-quarantine", new ToolCallingAgentFactory(chatClient), "prompt", [], 4, 30,
            null, NullLogger.Instance, contentGuard: guard);

        var context = new FakeWorkflowContext();
        await executor.HandleAsync(report, context, CancellationToken.None);

        Assert.Equal(AgentGuardrailConventions.DefaultQuarantinePlaceholder, capturedUserContent);
        Assert.DoesNotContain(guard.LastSeenText!, capturedUserContent!);
    }

    [Fact]
    public async Task DecideExecutor_ContentGuardBlocks_LlmNotCalled_NoDecisionForwarded()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        bool chatClientCalled = false;
        var chatClient = new FixtureChatClient(messages =>
        {
            chatClientCalled = true;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"));
        });

        var guard = new FakeDecideContentGuard(
            ModelVisibleContentAction.BlockModelIngestion,
            replacementText: AgentGuardrailConventions.DefaultBlockedPlaceholder);
        var executor = new DecideExecutor(
            "decide-guard-block", new ToolCallingAgentFactory(chatClient), "prompt", [], 4, 30,
            null, NullLogger.Instance, contentGuard: guard);

        var context = new FakeWorkflowContext();
        await executor.HandleAsync(report, context, CancellationToken.None);

        Assert.False(chatClientCalled);
        Assert.Empty(context.SentMessages);
    }

    // ------------------------------------------------------------------ //
    // ValidateExecutor
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task ValidateExecutor_InvalidOperationType_DropsWithoutForwarding()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("delete_cluster", new Dictionary<string, object?>(), null);
        var decisionCtx = new DecisionContext(report, decision);
        var context = new FakeWorkflowContext();

        using var testMeter = new Meter("test-validate-invalid-op");
        var metrics = new AgentGuardrailMetrics(testMeter);
        var recorded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == AgentGuardrailConventions.DecisionCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            recorded.Add(new Measurement<long>(value, tags)));
        listener.Start();

        var executor = new ValidateExecutor("validate-0", new ConcurrentDictionary<string, byte>(), new PlannerDedupeStore(), metrics, NullLogger.Instance);
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        Assert.Empty(context.SentMessages);
        var measurement = Assert.Single(recorded);
        Assert.Equal(1L, measurement.Value);
        var tags = measurement.Tags.ToArray();
        Assert.Equal(AgentGuardrailConventions.Outcomes.Rejected, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailOutcome).Value);
        Assert.Equal(AgentGuardrailConventions.Reasons.InvalidOperation, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailReason).Value);
    }

    [Fact]
    public async Task ValidateExecutor_ValidOperation_ForwardsDecisionContext()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("restart_deployment", new Dictionary<string, object?> { ["name"] = "nginx", ["namespace"] = "default" }, null);
        var decisionCtx = new DecisionContext(report, decision);
        var context = new FakeWorkflowContext();

        using var testMeter = new Meter("test-validate-valid-op");
        var metrics = new AgentGuardrailMetrics(testMeter);
        var recorded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == AgentGuardrailConventions.DecisionCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            recorded.Add(new Measurement<long>(value, tags)));
        listener.Start();

        var executor = new ValidateExecutor("validate-0", new ConcurrentDictionary<string, byte>(), new PlannerDedupeStore(), metrics, NullLogger.Instance);
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        var forwarded = Assert.Single(context.SentMessages);
        var forwardedCtx = Assert.IsType<DecisionContext>(forwarded);
        Assert.Equal("restart_deployment", forwardedCtx.Decision.OperationType);

        var measurement = Assert.Single(recorded);
        Assert.Equal(1L, measurement.Value);
        var tags = measurement.Tags.ToArray();
        Assert.Equal(AgentGuardrailConventions.Outcomes.Accepted, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailOutcome).Value);
        Assert.Equal(AgentGuardrailConventions.Reasons.None, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailReason).Value);
    }

    [Fact]
    public async Task ValidateExecutor_InvalidArguments_DropsWithoutForwarding()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        // Invalid arguments for scale_deployment (missing namespace, bad replicas)
        var decision = new RemediationDecision("scale_deployment", new Dictionary<string, object?> { ["name"] = "nginx" }, null);
        var decisionCtx = new DecisionContext(report, decision);
        var context = new FakeWorkflowContext();

        using var testMeter = new Meter("test-validate-invalid-args");
        var metrics = new AgentGuardrailMetrics(testMeter);
        var recorded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == AgentGuardrailConventions.DecisionCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            recorded.Add(new Measurement<long>(value, tags)));
        listener.Start();

        var executor = new ValidateExecutor("validate-0", new ConcurrentDictionary<string, byte>(), new PlannerDedupeStore(), metrics, NullLogger.Instance);
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        Assert.Empty(context.SentMessages);
        var measurement = Assert.Single(recorded);
        Assert.Equal(1L, measurement.Value);
        var tags = measurement.Tags.ToArray();
        Assert.Equal(AgentGuardrailConventions.Outcomes.Rejected, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailOutcome).Value);
        Assert.Equal(AgentGuardrailConventions.Reasons.InvalidArguments, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailReason).Value);
    }

    [Fact]
    public async Task ValidateExecutor_DedupeInBatch_DropsWithoutForwarding()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("restart_deployment", new Dictionary<string, object?> { ["name"] = "nginx", ["namespace"] = "default" }, null);
        var decisionCtx = new DecisionContext(report, decision);
        var context = new FakeWorkflowContext();

        using var testMeter = new Meter("test-validate-dedupe");
        var metrics = new AgentGuardrailMetrics(testMeter);
        var recorded = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == AgentGuardrailConventions.DecisionCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            recorded.Add(new Measurement<long>(value, tags)));
        listener.Start();

        var executor = new ValidateExecutor("validate-0", new ConcurrentDictionary<string, byte>(), new PlannerDedupeStore(), metrics, NullLogger.Instance);

        // Handle once (should be accepted)
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);
        // Handle twice (should be dedupe blocked)
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        Assert.Single(context.SentMessages); // Only forwarded once
        Assert.Equal(2, recorded.Count);

        var secondMeasurement = recorded[1];
        Assert.Equal(1L, secondMeasurement.Value);
        var tags = secondMeasurement.Tags.ToArray();
        Assert.Equal(AgentGuardrailConventions.Outcomes.Rejected, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailOutcome).Value);
        Assert.Equal(AgentGuardrailConventions.Reasons.DedupeInBatch, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailReason).Value);
    }

    [Fact]
    public async Task ValidateExecutor_NullGuardrailMetrics_DoesNotThrow()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("restart_deployment",
            new Dictionary<string, object?> { ["name"] = "nginx", ["namespace"] = "default" }, null);
        var decisionCtx = new DecisionContext(report, decision);
        var context = new FakeWorkflowContext();

        var executor = new ValidateExecutor("validate-null", new ConcurrentDictionary<string, byte>(),
            new PlannerDedupeStore(), guardrailMetrics: null, NullLogger.Instance);

        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        Assert.Single(context.SentMessages);
    }

    [Fact]
    public async Task ValidateExecutor_SetDeploymentImageValid_ForwardsDecision()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("set_deployment_image",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = "nginx",
                ["namespace"] = "default",
                ["container"] = "nginx",
                ["image"] = "nginx:2.0",
            }, null);
        var decisionCtx = new DecisionContext(report, decision);
        var context = new FakeWorkflowContext();

        var executor = new ValidateExecutor("validate-sdi", new ConcurrentDictionary<string, byte>(),
            new PlannerDedupeStore(), guardrailMetrics: null, NullLogger.Instance);

        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        Assert.Single(context.SentMessages);
    }

    // ------------------------------------------------------------------ //
    // ProposeExecutor
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task ProposeExecutor_Success_YieldsRemediationProposal()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("restart_deployment", new Dictionary<string, object?>(), null);
        var decisionCtx = new DecisionContext(report, decision);

        var mcpClient = new FakeAgentMcpToolset { ResponseText = """{"planId":"plan-123"}""" };
        var auditOutbox = new FakePlannerAuditOutbox();
        var context = new FakeWorkflowContext();

        var executor = new ProposeExecutor("propose-0", mcpClient, new PlannerDedupeStore(), auditOutbox, null, NullLogger.Instance);
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        var output = Assert.Single(context.YieldedOutputs);
        var proposal = Assert.IsType<RemediationProposal>(output);
        Assert.Equal("plan-123", proposal.PlanId);

        var audit = Assert.Single(auditOutbox.Entries);
        Assert.Equal(PlannerAuditEvents.ProposePlanSucceeded, audit.EventName);
    }

    [Fact]
    public async Task ProposeExecutor_MissingPlanIdInResponse_DoesNotYieldProposal()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("restart_deployment", new Dictionary<string, object?>(), null);
        var decisionCtx = new DecisionContext(report, decision);

        var mcpClient = new FakeAgentMcpToolset { ResponseText = """{"otherField":"value"}""" };
        var context = new FakeWorkflowContext();

        var executor = new ProposeExecutor("propose-0", mcpClient, new PlannerDedupeStore(), null, null, NullLogger.Instance);
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task ProposeExecutor_PlanIdInNestedContentObject_YieldsProposal()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("restart_deployment", new Dictionary<string, object?>(), null);
        var decisionCtx = new DecisionContext(report, decision);

        var mcpClient = new FakeAgentMcpToolset { ResponseText = """{"content":{"planId":"plan-nested"}}""" };
        var context = new FakeWorkflowContext();

        var executor = new ProposeExecutor("propose-0", mcpClient, new PlannerDedupeStore(), null, null, NullLogger.Instance);
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        var output = Assert.Single(context.YieldedOutputs);
        var proposal = Assert.IsType<RemediationProposal>(output);
        Assert.Equal("plan-nested", proposal.PlanId);
    }

    [Fact]
    public async Task ProposeExecutor_PlanIdInArray_YieldsProposal()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("restart_deployment", new Dictionary<string, object?>(), null);
        var decisionCtx = new DecisionContext(report, decision);

        var mcpClient = new FakeAgentMcpToolset { ResponseText = """[{"planId":"plan-array"}]""" };
        var context = new FakeWorkflowContext();

        var executor = new ProposeExecutor("propose-0", mcpClient, new PlannerDedupeStore(), null, null, NullLogger.Instance);
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        var output = Assert.Single(context.YieldedOutputs);
        var proposal = Assert.IsType<RemediationProposal>(output);
        Assert.Equal("plan-array", proposal.PlanId);
    }

    [Fact]
    public async Task ProposeExecutor_PlanIdInTextField_YieldsProposal()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("restart_deployment", new Dictionary<string, object?>(), null);
        var decisionCtx = new DecisionContext(report, decision);

        var mcpClient = new FakeAgentMcpToolset
        {
            ResponseText = "{\"Text\":\"{\\\"planId\\\":\\\"plan-from-text\\\"}\"}",
        };
        var context = new FakeWorkflowContext();

        var executor = new ProposeExecutor("propose-0", mcpClient, new PlannerDedupeStore(), null, null, NullLogger.Instance);
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        var output = Assert.Single(context.YieldedOutputs);
        var proposal = Assert.IsType<RemediationProposal>(output);
        Assert.Equal("plan-from-text", proposal.PlanId);
    }

    [Fact]
    public async Task ProposeExecutor_PlanIdNestedInsideArrayInContent_YieldsProposal()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("restart_deployment", new Dictionary<string, object?>(), null);
        var decisionCtx = new DecisionContext(report, decision);

        var mcpClient = new FakeAgentMcpToolset
        {
            ResponseText = """{"Content":[{"planId":"plan-from-content-array"}]}""",
        };
        var context = new FakeWorkflowContext();

        var executor = new ProposeExecutor("propose-0", mcpClient, new PlannerDedupeStore(), null, null, NullLogger.Instance);
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        var output = Assert.Single(context.YieldedOutputs);
        var proposal = Assert.IsType<RemediationProposal>(output);
        Assert.Equal("plan-from-content-array", proposal.PlanId);
    }

    [Fact]
    public async Task ProposeExecutor_PlanIdInContentUpperCase_YieldsProposal()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("restart_deployment", new Dictionary<string, object?>(), null);
        var decisionCtx = new DecisionContext(report, decision);

        var mcpClient = new FakeAgentMcpToolset
        {
            ResponseText = """{"Content":[{"planId":"plan-from-content"}]}""",
        };
        var context = new FakeWorkflowContext();

        var executor = new ProposeExecutor("propose-0", mcpClient, new PlannerDedupeStore(), null, null, NullLogger.Instance);
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        var output = Assert.Single(context.YieldedOutputs);
        var proposal = Assert.IsType<RemediationProposal>(output);
        Assert.Equal("plan-from-content", proposal.PlanId);
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private static AnomalyReport CreateAnomaly(AnomalyStatus status, AnomalyKind kind) =>
        new()
        {
            AnomalyId = "anomaly-executor-test",
            CycleId = "cycle-executor-test",
            DetectedAt = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero),
            Kind = kind,
            Target = new ResourceRef
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",
                Namespace = "mcp-nginx-demo",
                Name = "nginx-demo",
            },
            Severity = Severity.High,
            Status = status,
            Summary = "Test anomaly.",
            Evidence = [],
            Annotations = new Dictionary<string, string>(StringComparer.Ordinal),
        };

    private static AnomalyHandoffBatch CreateBatch(AnomalyReport report) =>
        new()
        {
            CycleId = "cycle-executor-test",
            EmittedAt = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero),
            Reports = [report],
        };

    // ------------------------------------------------------------------ //
    // Custom Fakes (Replacing NSubstitute)
    // ------------------------------------------------------------------ //

    private sealed class FakeWorkflowContext : IWorkflowContext
    {
        public IReadOnlyDictionary<string, string> TraceContext { get; } = new Dictionary<string, string>();
        public bool ConcurrentRunsEnabled { get; } = true;
        public List<object> SentMessages { get; } = [];
        public List<object> YieldedOutputs { get; } = [];

        public ValueTask AddEventAsync(WorkflowEvent @event, CancellationToken cancellationToken = default) => default;
        public ValueTask SendMessageAsync(object message, string? targetId, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message);
            return default;
        }
        public ValueTask YieldOutputAsync(object output, CancellationToken cancellationToken = default)
        {
            YieldedOutputs.Add(output);
            return default;
        }
        public ValueTask RequestHaltAsync() => default;
        public ValueTask<T?> ReadStateAsync<T>(string key, string? scope = null, CancellationToken cancellationToken = default) => new(default(T));
        public ValueTask<T> ReadOrInitStateAsync<T>(string key, Func<T> init, string? scope = null, CancellationToken cancellationToken = default) => new(init());
        public ValueTask<HashSet<string>> ReadStateKeysAsync(string? scope = null, CancellationToken cancellationToken = default) => new([]);
        public ValueTask QueueStateUpdateAsync<T>(string key, T? value, string? scope = null, CancellationToken cancellationToken = default) => default;
        public ValueTask QueueClearScopeAsync(string? scope = null, CancellationToken cancellationToken = default) => default;
    }

    private sealed class FakePlannerAuditOutbox : IPlannerAuditOutbox
    {
        public List<PlannerAuditEntry> Entries { get; } = [];
        public Task<long> AppendAsync(PlannerAuditEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.FromResult(1L);
        }
        public Task<long> AppendAsync(PlannerAuditEntry entry, Npgsql.NpgsqlConnection connection, Npgsql.NpgsqlTransaction transaction, CancellationToken cancellationToken) => Task.FromResult(1L);
    }

    private sealed class FakeAgentMcpToolset : IAgentMcpToolset
    {
        public string GatewayBaseUrl => "http://fake";
        public bool IsConnected => true;
        public string ResponseText { get; set; } = "{}";

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CallToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = ResponseText }] });
        public Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AITool>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeDecideContentGuard(
        ModelVisibleContentAction action,
        bool passthrough = false,
        string? replacementText = null) : IModelVisibleContentGuard
    {
        public string? LastSeenText { get; private set; }
        public ModelVisibleContentSource? LastSeenSource { get; private set; }
        public string? LastSeenToolName { get; private set; }

        public Task<ModelVisibleContentDecision> EvaluateAsync(
            ModelVisibleContent content, CancellationToken cancellationToken)
        {
            LastSeenText = content.Text;
            LastSeenSource = content.Source;
            LastSeenToolName = content.ToolName;
            string text = passthrough ? content.Text : (replacementText ?? content.Text);
            return Task.FromResult(new ModelVisibleContentDecision(
                action, text, [], AgentGuardrailConventions.Reasons.None));
        }
    }

    private sealed class FakeObserverChannel : IObserverChannel
    {
        public int SendToolRequestCallCount { get; private set; }
        public Task<ToolResponsePayload> SendToolRequestAsync(string cycleId, string toolName, string? argumentsJson, CancellationToken cancellationToken = default)
        {
            SendToolRequestCallCount++;
            return Task.FromResult(new ToolResponsePayload { IsError = false, ResultJson = "simulated-response" });
        }
    }
}
