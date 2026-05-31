using InfraGate.Observer.Classification;
using InfraGate.Observer.Contracts;
using InfraGate.Observer.Cycle.Workflow;
using InfraGate.Observer.Diagnostics;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class AnomalyParseExecutorTests
{
    private const string TestNamespace = "test-ns";
    private const string TestCycleId = "cycle-1";

    private static SeverityClassifier CreateClassifier() => new();

    private static AnomalyParseExecutor CreateExecutor(
        CapturingLogger<AnomalyParseExecutor> logger,
        Func<int>? toolCallCount = null) =>
        new("parse-0", TestNamespace, TestCycleId,
            toolCallCount ?? (() => 0), CreateClassifier(), logger);

    private static List<ChatMessage> AssistantMessage(string json) =>
        [new ChatMessage(ChatRole.Assistant, json)];

    private static string LlmJson(string kind, string severity, string targetJson)
    {
        return $$"""
        [
          {
            "Kind": "{{kind}}",
            "Severity": "{{severity}}",
            "Target": {{targetJson}},
            "Summary": "test summary"
          }
        ]
        """;
    }

    private static string ValidTargetJson(
        string name = "test-pod",
        string kind = "Pod",
        string ns = "default") =>
        $$"""{"ApiVersion":"v1","Kind":"{{kind}}","Namespace":"{{ns}}","Name":"{{name}}"}""";

    [Fact]
    public async Task HandleAsync_ValidPodUnhealthy_CreatesAnomalyReport()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger, () => 3);

        var json = LlmJson("PodUnhealthy", "High", ValidTargetJson());
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        var report = Assert.Single(result.Reports);
        Assert.Equal(AnomalyKind.PodUnhealthy, report.Kind);
        Assert.Equal(3, result.ToolCallsUsed);
    }

    [Fact]
    public async Task HandleAsync_NoAssistantMessage_ReturnsEmptyReports()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var messages = new List<ChatMessage> { new(ChatRole.User, "something") };
        var result = await executor.HandleAsync(messages, new NullWorkflowContext());

        Assert.Empty(result.Reports);
    }

    [Fact]
    public async Task HandleAsync_NoJsonArrayInText_LogsAndReturnsEmpty()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var result = await executor.HandleAsync(
            AssistantMessage("just plain text, no JSON here"),
            new NullWorkflowContext());

        Assert.Empty(result.Reports);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning &&
            e.Message.Contains("Failed to extract JSON array"));
    }

    [Fact]
    public async Task HandleAsync_MalformedJson_LogsAndReturnsEmpty()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var result = await executor.HandleAsync(
            AssistantMessage("[not valid json]"),
            new NullWorkflowContext());

        Assert.Empty(result.Reports);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData("PodUnhealthy", AnomalyKind.PodUnhealthy)]
    [InlineData("DeploymentUnavailable", AnomalyKind.DeploymentUnavailable)]
    [InlineData("ServiceNoEndpoints", AnomalyKind.ServiceNoEndpoints)]
    [InlineData("WarningEvent", AnomalyKind.WarningEvent)]
    [InlineData("UnknownKind", AnomalyKind.WarningEvent)]
    public async Task HandleAsync_AnomalyKinds_MapsCorrectly(string llmKind, AnomalyKind expected)
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = LlmJson(llmKind, "High", ValidTargetJson());
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        var report = Assert.Single(result.Reports);
        Assert.Equal(expected, report.Kind);
    }

    [Theory]
    [InlineData("High")]
    [InlineData("Medium")]
    [InlineData("Low")]
    [InlineData("Unknown")]
    public async Task HandleAsync_Severities_AllProduceValidReports(string llmSeverity)
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = LlmJson("PodUnhealthy", llmSeverity, ValidTargetJson());
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        var report = Assert.Single(result.Reports);
        Assert.Equal(AnomalyKind.PodUnhealthy, report.Kind);
    }

    [Fact]
    public async Task HandleAsync_NullTarget_SkipsThatAnomaly()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = """
        [
          { "Kind": "PodUnhealthy", "Severity": "High", "Target": null, "Summary": "skipped" },
          { "Kind": "PodUnhealthy", "Severity": "High", "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"default","Name":"kept-pod"}, "Summary": "kept" }
        ]
        """;
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        var report = Assert.Single(result.Reports);
        Assert.Equal("kept-pod", report.Target.Name);
    }

    [Fact]
    public async Task HandleAsync_EmptyTargetName_SkipsThatAnomaly()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = LlmJson("PodUnhealthy", "High", """{"ApiVersion":"v1","Kind":"Pod","Namespace":"default","Name":"  "}""");
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        Assert.Empty(result.Reports);
    }

    [Fact]
    public async Task HandleAsync_NullAnnotations_StillGetsMatchedRule()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = """
        [
          { "Kind": "PodUnhealthy", "Severity": "High", "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"default","Name":"test-pod"}, "Summary": "test", "Annotations": null }
        ]
        """;
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        var report = Assert.Single(result.Reports);
        Assert.Contains("MatchedRule", report.Annotations.Keys);
    }

    [Fact]
    public async Task HandleAsync_NullEvidence_UsesEmptyList()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = """
        [
          { "Kind": "PodUnhealthy", "Severity": "High", "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"default","Name":"test-pod"}, "Summary": "test", "Evidence": null }
        ]
        """;
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        var report = Assert.Single(result.Reports);
        Assert.Empty(report.Evidence);
    }

    [Fact]
    public async Task HandleAsync_NullSuggested_UsesNullRemediationHint()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = """
        [
          { "Kind": "PodUnhealthy", "Severity": "High", "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"default","Name":"test-pod"}, "Summary": "test", "Suggested": null }
        ]
        """;
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        var report = Assert.Single(result.Reports);
        Assert.Null(report.Suggested);
    }

    [Fact]
    public async Task HandleAsync_EvidenceWithMissingSourceAndContent_FiltersOut()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = """
        [
          { "Kind": "PodUnhealthy", "Severity": "High", "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"default","Name":"test-pod"}, "Summary": "test",
            "Evidence": [ {"Source":"", "Content":""}, {"Source":"valid", "Content":"something"} ] }
        ]
        """;
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        var report = Assert.Single(result.Reports);
        Assert.Single(report.Evidence);
        Assert.Equal("valid", report.Evidence[0].Source);
    }

    [Fact]
    public async Task HandleAsync_InvalidCapturedAt_FallsBackToUtcNow()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var before = DateTimeOffset.UtcNow;
        var json = """
        [
          { "Kind": "PodUnhealthy", "Severity": "High", "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"default","Name":"test-pod"}, "Summary": "test",
            "Evidence": [ {"Source":"log", "Content":"error", "CapturedAt":"not-a-date"} ] }
        ]
        """;
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());
        var after = DateTimeOffset.UtcNow;

        var report = Assert.Single(result.Reports);
        Assert.Single(report.Evidence);
        Assert.InRange(report.Evidence[0].CapturedAt, before, after);
    }

    [Fact]
    public async Task HandleAsync_MultipleAnomalies_ReturnsAll()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = """
        [
          { "Kind": "PodUnhealthy", "Severity": "High", "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"ns1","Name":"pod-a"}, "Summary": "first" },
          { "Kind": "DeploymentUnavailable", "Severity": "Medium", "Target": {"ApiVersion":"apps/v1","Kind":"Deployment","Namespace":"ns1","Name":"deploy-b"}, "Summary": "second" }
        ]
        """;
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        Assert.Equal(2, result.Reports.Count);
    }

    [Fact]
    public async Task HandleAsync_AnnotationsWithNumberValue_ConvertsToRawText()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = $$""""
        [
          { "Kind": "PodUnhealthy", "Severity": "High",
            "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"{{TestNamespace}}","Name":"pod-1"},
            "Summary": "test",
            "Annotations": { "ReplicasDesired": 3, "ReplicasAvailable": 2 } }
        ]
        """";
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        Assert.Single(result.Reports);
        Assert.Equal("3", result.Reports[0].Annotations["ReplicasDesired"]);
        Assert.Equal("2", result.Reports[0].Annotations["ReplicasAvailable"]);
    }

    [Fact]
    public async Task HandleAsync_AnnotationsWithBooleanValues_ConvertsToTrueFalse()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = $$""""
        [
          { "Kind": "PodUnhealthy", "Severity": "High",
            "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"{{TestNamespace}}","Name":"pod-1"},
            "Summary": "test",
            "Annotations": { "IsAllPodsAffected": true, "IsPending": false } }
        ]
        """";
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        Assert.Single(result.Reports);
        Assert.Equal("true", result.Reports[0].Annotations["IsAllPodsAffected"]);
        Assert.Equal("false", result.Reports[0].Annotations["IsPending"]);
    }

    [Fact]
    public async Task HandleAsync_AnnotationsWithNullStringValue_HandlesJsonNull()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = $$""""
        [
          { "Kind": "PodUnhealthy", "Severity": "High",
            "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"{{TestNamespace}}","Name":"pod-1"},
            "Summary": "test",
            "Annotations": { "PodCondition": null } }
        ]
        """";
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        Assert.Single(result.Reports);
        Assert.Equal("null", result.Reports[0].Annotations["PodCondition"]);
    }

    [Fact]
    public async Task HandleAsync_NullSummary_UsesEmptyString()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = $$""""
        [
          { "Kind": "PodUnhealthy", "Severity": "High",
            "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"{{TestNamespace}}","Name":"pod-1"},
            "Summary": null }
        ]
        """";
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        Assert.Single(result.Reports);
        Assert.Equal(string.Empty, result.Reports[0].Summary);
    }

    [Fact]
    public async Task HandleAsync_TargetWithNullApiVersionKindNamespace_UsesDefaults()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = $$""""
        [
          { "Kind": "PodUnhealthy", "Severity": "High",
            "Target": {"Name":"pod-1"},
            "Summary": "test" }
        ]
        """";
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        Assert.Single(result.Reports);
        Assert.Equal("v1", result.Reports[0].Target.ApiVersion);
        Assert.Equal("Unknown", result.Reports[0].Target.Kind);
        Assert.Equal(TestNamespace, result.Reports[0].Target.Namespace);
    }

    [Fact]
    public async Task HandleAsync_UnknownAnomalyKind_DefaultsToWarningEvent()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = $$""""
        [
          { "Kind": "SomethingUnknown", "Severity": "High",
            "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"{{TestNamespace}}","Name":"pod-1"},
            "Summary": "test" }
        ]
        """";
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        Assert.Single(result.Reports);
        Assert.Equal(AnomalyKind.WarningEvent, result.Reports[0].Kind);
    }

    [Fact]
    public async Task HandleAsync_JsonWithEscapedQuotesInString_ExtractsCorrectly()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        // Natural language text with a JSON array embedded, containing escaped quotes.
        var text = $$"""
        Here is the analysis:
        [
          { "Kind": "PodUnhealthy", "Severity": "High",
            "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"test-ns","Name":"pod-1"},
            "Summary": "pod has \\\"issues\\\" with readiness" }
        ]
        End of report.
        """;
        var result = await executor.HandleAsync(AssistantMessage(text), new NullWorkflowContext());

        Assert.Single(result.Reports);
        Assert.Contains("issues", result.Reports[0].Summary);
    }

    [Fact]
    public async Task HandleAsync_EvidenceItemWithBothNullSourceAndContent_FiltersOut()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = $$""""
        [
          { "Kind": "PodUnhealthy", "Severity": "High",
            "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"{{TestNamespace}}","Name":"pod-1"},
            "Summary": "test",
            "Evidence": [
              { "Source": null, "Content": null },
              { "Source": "api", "Content": "data" }
            ] }
        ]
        """";
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        Assert.Single(result.Reports);
        Assert.Single(result.Reports[0].Evidence);
        Assert.Equal("api", result.Reports[0].Evidence[0].Source);
    }

    [Fact]
    public async Task HandleAsync_NullContext_DoesNotThrow()
    {
        var logger = new CapturingLogger<AnomalyParseExecutor>();
        var executor = CreateExecutor(logger);

        var json = $$""""
        [
          { "Kind": "PodUnhealthy", "Severity": "High",
            "Target": {"ApiVersion":"v1","Kind":"Pod","Namespace":"{{TestNamespace}}","Name":"pod-1"},
            "Summary": "test" }
        ]
        """";
        var result = await executor.HandleAsync(AssistantMessage(json), new NullWorkflowContext());

        Assert.Single(result.Reports);
    }

    private sealed class NullWorkflowContext : IWorkflowContext
    {
        public IReadOnlyDictionary<string, string> TraceContext { get; } = new Dictionary<string, string>();
        public bool ConcurrentRunsEnabled => false;

        public ValueTask AddEventAsync(WorkflowEvent @event, CancellationToken ct = default) => default;
        public ValueTask SendMessageAsync(object message, string? targetId, CancellationToken ct = default) => default;
        public ValueTask YieldOutputAsync(object output, CancellationToken ct = default) => default;
        public ValueTask RequestHaltAsync() => default;
        public ValueTask<T?> ReadStateAsync<T>(string key, string? scope = null, CancellationToken ct = default) => new(default(T));
        public ValueTask<T> ReadOrInitStateAsync<T>(string key, Func<T> init, string? scope = null, CancellationToken ct = default) => new(init());
        public ValueTask<HashSet<string>> ReadStateKeysAsync(string? scope = null, CancellationToken ct = default) => new([]);
        public ValueTask QueueStateUpdateAsync<T>(string key, T? value, string? scope = null, CancellationToken ct = default) => default;
        public ValueTask QueueClearScopeAsync(string? scope = null, CancellationToken ct = default) => default;
    }
}
