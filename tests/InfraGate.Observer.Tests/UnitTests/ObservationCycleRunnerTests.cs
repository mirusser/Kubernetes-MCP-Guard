using InfraGate.Observer.Classification;
using InfraGate.Observer.Cycle;
using InfraGate.Observer.Prompts;
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
        ObserverOptions? opts = null)
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

        var chatClient = new FixtureChatClient(_ =>
        {
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, llmResponseJson));
        });

        var severityClassifier = new SeverityClassifier();

        var mcpClient = Substitute.For<IObserverMcpClient>();

        var logger = NullLogger<ObservationCycleRunner>.Instance;

        return new ObservationCycleRunner(
            optionsSnapshot,
            optionsMonitor,
            snapshotFetcher,
            systemPromptProvider,
            chatClient,
            severityClassifier,
            mcpClient,
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
        var runner = CreateRunner(ValidLlmJson("High", "PodUnhealthy"));

        var result1 = await runner.RunAsync(CancellationToken.None);
        var result2 = await runner.RunAsync(CancellationToken.None);

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

        var chatClient = new FixtureChatClient(ValidLlmJson());

        var mcpClient = Substitute.For<IObserverMcpClient>();

        var runner = new ObservationCycleRunner(
            optionsSnapshot,
            optionsMonitor,
            snapshotFetcher,
            systemPromptProvider,
            chatClient,
            new SeverityClassifier(),
            mcpClient,
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
        systemPromptProvider.Get(Arg.Any<string>(), Arg.Any<int>()).Returns("prompt");

        var callCount = 0;
        var chatClient = new FixtureChatClient(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    "TOOL_CALL: {\"tool\":\"describe_k8s_resource\",\"arguments\":{\"name\":\"foo\"}}"));
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, ValidLlmJson()));
        });

        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetToolResultAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("{}"));

        var runner = new ObservationCycleRunner(
            optionsSnapshot,
            optionsMonitor,
            snapshotFetcher,
            systemPromptProvider,
            chatClient,
            new SeverityClassifier(),
            mcpClient,
            NullLogger<ObservationCycleRunner>.Instance);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.False(result.IsTruncated);
        Assert.Equal(1, result.ToolCallsUsed);
        Assert.Single(result.Reports);
    }

    [Fact]
    public async Task RunAsync_MaxToolIterationsExceeded_Truncates()
    {
        var options = DefaultOptions() with { MaxToolIterations = 2 };

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
        systemPromptProvider.Get(Arg.Any<string>(), Arg.Any<int>()).Returns("prompt");

        var chatClient = new FixtureChatClient(_ =>
        {
            return new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "TOOL_CALL: {\"tool\":\"get_k8s_status\",\"arguments\":{\"namespace\":\"default\"}}"));
        });

        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetToolResultAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("{}"));

        var runner = new ObservationCycleRunner(
            optionsSnapshot,
            optionsMonitor,
            snapshotFetcher,
            systemPromptProvider,
            chatClient,
            new SeverityClassifier(),
            mcpClient,
            NullLogger<ObservationCycleRunner>.Instance);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.True(result.IsTruncated);
        Assert.Empty(result.Reports);
        Assert.Equal(2, result.ToolCallsUsed);
    }
}
