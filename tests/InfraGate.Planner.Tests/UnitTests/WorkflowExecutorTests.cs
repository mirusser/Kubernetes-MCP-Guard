using System.Collections.Concurrent;
using System.Text.Json;
using InfraGate.AgentLlm;
using InfraGate.Observer.Contracts;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Cycle.Workflow;
using InfraGate.Planner.Decision;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using InfraGate.Planner.Mcp;
using InfraGate.Remediation.Contracts;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

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

        var executor = new ValidateExecutor("validate-0", new ConcurrentDictionary<string, byte>(), new PlannerDedupeStore(), null, null, NullLogger.Instance);
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task ValidateExecutor_ValidOperation_ForwardsDecisionContext()
    {
        var report = CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable);
        var decision = new RemediationDecision("restart_deployment", new Dictionary<string, object?> { ["name"] = "nginx", ["namespace"] = "default" }, null);
        var decisionCtx = new DecisionContext(report, decision);
        var context = new FakeWorkflowContext();

        var executor = new ValidateExecutor("validate-0", new ConcurrentDictionary<string, byte>(), new PlannerDedupeStore(), null, null, NullLogger.Instance);
        await executor.HandleAsync(decisionCtx, context, CancellationToken.None);

        var forwarded = Assert.Single(context.SentMessages);
        var forwardedCtx = Assert.IsType<DecisionContext>(forwarded);
        Assert.Equal("restart_deployment", forwardedCtx.Decision.OperationType);
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

        var mcpClient = new FakePlannerMcpClient { Response = """{"planId":"plan-123"}""" };
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

    private sealed class FakePlannerMcpClient : IPlannerMcpClient
    {
        public string GatewayBaseUrl => "http://fake";
        public bool IsConnected => true;
        public string Response { get; set; } = "{}";

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken) => Task.FromResult(Response);
        public Task<IReadOnlyList<AITool>> GetReadOnlyToolsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AITool>>([]);
    }
}
