using InfraGate.Approvals;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ApprovalStoreTests
{
    private const string AdapterId = "test-adapter";
    private const string Operation = "test-operation";
    private const string Renderer = "test-renderer-v1";
    private const string Subject = "requester";
    private const string TargetNamespace = "test-namespace";

    [Fact]
    public async Task GetPlanStatusAsync_NoFiles_ReturnsNotFound()
    {
        var store = CreateStore();

        var result = await store.GetPlanStatusAsync(ApprovalIds.NewPlanId(), CancellationToken.None);

        Assert.Equal(PlanStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetPlanStatusAsync_PendingFileOnly_ReturnsApprovalRequired()
    {
        var store = CreateStore();
        var created = await CreatePlanAsync(store);

        var result = await store.GetPlanStatusAsync(created.Envelope.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.ApprovalRequired, result.Status);
    }

    [Fact]
    public async Task GetPlanStatusAsync_ValidGrant_ReturnsApproved()
    {
        var store = CreateStore();
        var created = await CreatePlanAsync(store);
        await store.CreateGrantAsync(created.Envelope, Subject, "challenge-1", CancellationToken.None);

        var result = await store.GetPlanStatusAsync(created.Envelope.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.Approved, result.Status);
    }

    [Fact]
    public async Task GetPlanStatusAsync_ExpiredGrant_ReturnsExpired()
    {
        var store = CreateStore();
        var created = await CreatePlanAsync(store, DateTimeOffset.UtcNow.AddHours(-2));
        await store.CreateGrantAsync(created.Envelope, Subject, "challenge-1", CancellationToken.None);

        var result = await store.GetPlanStatusAsync(created.Envelope.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.Expired, result.Status);
    }

    [Fact]
    public async Task GetPlanStatusAsync_AppliedFile_ReturnsApplied()
    {
        var store = CreateStore();
        string planId = ApprovalIds.NewPlanId();
        Directory.CreateDirectory(store.AppliedDirectory);
        await File.WriteAllTextAsync(store.GetAppliedPath(planId), "{}", CancellationToken.None);

        var result = await store.GetPlanStatusAsync(planId, CancellationToken.None);

        Assert.Equal(PlanStatus.Applied, result.Status);
    }

    [Fact]
    public async Task GetPlanStatusAsync_AppliedFileAndGrant_ReturnsApplied()
    {
        var store = CreateStore();
        var created = await CreatePlanAsync(store);
        var grant = await store.CreateGrantAsync(created.Envelope, Subject, "challenge-1", CancellationToken.None);
        await store.MarkAppliedAsync(created.Envelope, TargetNamespace, grant, CancellationToken.None);

        var result = await store.GetPlanStatusAsync(created.Envelope.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.Applied, result.Status);
    }

    [Fact]
    public async Task GetPendingPlanAsync_OperatorApprovalPolicy_RoundTripsParameters()
    {
        var store = CreateStore();
        var created = await CreatePlanAsync(store, approvalPolicy: ApprovalPolicy.OperatorApproval("kubernetes-operators"));

        var pending = await store.GetPendingPlanAsync(created.Envelope.Id, CancellationToken.None);

        Assert.True(pending.IsPending);
        Assert.Equal(created.Envelope.ApprovalPolicy, pending.Envelope?.ApprovalPolicy);
        Assert.Equal(
            "kubernetes-operators",
            pending.Envelope?.ApprovalPolicy.Parameters?[ApprovalConventions.ApprovalPolicyParameters.OperatorGroup]);
    }

    [Fact]
    public async Task GetGrantAsync_OperatorApprovalPolicy_RoundTripsParameters()
    {
        var store = CreateStore();
        var created = await CreatePlanAsync(store, approvalPolicy: ApprovalPolicy.OperatorApproval("kubernetes-operators"));

        await store.CreateGrantAsync(created.Envelope, "operator-user", "challenge-1", CancellationToken.None);
        var grant = await store.GetGrantAsync(created.Envelope.Id, CancellationToken.None);

        Assert.Equal(created.Envelope.ApprovalPolicy, grant?.ApprovalPolicy);
        Assert.Equal(
            "kubernetes-operators",
            grant?.ApprovalPolicy.Parameters?[ApprovalConventions.ApprovalPolicyParameters.OperatorGroup]);
    }

    private static ApprovalStore CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-plan-status-tests", Guid.NewGuid().ToString("N"));

        return new ApprovalStore(new ApprovalStoreOptions(root));
    }

    private static Task<ApprovalPlanResult> CreatePlanAsync(
        ApprovalStore store,
        DateTimeOffset? createdAtUtc = null,
        ApprovalPolicy? approvalPolicy = null)
    {
        DateTimeOffset createdAt = createdAtUtc ?? DateTimeOffset.UtcNow;
        var envelope = PlanEnvelopeFactory.Create(
            ApprovalIds.NewPlanId(),
            AdapterId,
            Operation,
            createdAt,
            new PlanRequester(Subject, "test"),
            ApprovalDigest.ComputeSha256("test.intent.v1", new { operation = Operation }),
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, Renderer),
            new TestPlanPayload("demo"),
            approvalPolicy: approvalPolicy);

        return store.CreatePlanAsync(envelope, TargetNamespace, CancellationToken.None);
    }

    private sealed record class TestPlanPayload(string Name);
}
