using InfraGate.Approvals;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class PlanEnvelopeFactoryTests
{
    [Fact]
    public void Create_WhenReviewSurfaceContextChanges_ChangesReviewDigest()
    {
        var createdAtUtc = DateTimeOffset.Parse("2026-05-15T00:00:00Z");
        var requester = new PlanRequester("test-subject", "test");
        var intentDigest = ApprovalDigest.ComputeSha256(
            "dummy.intent.v1",
            new { operation = "scale", name = "demo" });
        var payload = new Dictionary<string, string>
        {
            ["name"] = "demo"
        };

        var browserEnvelope = PlanEnvelopeFactory.Create(
            "p-11111111111111111111111111111111",
            "dummy",
            "scale",
            createdAtUtc,
            requester,
            intentDigest,
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            payload);
        var otherEnvelope = PlanEnvelopeFactory.Create(
            "p-11111111111111111111111111111111",
            "dummy",
            "scale",
            createdAtUtc,
            requester,
            intentDigest,
            new ReviewSurfaceContext("other-review-surface", "dummy-review-v1"),
            payload);

        Assert.NotEqual(browserEnvelope.ReviewDigest, otherEnvelope.ReviewDigest);
    }
}
