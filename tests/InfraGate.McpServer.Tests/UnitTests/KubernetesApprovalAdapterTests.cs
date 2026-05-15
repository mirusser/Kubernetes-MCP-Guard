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
            [new K8sObjectRef("apps/v1", "Deployment", "demo", "demo")]));

        var withEvidence = KubernetesApprovalAdapter.WithPayload(
            plan,
            plan.Payload with
            {
                DryRun = new K8sPlanDryRun(
                    "succeeded",
                    DateTimeOffset.UtcNow,
                    [new K8sPlanDryRunObject("apps/v1 Deployment demo/demo", "{}")],
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
            [new K8sObjectRef("apps/v1", "Deployment", "demo", "demo")]));
        var right = CreatePlan(new KubernetesPlanPayload(
            "demo",
            "Scale deployment.",
            new Dictionary<string, string>
            {
                ["name"] = "demo",
                ["replicas"] = "2"
            },
            [new K8sObjectRef("apps/v1", "Deployment", "demo", "demo")]));

        Assert.Equal(left.IntentDigest, right.IntentDigest);
    }

    private static PlanEnvelope<KubernetesPlanPayload> CreatePlan(KubernetesPlanPayload payload) =>
        KubernetesApprovalAdapter.CreateEnvelope(
            "plan-1",
            "scale",
            DateTimeOffset.UnixEpoch,
            new PlanRequester("requester", "test"),
            payload);
}
