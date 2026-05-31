using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Execution;
using InfraGate.KubernetesAdapter;

namespace InfraGate.KubernetesAdapter.Tests.UnitTests;

public sealed class KubernetesDomainAdapterTests
{
    private static readonly PlanEnvelope TestEnvelope = new();
    private static readonly PlanRequester TestRequester = new("test-subject", "oauth-jwt");
    private static readonly ApprovalPolicy TestPolicy = ApprovalPolicy.SameSubject();

    [Fact]
    public void AdapterId_DelegatesToPlanReviewAdapter()
    {
        var reviewAdapter = new StubPlanReviewAdapter("kubernetes");
        var adapter = new KubernetesDomainAdapter(
            new StubPlanBuilder(PlanBuildResult.Failed("n/a")),
            new StubPlanExecutor(),
            reviewAdapter);

        Assert.Equal("kubernetes", adapter.AdapterId);
    }

    [Fact]
    public async Task BuildAsync_DelegatesToPlanBuilder()
    {
        var expectedResult = PlanBuildResult.Failed("validation failed");
        var builder = new StubPlanBuilder(expectedResult);
        var adapter = new KubernetesDomainAdapter(
            builder,
            new StubPlanExecutor(),
            new StubPlanReviewAdapter("kubernetes"));
        var args = new Dictionary<string, object?>(StringComparer.Ordinal) { ["key"] = "value" };

        var result = await adapter.BuildAsync(
            "restart_deployment", args, TestRequester, TestPolicy, CancellationToken.None);

        Assert.Same(expectedResult, result);
        Assert.Equal("restart_deployment", builder.LastMutationToolName);
        Assert.Same(args, builder.LastArguments);
        Assert.Equal(TestRequester, builder.LastRequester);
        Assert.Equal(TestPolicy, builder.LastPolicy);
    }

    [Fact]
    public async Task CheckPreExecutionAsync_DelegatesToPlanExecutor()
    {
        var expectedResult = DomainPlanExecutionResult.Blocked("pre-check failed");
        var executor = new StubPlanExecutor { CheckPreExecutionResult = expectedResult };
        var adapter = new KubernetesDomainAdapter(
            new StubPlanBuilder(PlanBuildResult.Failed("n/a")),
            executor,
            new StubPlanReviewAdapter("kubernetes"));

        var result = await adapter.CheckPreExecutionAsync(TestEnvelope, CancellationToken.None);

        Assert.Same(expectedResult, result);
        Assert.Same(TestEnvelope, executor.LastCheckedEnvelope);
    }

    [Fact]
    public async Task ExecuteAsync_DelegatesToPlanExecutor()
    {
        var expectedResult = DomainPlanExecutionResult.Success("ok", "default");
        var executor = new StubPlanExecutor { ExecuteResult = expectedResult };
        var adapter = new KubernetesDomainAdapter(
            new StubPlanBuilder(PlanBuildResult.Failed("n/a")),
            executor,
            new StubPlanReviewAdapter("kubernetes"));

        var result = await adapter.ExecuteAsync(TestEnvelope, CancellationToken.None);

        Assert.Same(expectedResult, result);
        Assert.Same(TestEnvelope, executor.LastExecutedEnvelope);
    }

    [Fact]
    public void TryDecodeForReview_DelegatesToPlanReviewAdapter()
    {
        var reviewAdapter = new StubPlanReviewAdapter("kubernetes");
        var adapter = new KubernetesDomainAdapter(
            new StubPlanBuilder(PlanBuildResult.Failed("n/a")),
            new StubPlanExecutor(),
            reviewAdapter);

        var review = adapter.TryDecodeForReview(TestEnvelope, out string? error);

        Assert.Null(review);
        Assert.Null(error);
        Assert.Same(TestEnvelope, reviewAdapter.LastEnvelope);
    }

    private sealed class StubPlanBuilder(PlanBuildResult result) : IDomainPlanBuilder
    {
        public string? LastMutationToolName { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastArguments { get; private set; }
        public PlanRequester? LastRequester { get; private set; }
        public ApprovalPolicy? LastPolicy { get; private set; }

        public Task<PlanBuildResult> BuildAsync(
            string mutationToolName,
            IReadOnlyDictionary<string, object?> arguments,
            PlanRequester requester,
            ApprovalPolicy approvalPolicy,
            CancellationToken ct)
        {
            LastMutationToolName = mutationToolName;
            LastArguments = arguments;
            LastRequester = requester;
            LastPolicy = approvalPolicy;
            return Task.FromResult(result);
        }
    }

    private sealed class StubPlanExecutor : IDomainPlanExecutor
    {
        public DomainPlanExecutionResult CheckPreExecutionResult { get; init; } =
            DomainPlanExecutionResult.Blocked("not configured");
        public DomainPlanExecutionResult ExecuteResult { get; init; } =
            DomainPlanExecutionResult.Blocked("not configured");
        public PlanEnvelope? LastCheckedEnvelope { get; private set; }
        public PlanEnvelope? LastExecutedEnvelope { get; private set; }

        public Task<DomainPlanExecutionResult> CheckPreExecutionAsync(PlanEnvelope envelope, CancellationToken ct)
        {
            LastCheckedEnvelope = envelope;
            return Task.FromResult(CheckPreExecutionResult);
        }

        public Task<DomainPlanExecutionResult> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct)
        {
            LastExecutedEnvelope = envelope;
            return Task.FromResult(ExecuteResult);
        }
    }

    private sealed class StubPlanReviewAdapter(string adapterId) : IPlanReviewAdapter
    {
        public string AdapterId => adapterId;
        public PlanEnvelope? LastEnvelope { get; private set; }

        public IPlanReview? TryDecodeForReview(PlanEnvelope envelope, out string? error)
        {
            LastEnvelope = envelope;
            error = null;
            return null;
        }
    }
}
