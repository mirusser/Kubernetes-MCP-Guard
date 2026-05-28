using InfraGate.Approvals;
using InfraGate;

using InfraGate.Approvals.Plan;

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

    [Fact]
    public void Create_WhenEvidenceArtifactDigestChanges_ChangesReviewDigestOnly()
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

        var left = PlanEnvelopeFactory.Create(
            "p-11111111111111111111111111111111",
            "dummy",
            "scale",
            createdAtUtc,
            requester,
            intentDigest,
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            payload,
            evidenceArtifacts:
            [
                new EvidenceArtifactSummary(
                    "diff",
                    ApprovalDigest.ComputeSha256("dummy.diff.v1", new { replicas = 2 }),
                    "payload.diffs",
                    [])
            ]);
        var right = PlanEnvelopeFactory.Create(
            "p-11111111111111111111111111111111",
            "dummy",
            "scale",
            createdAtUtc,
            requester,
            intentDigest,
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            payload,
            evidenceArtifacts:
            [
                new EvidenceArtifactSummary(
                    "diff",
                    ApprovalDigest.ComputeSha256("dummy.diff.v1", new { replicas = 3 }),
                    "payload.diffs",
                    [])
            ]);

        Assert.Equal(left.IntentDigest, right.IntentDigest);
        Assert.NotEqual(left.ReviewDigest, right.ReviewDigest);
    }

    [Fact]
    public void Create_WhenPayloadEvidenceChangesWithoutArtifactChange_DoesNotChangeReviewDigest()
    {
        var createdAtUtc = DateTimeOffset.Parse("2026-05-15T00:00:00Z");
        var requester = new PlanRequester("test-subject", "test");
        var intentDigest = ApprovalDigest.ComputeSha256(
            "dummy.intent.v1",
            new { operation = "scale", name = "demo" });
        var artifact = new EvidenceArtifactSummary(
            "diff",
            ApprovalDigest.ComputeSha256("dummy.diff.v1", new { replicas = 2 }),
            "payload.diffs",
            []);

        var left = PlanEnvelopeFactory.Create(
            "p-11111111111111111111111111111111",
            "dummy",
            "scale",
            createdAtUtc,
            requester,
            intentDigest,
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            new Dictionary<string, string> { ["evidence"] = "old" },
            evidenceArtifacts: [artifact]);
        var right = PlanEnvelopeFactory.Create(
            "p-11111111111111111111111111111111",
            "dummy",
            "scale",
            createdAtUtc,
            requester,
            intentDigest,
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            new Dictionary<string, string> { ["evidence"] = "new" },
            evidenceArtifacts: [artifact]);

        Assert.Equal(left.ReviewDigest, right.ReviewDigest);
    }

    [Fact]
    public void Create_WhenApprovalPolicyChanges_ChangesReviewDigest()
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

        var sameSubject = PlanEnvelopeFactory.Create(
            "p-11111111111111111111111111111111",
            "dummy",
            "scale",
            createdAtUtc,
            requester,
            intentDigest,
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            payload);
        var operatorApproval = PlanEnvelopeFactory.Create(
            "p-11111111111111111111111111111111",
            "dummy",
            "scale",
            createdAtUtc,
            requester,
            intentDigest,
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            payload,
            approvalPolicy: ApprovalPolicy.OperatorApproval("kubernetes-operators"));

        Assert.NotEqual(sameSubject.ReviewDigest, operatorApproval.ReviewDigest);
    }

    [Fact]
    public void Create_WhenSameSubjectPolicyIsUsed_DoesNotSerializeNullParameters()
    {
        var createdAtUtc = DateTimeOffset.Parse("2026-05-15T00:00:00Z");
        var requester = new PlanRequester("test-subject", "test");
        var intentDigest = ApprovalDigest.ComputeSha256(
            "dummy.intent.v1",
            new { operation = "scale", name = "demo" });

        var envelope = PlanEnvelopeFactory.Create(
            "p-11111111111111111111111111111111",
            "dummy",
            "scale",
            createdAtUtc,
            requester,
            intentDigest,
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            new Dictionary<string, string> { ["name"] = "demo" });
        string canonicalJson = CanonicalJson.Serialize(envelope.ApprovalPolicy);

        Assert.DoesNotContain("parameters", canonicalJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_WhenEvidenceArtifactRedactionMetadataChanges_ChangesReviewDigest()
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
        var artifactDigest = ApprovalDigest.ComputeSha256("dummy.diff.v1", new { replicas = 2 });

        var left = PlanEnvelopeFactory.Create(
            "p-11111111111111111111111111111111",
            "dummy",
            "scale",
            createdAtUtc,
            requester,
            intentDigest,
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            payload,
            evidenceArtifacts:
            [
                new EvidenceArtifactSummary("diff", artifactDigest, "payload.diffs", [])
            ]);
        var right = PlanEnvelopeFactory.Create(
            "p-11111111111111111111111111111111",
            "dummy",
            "scale",
            createdAtUtc,
            requester,
            intentDigest,
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            payload,
            evidenceArtifacts:
            [
                new EvidenceArtifactSummary("diff", artifactDigest, "payload.diffs", new Dictionary<string, string>
                {
                    ["redactedPaths"] = "/data/password"
                })
            ]);

        Assert.NotEqual(left.ReviewDigest, right.ReviewDigest);
    }
}
