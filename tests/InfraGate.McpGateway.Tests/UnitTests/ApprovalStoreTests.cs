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

        var result = await store.GetPlanStatusAsync(ApprovalStore.NewPlanId(), CancellationToken.None);

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
        string planId = ApprovalStore.NewPlanId();
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

    private static ApprovalStore CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-plan-status-tests", Guid.NewGuid().ToString("N"));

        return new ApprovalStore(new ApprovalStoreOptions(root));
    }

    private static Task<ApprovalPlanResult> CreatePlanAsync(
        ApprovalStore store,
        DateTimeOffset? createdAtUtc = null)
    {
        DateTimeOffset createdAt = createdAtUtc ?? DateTimeOffset.UtcNow;
        var envelope = PlanEnvelopeFactory.Create(
            ApprovalStore.NewPlanId(),
            AdapterId,
            Operation,
            createdAt,
            new PlanRequester(Subject, "test"),
            ApprovalDigest.ComputeSha256("test.intent.v1", new { operation = Operation }),
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, Renderer),
            new TestPlanPayload("demo"));

        return store.CreatePlanAsync(envelope, TargetNamespace, CancellationToken.None);
    }

    private sealed record class TestPlanPayload(string Name);
}
