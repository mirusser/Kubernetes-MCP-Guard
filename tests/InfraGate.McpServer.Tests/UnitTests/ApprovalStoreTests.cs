using System.Text.Json;
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

        Assert.False(Directory.Exists(Path.Combine(root, "approved")));
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

    [Fact]
    public async Task GetGrantedPlanAsync_UnsafePlanId_ReturnsDenied()
    {
        var store = CreateStore();

        var result = await store.GetGrantedPlanAsync("../etc/passwd", CancellationToken.None);

        Assert.False(result.IsGranted);
        Assert.Contains("unsupported characters", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPendingPlanAsync_UnsafePlanId_ReturnsDenied()
    {
        var store = CreateStore();

        var result = await store.GetPendingPlanAsync("../etc/passwd", CancellationToken.None);

        Assert.False(result.IsPending);
        Assert.Contains("unsupported characters", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetGrantAsync_UnsafePlanId_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.GetGrantAsync("../etc/passwd", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetGrantAsync_EmptyString_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.GetGrantAsync("", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPendingPlanAsync_EnvelopeWithNullAdapterId_ReturnsReRequestMessage()
    {
        var store = CreateStore();
        string planId = ApprovalStore.NewPlanId();
        Directory.CreateDirectory(store.PendingDirectory);
        var envelope = new PlanEnvelope
        {
            Id = planId,
            AdapterId = null!,
            Operation = "scale",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ValidFromUtc = DateTimeOffset.UtcNow,
            ValidUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10),
            Requester = new PlanRequester("test-subject", "test"),
            ApprovalPolicy = new ApprovalPolicy { Type = ApprovalConventions.ApprovalPolicyTypes.SameSubject },
            ExecutionReusePolicy = new ExecutionReusePolicy { Type = ApprovalConventions.ExecutionReusePolicyTypes.SingleExecution },
            IntentDigest = ApprovalDigest.ComputeSha256("dummy.intent.v1", new { op = "scale" }),
            ReviewDigest = ApprovalDigest.ComputeSha256("dummy.review.v1", new { renderer = "browser" }),
            ReviewSurfaceContext = new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            Payload = JsonSerializer.SerializeToElement(new { name = "demo", replicas = "1" })
        };
        await File.WriteAllTextAsync(
            store.GetPendingPath(planId),
            JsonSerializer.Serialize(envelope),
            CancellationToken.None);

        var pending = await store.GetPendingPlanAsync(planId, CancellationToken.None);

        Assert.False(pending.IsPending);
        Assert.Contains("old approval file format", pending.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPendingPlanAsync_EnvelopeWithMismatchedDigestAlgorithm_ReturnsReRequestMessage()
    {
        var store = CreateStore();
        string planId = ApprovalStore.NewPlanId();
        Directory.CreateDirectory(store.PendingDirectory);
        var envelope = new PlanEnvelope
        {
            Id = planId,
            AdapterId = "kubernetes",
            Operation = "scale",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ValidFromUtc = DateTimeOffset.UtcNow,
            ValidUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10),
            Requester = new PlanRequester("test-subject", "test"),
            ApprovalPolicy = new ApprovalPolicy { Type = ApprovalConventions.ApprovalPolicyTypes.SameSubject },
            ExecutionReusePolicy = new ExecutionReusePolicy { Type = ApprovalConventions.ExecutionReusePolicyTypes.SingleExecution },
            IntentDigest = new ApprovalDigest("md5", "dummy", "abc123"),
            ReviewDigest = ApprovalDigest.ComputeSha256("dummy.review.v1", new { renderer = "browser" }),
            ReviewSurfaceContext = new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            Payload = JsonSerializer.SerializeToElement(new { name = "demo", replicas = "1" })
        };
        await File.WriteAllTextAsync(
            store.GetPendingPath(planId),
            JsonSerializer.Serialize(envelope),
            CancellationToken.None);

        var pending = await store.GetPendingPlanAsync(planId, CancellationToken.None);

        Assert.False(pending.IsPending);
        Assert.Contains("old approval file format", pending.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPendingPlanAsync_EnvelopeWithNullReviewSurface_ReturnsReRequestMessage()
    {
        var store = CreateStore();
        string planId = ApprovalStore.NewPlanId();
        Directory.CreateDirectory(store.PendingDirectory);
        var envelope = new PlanEnvelope
        {
            Id = planId,
            AdapterId = "kubernetes",
            Operation = "scale",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ValidFromUtc = DateTimeOffset.UtcNow,
            ValidUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10),
            Requester = new PlanRequester("test-subject", "test"),
            ApprovalPolicy = new ApprovalPolicy { Type = ApprovalConventions.ApprovalPolicyTypes.SameSubject },
            ExecutionReusePolicy = new ExecutionReusePolicy { Type = ApprovalConventions.ExecutionReusePolicyTypes.SingleExecution },
            IntentDigest = ApprovalDigest.ComputeSha256("dummy.intent.v1", new { op = "scale" }),
            ReviewDigest = ApprovalDigest.ComputeSha256("dummy.review.v1", new { renderer = "browser" }),
            ReviewSurfaceContext = new ReviewSurfaceContext(null!, "dummy-review-v1"),
            Payload = JsonSerializer.SerializeToElement(new { name = "demo", replicas = "1" })
        };
        await File.WriteAllTextAsync(
            store.GetPendingPath(planId),
            JsonSerializer.Serialize(envelope),
            CancellationToken.None);

        var pending = await store.GetPendingPlanAsync(planId, CancellationToken.None);

        Assert.False(pending.IsPending);
        Assert.Contains("old approval file format", pending.Message, StringComparison.OrdinalIgnoreCase);
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
