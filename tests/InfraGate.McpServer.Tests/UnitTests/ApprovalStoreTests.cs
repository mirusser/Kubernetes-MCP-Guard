using InfraGate.Approvals;
using InfraGate.McpServer;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class ApprovalStoreTests
{
    private const string TargetNamespace = K8SMcpOptions.DefaultNamespace;

    [Fact]
    public void NewPlanId_ReturnsOpaquePlanIdentifier()
    {
        string planId = ApprovalStore.NewPlanId();

        Assert.Matches("^[0-9a-f]{32}$", planId);
        Assert.DoesNotMatch("^\\d{14}-", planId);
    }

    [Fact]
    public async Task CreatePlanAsync_DoesNotCreateLegacyApprovedHashDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        var store = new ApprovalStore(new ApprovalStoreOptions(root));
        var plan = CreatePlan();

        await store.CreatePlanAsync(plan, TargetNamespace, CancellationToken.None);

        Assert.False(Directory.Exists(Path.Combine(root, ApprovalConventions.Storage.ApprovedDirectory)));
    }

    [Fact]
    public async Task GetPendingPlanAsync_DeniesAlreadyAppliedPlan()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, TargetNamespace, CancellationToken.None);
        var grant = await store.CreateGrantAsync(created.Envelope, "test-subject", "challenge-1", CancellationToken.None);
        await store.MarkAppliedAsync(created.Envelope, TargetNamespace, grant, CancellationToken.None);

        var pending = await store.GetPendingPlanAsync(created.Envelope.Id, CancellationToken.None);

        Assert.False(pending.IsPending);
        Assert.Contains("already applied", pending.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPendingPlanAsync_LegacyRawPlan_ReturnsReRequestMessage()
    {
        var store = CreateStore();
        string planId = ApprovalStore.NewPlanId();
        Directory.CreateDirectory(store.PendingDirectory);
        await File.WriteAllTextAsync(
            store.GetPendingPath(planId),
            $$"""
              {
                "id": "{{planId}}",
                "operation": "scale",
                "namespace": "{{TargetNamespace}}",
                "createdAtUtc": "2026-05-15T00:00:00Z"
              }
              """,
            CancellationToken.None);

        var pending = await store.GetPendingPlanAsync(planId, CancellationToken.None);

        Assert.False(pending.IsPending);
        Assert.Contains("old approval file format", pending.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Re-request", pending.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPendingPlanAsync_EnvelopeWithoutDigests_ReturnsReRequestMessage()
    {
        var store = CreateStore();
        string planId = ApprovalStore.NewPlanId();
        Directory.CreateDirectory(store.PendingDirectory);
        await File.WriteAllTextAsync(
            store.GetPendingPath(planId),
            $$"""
              {
                "id": "{{planId}}",
                "adapterId": "dummy",
                "operation": "scale",
                "createdAtUtc": "2026-05-15T00:00:00Z",
                "requester": {
                  "subject": "test-subject",
                  "authenticationType": "test"
                },
                "payload": {
                  "name": "mcp-api-demo",
                  "replicas": "1"
                }
              }
              """,
            CancellationToken.None);

        var pending = await store.GetPendingPlanAsync(planId, CancellationToken.None);

        Assert.False(pending.IsPending);
        Assert.Contains("old approval file format", pending.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Re-request", pending.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ApprovalStore CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        return new ApprovalStore(new ApprovalStoreOptions(root));
    }

    private static PlanEnvelope<Dictionary<string, string>> CreatePlan() =>
        PlanEnvelopeFactory.Create(
            ApprovalStore.NewPlanId(),
            "dummy",
            "scale",
            DateTimeOffset.UtcNow,
            new PlanRequester("test-subject", "test"),
            ApprovalDigest.ComputeSha256(
                "dummy.intent.v1",
                new
                {
                    operation = "scale",
                    name = "mcp-api-demo",
                    replicas = "1"
                }),
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            new Dictionary<string, string>
            {
                ["name"] = "mcp-api-demo",
                ["replicas"] = "1"
            });
}
