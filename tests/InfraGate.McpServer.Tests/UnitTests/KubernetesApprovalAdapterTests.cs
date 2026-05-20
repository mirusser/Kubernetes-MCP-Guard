using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesApprovalAdapterTests
{
    [Fact]
    public void WithPayload_ReviewEvidenceChanges_ChangesReviewDigestOnly()
    {
        var plan = CreatePlan(new KubernetesPlanPayload(
            "demo",
            "Scale deployment.",
            new Dictionary<string, string>
            {
                ["replicas"] = "2",
                ["name"] = "demo"
            },
            [new KubernetesObjectRef("apps/v1", "Deployment", "demo", "demo")]));

        var withEvidence = KubernetesApprovalAdapter.WithPayload(
            plan,
            plan.Payload with
            {
                DryRun = new KubernetesPlanDryRun(
                    "succeeded",
                    DateTimeOffset.UtcNow,
                    [new KubernetesPlanDryRunObject("apps/v1 Deployment demo/demo", "{}")],
                    [],
                    "Server-side dry-run succeeded.")
            });

        Assert.Equal(plan.IntentDigest, withEvidence.IntentDigest);
        Assert.NotEqual(plan.ReviewDigest, withEvidence.ReviewDigest);
    }

    [Fact]
    public void CreateEnvelope_ParameterInsertionOrderChanges_ProducesSameIntentDigest()
    {
        var left = CreatePlan(new KubernetesPlanPayload(
            "demo",
            "Scale deployment.",
            new Dictionary<string, string>
            {
                ["replicas"] = "2",
                ["name"] = "demo"
            },
            [new KubernetesObjectRef("apps/v1", "Deployment", "demo", "demo")]));
        var right = CreatePlan(new KubernetesPlanPayload(
            "demo",
            "Scale deployment.",
            new Dictionary<string, string>
            {
                ["name"] = "demo",
                ["replicas"] = "2"
            },
            [new KubernetesObjectRef("apps/v1", "Deployment", "demo", "demo")]));

        Assert.Equal(left.IntentDigest, right.IntentDigest);
    }

    [Fact]
    public void CreateEnvelope_WithEvidence_DerivesEvidenceArtifactSummaries()
    {
        var plan = CreatePlan(new KubernetesPlanPayload(
            "demo",
            "Scale deployment.",
            new Dictionary<string, string>
            {
                ["replicas"] = "2",
                ["name"] = "demo"
            },
            [new KubernetesObjectRef("apps/v1", "Deployment", "demo", "demo")])
        {
            DryRun = new KubernetesPlanDryRun(
                "succeeded",
                DateTimeOffset.UnixEpoch,
                [new KubernetesPlanDryRunObject("apps/v1 Deployment demo/demo", "{}")],
                [],
                "Server-side dry-run succeeded."),
            Diffs =
            [
                new KubernetesPlanDiff(
                    new KubernetesObjectRef("apps/v1", "Deployment", "demo", "demo"),
                    ApprovalConventions.DiffChangeTypes.Update,
                    "Deployment will be updated.",
                    "@@ -1 +1 @@",
                    "{}",
                    "{}",
                    [],
                    [],
                    [])
            ],
            PolicyFindings =
            [
                new KubernetesPlanPolicyFinding("Warning", "IMAGE_LATEST_TAG", "apps/v1 Deployment demo/demo", "Avoid latest.")
            ]
        });

        Assert.Contains(plan.EvidenceArtifacts, artifact => artifact.Type == "kubernetes.dry-run");
        Assert.Contains(plan.EvidenceArtifacts, artifact => artifact.Type == "kubernetes.diff");
        Assert.Contains(plan.EvidenceArtifacts, artifact => artifact.Type == "kubernetes.policy-findings");
        Assert.All(plan.EvidenceArtifacts, artifact => Assert.Equal(ApprovalConventions.Digests.Sha256, artifact.Digest.Algorithm));
    }

    [Fact]
    public void KubernetesPlanPolicyFinding_ImplementsDomainPolicyCheckContract()
    {
        var finding = new KubernetesPlanPolicyFinding("Warning", "IMAGE_LATEST_TAG", "apps/v1 Deployment demo/demo", "Avoid latest.");

        var policyCheck = Assert.IsType<IDomainPolicyCheck>(finding, exactMatch: false);

        Assert.Equal("IMAGE_LATEST_TAG", policyCheck.Code);
        Assert.Equal("Avoid latest.", policyCheck.Message);
        Assert.Equal("Warning", policyCheck.Severity);
        Assert.Equal("apps/v1 Deployment demo/demo", policyCheck.ObjectRef);
    }

    [Fact]
    public void Decode_WhenPayloadIntentChangesWithoutDigestChange_Fails()
    {
        var plan = CreatePlan(new KubernetesPlanPayload(
            "demo",
            "Scale deployment.",
            new Dictionary<string, string>
            {
                ["replicas"] = "2",
                ["name"] = "demo"
            },
            [new KubernetesObjectRef("apps/v1", "Deployment", "demo", "demo")]));
        var tamperedPayload = plan.Payload with
        {
            Parameters = new Dictionary<string, string>
            {
                ["replicas"] = "3",
                ["name"] = "demo"
            }
        };
        var tamperedEnvelope = KubernetesApprovalAdapter.ToEnvelope(plan with { Payload = tamperedPayload });

        var result = KubernetesApprovalAdapter.Decode(tamperedEnvelope);

        Assert.False(result.Succeeded);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.IntentDigestChanged, result.ReasonCode);
    }

    private static PlanEnvelope<KubernetesPlanPayload> CreatePlan(KubernetesPlanPayload payload) =>
        KubernetesApprovalAdapter.CreateEnvelope(
            "plan-1",
            "scale",
            DateTimeOffset.UnixEpoch,
            new PlanRequester("requester", "test"),
            payload);
}
