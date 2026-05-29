using InfraGate.AgentLlm;
using InfraGate.Observer.Classification;
using InfraGate.Observer.Cycle;
using InfraGate.Observer.Prompts;
using InfraGate.Observer.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObservationCycleRunnerTests
{
    private static readonly List<string> DefaultNamespaces = new() { "default" };

    private static ObserverOptions DefaultOptions()
    {
        return new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            AllowedNamespaces = DefaultNamespaces,
            WallClockCapSeconds = 20,
            MaxToolIterations = 8,
        };
    }

    private static IObservationCycleRunner CreateRunner(
        string llmResponseJson,
        ObserverOptions? opts = null,
        IAnomalyHandoffSink? handoffSink = null)
    {
        var options = opts ?? DefaultOptions();

        var optionsSnapshot = Substitute.For<IOptions<ObserverOptions>>();
        optionsSnapshot.Value.Returns(options);

        var optionsMonitor = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        optionsMonitor.CurrentValue.Returns(options);

        var snapshotFetcher = Substitute.For<ISnapshotFetcher>();
        snapshotFetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SnapshotDocument(
                "default", "{}", "{}", "{}", "{}", "{}", "{}",
                DateTimeOffset.UtcNow)));

        var systemPromptProvider = Substitute.For<ISystemPromptProvider>();
        systemPromptProvider.Get(Arg.Any<string>(), Arg.Any<int>())
            .Returns("system prompt");

        var chatClientFactory = new FixtureChatClient(_ =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, llmResponseJson)));

        var severityClassifier = new SeverityClassifier();

        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetReadOnlyToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AITool>>(Array.Empty<AITool>()));

        var dedupeStore = new AnomalyDedupeStore();
        var logger = NullLogger<ObservationCycleRunner>.Instance;

        handoffSink ??= Substitute.For<IAnomalyHandoffSink>();

        return new ObservationCycleRunner(
            optionsMonitor,
            snapshotFetcher,
            systemPromptProvider,
            new ToolCallingAgentFactory(chatClientFactory),
            severityClassifier,
            mcpClient,
            dedupeStore,
            handoffSink,
            logger);
    }

    private static string ValidLlmJson(string severity = "High", string kind = "PodUnhealthy")
    {
        return $$"""
        [
          {
            "Kind": "{{kind}}",
            "Severity": "{{severity}}",
            "Target": {
              "ApiVersion": "v1",
              "Kind": "Pod",
              "Namespace": "default",
              "Name": "crashing-pod"
            },
            "Summary": "Pod is crash-looping",
            "Evidence": [
              {
                "Source": "pod-condition",
                "Content": "CrashLoopBackOff",
                "CapturedAt": "2026-05-23T12:00:00Z"
              }
            ],
            "Suggested": {
              "Action": "Inspect logs",
              "Explanation": "Check logs for crash cause"
            },
            "Annotations": {
              "PodCondition": "CrashLoopBackOff",
              "IsAllPodsAffected": "true"
            }
          }
        ]
        """;
    }

    [Fact]
    public async Task RunAsync_ValidLlmOutput_ReturnsReports()
    {
        var runner = CreateRunner(ValidLlmJson("High", "PodUnhealthy"));

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.False(result.IsTruncated);
        Assert.NotEmpty(result.Reports);
        Assert.Single(result.Reports);

        var report = result.Reports[0];
        Assert.Equal(AnomalyKind.PodUnhealthy, report.Kind);
        Assert.Equal(AnomalyStatus.Active, report.Status);
        Assert.Equal("crashing-pod", report.Target.Name);
        Assert.Equal("default", report.Target.Namespace);
        Assert.Equal("Pod", report.Target.Kind);
        Assert.NotNull(report.AnomalyId);
        Assert.Equal(12, report.AnomalyId.Length);
    }

    [Fact]
    public async Task RunAsync_AnomalyId_IsStableAcrossCalls()
    {
        var json = ValidLlmJson("High", "PodUnhealthy");
        var runner1 = CreateRunner(json);
        var runner2 = CreateRunner(json);

        var result1 = await runner1.RunAsync(CancellationToken.None);
        var result2 = await runner2.RunAsync(CancellationToken.None);

        var id1 = result1.Reports[0].AnomalyId;
        var id2 = result2.Reports[0].AnomalyId;

        Assert.Equal(id1, id2, StringComparer.Ordinal);
        Assert.Equal(12, id1.Length);
    }

    [Fact]
    public async Task RunAsync_DifferentResources_DifferentAnomalyIds()
    {
        var json1 = ValidLlmJson("High", "PodUnhealthy");
        var runner1 = CreateRunner(json1);
        var result1 = await runner1.RunAsync(CancellationToken.None);

        var json2 = """
        [
          {
            "Kind": "DeploymentUnavailable",
            "Severity": "High",
            "Target": {
              "ApiVersion": "apps/v1",
              "Kind": "Deployment",
              "Namespace": "default",
              "Name": "nginx"
            },
            "Summary": "Deployment has no ready pods",
            "Evidence": [],
            "Annotations": { "ReplicasDesired": "3", "ReplicasAvailable": "0" }
          }
        ]
        """;
        var runner2 = CreateRunner(json2);
        var result2 = await runner2.RunAsync(CancellationToken.None);

        Assert.NotEqual(
            result1.Reports[0].AnomalyId,
            result2.Reports[0].AnomalyId,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task RunAsync_SeverityDisagreement_UsesClassifierSeverity()
    {
        var json = """
        [
          {
            "Kind": "PodUnhealthy",
            "Severity": "High",
            "Target": {
              "ApiVersion": "v1",
              "Kind": "Pod",
              "Namespace": "default",
              "Name": "pending-pod"
            },
            "Summary": "Pod is pending",
            "Evidence": [],
            "Annotations": { "IsPending": "true" }
          }
        ]
        """;

        var runner = CreateRunner(json);
        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Single(result.Reports);
        Assert.Equal(Severity.Low, result.Reports[0].Severity);
        Assert.Equal(1, result.SeverityDisagreements);
    }

    [Fact]
    public async Task RunAsync_NoDisagreement_DeltaIsZero()
    {
        var json = ValidLlmJson("High", "PodUnhealthy");

        var runner = CreateRunner(json);
        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Single(result.Reports);
        Assert.Equal(Severity.High, result.Reports[0].Severity);
        Assert.Equal(0, result.SeverityDisagreements);
    }

    [Fact]
    public async Task RunAsync_ShutdownTokenCancelled_Truncates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var runner = CreateRunner(ValidLlmJson());

        var result = await runner.RunAsync(cts.Token);

        Assert.True(result.IsTruncated);
        Assert.Empty(result.Reports);
    }

    [Fact]
    public async Task RunAsync_WallClockCapReached_Truncates()
    {
        var options = DefaultOptions() with { WallClockCapSeconds = 1 };

        var optionsSnapshot = Substitute.For<IOptions<ObserverOptions>>();
        optionsSnapshot.Value.Returns(options);

        var optionsMonitor = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        optionsMonitor.CurrentValue.Returns(options);

        var snapshotFetcher = Substitute.For<ISnapshotFetcher>();
        snapshotFetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                await Task.Delay(2000, ct);
                return new SnapshotDocument("default", "{}", "{}", "{}", "{}", "{}", "{}", DateTimeOffset.UtcNow);
            });

        var systemPromptProvider = Substitute.For<ISystemPromptProvider>();
        systemPromptProvider.Get(Arg.Any<string>(), Arg.Any<int>()).Returns("prompt");

        var chatClientFactory = new FixtureChatClient(ValidLlmJson());
        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetReadOnlyToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AITool>>(Array.Empty<AITool>()));

        var runner = new ObservationCycleRunner(
            optionsMonitor,
            snapshotFetcher,
            systemPromptProvider,
            new ToolCallingAgentFactory(chatClientFactory),
            new SeverityClassifier(),
            mcpClient,
            new AnomalyDedupeStore(),
            Substitute.For<IAnomalyHandoffSink>(),
            NullLogger<ObservationCycleRunner>.Instance);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.True(result.IsTruncated);
        Assert.Empty(result.Reports);
    }

    [Fact]
    public async Task RunAsync_NoNamespaces_ReturnsEmptyReports()
    {
        var options = DefaultOptions() with { AllowedNamespaces = Array.Empty<string>() };

        var runner = CreateRunner(ValidLlmJson(), options);
        var result = await runner.RunAsync(CancellationToken.None);

        Assert.False(result.IsTruncated);
        Assert.Empty(result.Reports);
    }

    [Fact]
    public async Task RunAsync_CycleId_IsAlwaysFresh()
    {
        var runner = CreateRunner(ValidLlmJson());

        var result1 = await runner.RunAsync(CancellationToken.None);
        var result2 = await runner.RunAsync(CancellationToken.None);

        Assert.NotEqual(result1.CycleId, result2.CycleId, StringComparer.Ordinal);
        Assert.Equal(result1.CycleId, result1.Reports[0].CycleId, StringComparer.Ordinal);
    }

    [Fact]
    public async Task RunAsync_InvalidJson_ReturnsEmptyReports()
    {
        var runner = CreateRunner("not valid json at all");

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.False(result.IsTruncated);
        Assert.Empty(result.Reports);
    }

    [Fact]
    public async Task RunAsync_MultipleReportsInOneCycle()
    {
        var json = """
        [
          {
            "Kind": "PodUnhealthy",
            "Severity": "Medium",
            "Target": { "ApiVersion": "v1", "Kind": "Pod", "Namespace": "default", "Name": "crash-pod" },
            "Summary": "Pod A crashing",
            "Evidence": [],
            "Annotations": { "PodCondition": "CrashLoopBackOff", "HasHealthySiblings": "true" }
          },
          {
            "Kind": "PodUnhealthy",
            "Severity": "Low",
            "Target": { "ApiVersion": "v1", "Kind": "Pod", "Namespace": "default", "Name": "pending-pod" },
            "Summary": "Pod B pending",
            "Evidence": [],
            "Annotations": { "IsPending": "true" }
          }
        ]
        """;

        var runner = CreateRunner(json);
        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(2, result.Reports.Count);
        Assert.Equal(Severity.Medium, result.Reports[0].Severity);
        Assert.Equal(Severity.Low, result.Reports[1].Severity);
    }

    [Fact]
    public async Task RunAsync_Evidence_IsParsedCorrectly()
    {
        var runner = CreateRunner(ValidLlmJson("Medium", "PodUnhealthy"));
        var result = await runner.RunAsync(CancellationToken.None);

        var report = result.Reports[0];
        Assert.NotEmpty(report.Evidence);
        Assert.Equal("pod-condition", report.Evidence[0].Source);
        Assert.Equal("CrashLoopBackOff", report.Evidence[0].Content);
    }

    [Fact]
    public async Task RunAsync_RemediationHint_IsParsed()
    {
        var runner = CreateRunner(ValidLlmJson());
        var result = await runner.RunAsync(CancellationToken.None);

        Assert.NotNull(result.Reports[0].Suggested);
        Assert.Equal("Inspect logs", result.Reports[0].Suggested!.Action);
    }

    [Fact]
    public async Task RunAsync_ToolCallsIncrementCounter()
    {
        var options = DefaultOptions();
        var optionsMonitor = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        optionsMonitor.CurrentValue.Returns(options);

        var snapshotFetcher = Substitute.For<ISnapshotFetcher>();
        snapshotFetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SnapshotDocument(
                "default", "{}", "{}", "{}", "{}", "{}", "{}",
                DateTimeOffset.UtcNow)));

        var systemPromptProvider = Substitute.For<ISystemPromptProvider>();
        systemPromptProvider.Get(Arg.Any<string>(), Arg.Any<int>()).Returns("prompt");

        // First LLM call returns a native function call; second returns the final JSON array.
        var llmCallCount = 0;
        var chatClientFactory = new FixtureChatClient(_ =>
        {
            llmCallCount++;
            if (llmCallCount == 1)
            {
                return new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call-1", "describe_k8s_resource",
                        new Dictionary<string, object?> { ["name"] = "foo" })]));
            }
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, ValidLlmJson()));
        });

        // Expose a fake "describe_k8s_resource" tool so FunctionInvokingChatClient can invoke it.
        var fakeTool = AIFunctionFactory.Create(static () => "{}", "describe_k8s_resource");
        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetReadOnlyToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AITool>>(new List<AITool> { fakeTool }));

        var runner = new ObservationCycleRunner(
            optionsMonitor,
            snapshotFetcher,
            systemPromptProvider,
            new ToolCallingAgentFactory(chatClientFactory),
            new SeverityClassifier(),
            mcpClient,
            new AnomalyDedupeStore(),
            Substitute.For<IAnomalyHandoffSink>(),
            NullLogger<ObservationCycleRunner>.Instance);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.False(result.IsTruncated);
        Assert.Equal(1, result.ToolCallsUsed);
        Assert.Single(result.Reports);
    }

    [Fact]
    public async Task RunAsync_MaxToolIterationsExceeded_StopsCallingTools()
    {
        // FunctionInvokingChatClient stops invoking tools after MaxToolIterations.
        // The cycle does NOT truncate — it completes with whatever the LLM last said.
        var options = DefaultOptions() with { MaxToolIterations = 2 };
        var optionsMonitor = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        optionsMonitor.CurrentValue.Returns(options);

        var snapshotFetcher = Substitute.For<ISnapshotFetcher>();
        snapshotFetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SnapshotDocument(
                "default", "{}", "{}", "{}", "{}", "{}", "{}",
                DateTimeOffset.UtcNow)));

        var systemPromptProvider = Substitute.For<ISystemPromptProvider>();
        systemPromptProvider.Get(Arg.Any<string>(), Arg.Any<int>()).Returns("prompt");

        // LLM always requests a tool call → FunctionInvokingChatClient capped at 2 invocations.
        var chatClientFactory = new FixtureChatClient(_ =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "get_k8s_status",
                    new Dictionary<string, object?> { ["namespace"] = "default" })])));

        var fakeTool = AIFunctionFactory.Create(static () => "{}", "get_k8s_status");
        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetReadOnlyToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AITool>>(new List<AITool> { fakeTool }));

        var runner = new ObservationCycleRunner(
            optionsMonitor,
            snapshotFetcher,
            systemPromptProvider,
            new ToolCallingAgentFactory(chatClientFactory),
            new SeverityClassifier(),
            mcpClient,
            new AnomalyDedupeStore(),
            Substitute.For<IAnomalyHandoffSink>(),
            NullLogger<ObservationCycleRunner>.Instance);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.False(result.IsTruncated);
        Assert.Empty(result.Reports);
        Assert.True(result.ToolCallsUsed <= options.MaxToolIterations,
            $"Expected ≤ {options.MaxToolIterations} tool calls, got {result.ToolCallsUsed}");
    }

    [Fact]
    public async Task RunAsync_SnapshotFetchThrows_LogsErrorAndContinues()
    {
        // SnapshotExecutor catches non-OCE exceptions and continues with an empty snapshot
        // so the fan-in chain always completes (graceful degradation).
        var options = DefaultOptions();
        var optionsMonitor = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        optionsMonitor.CurrentValue.Returns(options);

        var snapshotFetcher = Substitute.For<ISnapshotFetcher>();
        snapshotFetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<SnapshotDocument>>(_ =>
                Task.FromException<SnapshotDocument>(new HttpRequestException("Gateway unreachable")));

        var systemPromptProvider = Substitute.For<ISystemPromptProvider>();
        systemPromptProvider.Get(Arg.Any<string>(), Arg.Any<int>()).Returns("prompt");

        // LLM returns empty JSON array for the empty snapshot.
        var chatClientFactory = new FixtureChatClient("[]");
        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetReadOnlyToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AITool>>(Array.Empty<AITool>()));

        var runner = new ObservationCycleRunner(
            optionsMonitor,
            snapshotFetcher,
            systemPromptProvider,
            new ToolCallingAgentFactory(chatClientFactory),
            new SeverityClassifier(),
            mcpClient,
            new AnomalyDedupeStore(),
            Substitute.For<IAnomalyHandoffSink>(),
            NullLogger<ObservationCycleRunner>.Instance);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.False(result.IsTruncated);
        Assert.Empty(result.Reports);
    }

    // ── Dedupe integration ───────────────────────────────────────

    [Fact]
    public async Task RunAsync_SameAnomalyAcrossCycles_SuppressesWithinWindow()
    {
        var sharedDedupeStore = new AnomalyDedupeStore();
        var json = ValidLlmJson("High", "PodUnhealthy");

        var result1 = await CreateRunnerWithDedupe(json, sharedDedupeStore).RunAsync(CancellationToken.None);
        Assert.Single(result1.Reports);
        Assert.Equal(AnomalyStatus.Active, result1.Reports[0].Status);

        var result2 = await CreateRunnerWithDedupe(json, sharedDedupeStore).RunAsync(CancellationToken.None);
        Assert.Empty(result2.Reports);
    }

    [Fact]
    public async Task RunAsync_DifferentAnomalies_AllEmitFirstTime()
    {
        var sharedDedupeStore = new AnomalyDedupeStore();

        var podJson = ValidLlmJson("Medium", "PodUnhealthy");
        var deploymentJson = """
        [
          {
            "Kind": "DeploymentUnavailable",
            "Severity": "High",
            "Target": { "ApiVersion": "apps/v1", "Kind": "Deployment", "Namespace": "default", "Name": "nginx" },
            "Summary": "Deployment unavailable",
            "Evidence": [],
            "Annotations": { "ReplicasDesired": "3", "ReplicasAvailable": "0" }
          }
        ]
        """;

        var result1 = await CreateRunnerWithDedupe(podJson, sharedDedupeStore).RunAsync(CancellationToken.None);
        var result2 = await CreateRunnerWithDedupe(deploymentJson, sharedDedupeStore).RunAsync(CancellationToken.None);

        Assert.Single(result1.Reports);
        Assert.Single(result2.Reports);
        Assert.NotEqual(result1.Reports[0].AnomalyId, result2.Reports[0].AnomalyId);
    }

    [Fact]
    public async Task RunAsync_TruncatedCycle_DoesNotConsumeSuppressionWindow()
    {
        var sharedDedupeStore = new AnomalyDedupeStore();
        var json = ValidLlmJson("High", "PodUnhealthy");

        // First cycle emits normally
        var result1 = await CreateRunnerWithDedupe(json, sharedDedupeStore).RunAsync(CancellationToken.None);
        Assert.Single(result1.Reports);

        // Create runner with 1s cap that will truncate
        var options = DefaultOptions() with { WallClockCapSeconds = 1 };
        var optionsSnapshot = Substitute.For<IOptions<ObserverOptions>>();
        optionsSnapshot.Value.Returns(options);
        var optionsMonitor = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        optionsMonitor.CurrentValue.Returns(options);

        var snapshotFetcher = Substitute.For<ISnapshotFetcher>();
        snapshotFetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                await Task.Delay(2000, ct);
                return new SnapshotDocument("default", "{}", "{}", "{}", "{}", "{}", "{}", DateTimeOffset.UtcNow);
            });

        var systemPromptProvider = Substitute.For<ISystemPromptProvider>();
        systemPromptProvider.Get(Arg.Any<string>(), Arg.Any<int>()).Returns("prompt");

        var chatClientFactory = new FixtureChatClient(json);
        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetReadOnlyToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AITool>>(Array.Empty<AITool>()));

        var truncatedRunner = new ObservationCycleRunner(
            optionsMonitor,
            snapshotFetcher,
            systemPromptProvider,
            new ToolCallingAgentFactory(chatClientFactory),
            new SeverityClassifier(),
            mcpClient,
            sharedDedupeStore,
            Substitute.For<IAnomalyHandoffSink>(),
            NullLogger<ObservationCycleRunner>.Instance);

        var truncatedResult = await truncatedRunner.RunAsync(CancellationToken.None);
        Assert.True(truncatedResult.IsTruncated);
        Assert.Empty(truncatedResult.Reports);

        // After truncated cycle, the same anomaly should still be in the suppression window
        // (truncated cycle didn't advance the counter)
        var result3 = await CreateRunnerWithDedupe(json, sharedDedupeStore).RunAsync(CancellationToken.None);
        Assert.Empty(result3.Reports);
    }

    [Fact]
    public async Task RunAsync_ResolutionEmission_WhenAnomalyFixed()
    {
        var sharedDedupeStore = new AnomalyDedupeStore();
        var json = ValidLlmJson("High", "PodUnhealthy");
        var resolverOptions = DefaultOptions() with { DedupeResolutionThreshold = 2 };

        // Cycle 1: anomaly detected
        var runner1 = CreateRunnerWithDedupe(json, sharedDedupeStore, resolverOptions);
        var result1 = await runner1.RunAsync(CancellationToken.None);
        Assert.Single(result1.Reports);
        Assert.Equal(AnomalyStatus.Active, result1.Reports[0].Status);

        // Cycle 2: no anomalies (empty LLM output)
        var emptyJson = "[]";
        var runner2 = CreateRunnerWithDedupe(emptyJson, sharedDedupeStore, resolverOptions);
        var result2 = await runner2.RunAsync(CancellationToken.None);
        Assert.Empty(result2.Reports);

        // Cycle 3: still absent — resolved emitted
        var result3 = await CreateRunnerWithDedupe(emptyJson, sharedDedupeStore, resolverOptions).RunAsync(CancellationToken.None);
        Assert.Single(result3.Reports);
        Assert.Equal(AnomalyStatus.Resolved, result3.Reports[0].Status);
        Assert.Equal(Severity.Low, result3.Reports[0].Severity);
    }

    private static IObservationCycleRunner CreateRunnerWithDedupe(
        string llmResponseJson,
        IAnomalyDedupeStore dedupeStore,
        ObserverOptions? opts = null,
        IAnomalyHandoffSink? handoffSink = null)
    {
        var options = opts ?? DefaultOptions();
        var optionsSnapshot = Substitute.For<IOptions<ObserverOptions>>();
        optionsSnapshot.Value.Returns(options);
        var optionsMonitor = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        optionsMonitor.CurrentValue.Returns(options);

        var snapshotFetcher = Substitute.For<ISnapshotFetcher>();
        snapshotFetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SnapshotDocument(
                "default", "{}", "{}", "{}", "{}", "{}", "{}",
                DateTimeOffset.UtcNow)));

        var systemPromptProvider = Substitute.For<ISystemPromptProvider>();
        systemPromptProvider.Get(Arg.Any<string>(), Arg.Any<int>()).Returns("system prompt");

        var chatClientFactory = new FixtureChatClient(_ =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, llmResponseJson)));

        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetReadOnlyToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AITool>>(Array.Empty<AITool>()));

        handoffSink ??= Substitute.For<IAnomalyHandoffSink>();

        return new ObservationCycleRunner(
            optionsMonitor,
            snapshotFetcher,
            systemPromptProvider,
            new ToolCallingAgentFactory(chatClientFactory),
            new SeverityClassifier(),
            mcpClient,
            dedupeStore,
            handoffSink,
            NullLogger<ObservationCycleRunner>.Instance);
    }

    // ── Handoff sink integration ─────────────────────────────────

    [Fact]
    public async Task RunAsync_PublishesBatchToHandoffSink()
    {
        var handoffSink = Substitute.For<IAnomalyHandoffSink>();
        var runner = CreateRunner(ValidLlmJson("High", "PodUnhealthy"), handoffSink: handoffSink);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.False(result.IsTruncated);
        Assert.NotEmpty(result.Reports);

        await handoffSink.Received(1).PublishAsync(
            Arg.Is<AnomalyHandoffBatch>(b =>
                b.Reports.Count == 1 &&
                b.Reports[0].AnomalyId == result.Reports[0].AnomalyId &&
                b.Reports[0].Kind == result.Reports[0].Kind),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_EmptyBatch_DoesNotPublish()
    {
        var handoffSink = Substitute.For<IAnomalyHandoffSink>();
        var options = DefaultOptions() with { AllowedNamespaces = Array.Empty<string>() };
        var runner = CreateRunner(ValidLlmJson(), options, handoffSink);

        await runner.RunAsync(CancellationToken.None);

        await handoffSink.DidNotReceive().PublishAsync(
            Arg.Any<AnomalyHandoffBatch>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TruncatedCycle_DoesNotPublish()
    {
        var handoffSink = Substitute.For<IAnomalyHandoffSink>();
        var options = DefaultOptions() with { WallClockCapSeconds = 1 };

        var optionsSnapshot = Substitute.For<IOptions<ObserverOptions>>();
        optionsSnapshot.Value.Returns(options);
        var optionsMonitor = Substitute.For<IOptionsMonitor<ObserverOptions>>();
        optionsMonitor.CurrentValue.Returns(options);

        var snapshotFetcher = Substitute.For<ISnapshotFetcher>();
        snapshotFetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                await Task.Delay(2000, ct);
                return new SnapshotDocument("default", "{}", "{}", "{}", "{}", "{}", "{}", DateTimeOffset.UtcNow);
            });

        var systemPromptProvider = Substitute.For<ISystemPromptProvider>();
        systemPromptProvider.Get(Arg.Any<string>(), Arg.Any<int>()).Returns("prompt");

        var chatClientFactory = new FixtureChatClient(ValidLlmJson());
        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetReadOnlyToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AITool>>(Array.Empty<AITool>()));

        var runner = new ObservationCycleRunner(
            optionsMonitor,
            snapshotFetcher,
            systemPromptProvider,
            new ToolCallingAgentFactory(chatClientFactory),
            new SeverityClassifier(),
            mcpClient,
            new AnomalyDedupeStore(),
            handoffSink,
            NullLogger<ObservationCycleRunner>.Instance);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.True(result.IsTruncated);

        await handoffSink.DidNotReceive().PublishAsync(
            Arg.Any<AnomalyHandoffBatch>(),
            Arg.Any<CancellationToken>());
    }
}
