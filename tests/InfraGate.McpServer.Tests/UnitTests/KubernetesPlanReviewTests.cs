using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.Approval;
using InfraGate.KubernetesAdapter.Evidence;
using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesPlanReviewTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HasReviewEvidence_WithDryRunAndDiffs_ReturnsTrue()
    {
        var plan = Materialize(WithPlan(payload => payload with
        {
            DryRun = CreateDryRun(),
            Diffs = [CreateDiff()]
        }));

        var review = (IPlanReview)plan;

        Assert.True(review.HasReviewEvidence);
    }

    [Fact]
    public void HasReviewEvidence_DeploymentOperationWithoutDiffs_ReturnsTrue()
    {
        var plan = Materialize(WithPlan(payload => payload with
        {
            DryRun = CreateDryRun(),
            Diffs = []
        }));

        var review = (IPlanReview)plan;

        Assert.True(review.HasReviewEvidence);
    }

    [Fact]
    public void HasReviewEvidence_ManifestOperationWithoutDiffs_ReturnsFalse()
    {
        var plan = Materialize(WithPlan(
            payload => payload with
            {
                DryRun = CreateDryRun(),
                Diffs = []
            },
            KubernetesAdapterConventions.PlanOperations.Apply));

        var review = (IPlanReview)plan;

        Assert.False(review.HasReviewEvidence);
    }

    [Fact]
    public void HasReviewEvidence_WithoutDryRun_ReturnsFalse()
    {
        var plan = Materialize(WithPlan(payload => payload with
        {
            DryRun = null,
            Diffs = [CreateDiff()]
        }));

        var review = (IPlanReview)plan;

        Assert.False(review.HasReviewEvidence);
    }

    [Fact]
    public void CanBeApproved_WithDenyPolicyFinding_ReturnsFalse()
    {
        var plan = Materialize(WithPlan(payload => payload with
        {
            DryRun = CreateDryRun(),
            Diffs = [CreateDiff()],
            PolicyFindings = [new KubernetesPlanPolicyFinding("Deny", "POL-001", "deployment/demo", "Not allowed.")]
        }));

        var review = (IPlanReview)plan;

        Assert.False(review.CanBeApproved);
    }

    [Fact]
    public void CanBeApproved_WithoutDenyFindings_ReturnsTrue()
    {
        var plan = Materialize(WithPlan(payload => payload with
        {
            DryRun = CreateDryRun(),
            Diffs = [CreateDiff()],
            PolicyFindings = [new KubernetesPlanPolicyFinding("Warn", "W001", "deployment/demo", "Consider labels.")]
        }));

        var review = (IPlanReview)plan;

        Assert.True(review.CanBeApproved);
    }

    [Fact]
    public void Description_ReturnsPayloadDescription()
    {
        const string expected = "Scale deployment.";
        var plan = Materialize(WithPlan(payload => payload));

        var description = ((IPlanReview)plan).Description;

        Assert.Equal(expected, description);
    }

    [Fact]
    public void Targets_MapsKubernetesObjects()
    {
        var plan = Materialize(WithPlan(payload => payload with
        {
            Objects =
            [
                new KubernetesObjectRef("apps/v1", "Deployment", "mcp-ns", "demo"),
                new KubernetesObjectRef("v1", "Service", "mcp-ns", "svc")
            ]
        }));

        var targets = ((IPlanReview)plan).Targets;

        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, t => t is { Type: "Deployment", Name: "demo", Scope: "mcp-ns" });
        Assert.Contains(targets, t => t is { Type: "Service", Name: "svc", Scope: "mcp-ns" });
    }

    [Fact]
    public void Targets_IncludesApiVersionInAttributes()
    {
        var plan = Materialize(WithPlan(payload => payload));

        var target = ((IPlanReview)plan).Targets.Single();

        Assert.Equal("apps/v1", target.Attributes["apiVersion"]);
    }

    private static KubernetesPlan Materialize(PlanEnvelope<KubernetesPlanPayload> envelope) =>
        KubernetesApprovalAdapter.Materialize(envelope);

    private static PlanEnvelope<KubernetesPlanPayload> WithPlan(
        Func<KubernetesPlanPayload, KubernetesPlanPayload> configure,
        string operation = KubernetesAdapterConventions.PlanOperations.Scale)
    {
        var payload = configure(new KubernetesPlanPayload(
            "mcp-ns",
            "Scale deployment.",
            new Dictionary<string, string> { ["replicas"] = "3" },
            [new KubernetesObjectRef("apps/v1", "Deployment", "mcp-ns", "demo")]));

        return KubernetesApprovalAdapter.CreateEnvelope(
            "plan-xyz",
            operation,
            FixedTime,
            new PlanRequester("requester", "test"),
            payload);
    }

    private static KubernetesPlanDryRun CreateDryRun() =>
        new(
            "succeeded",
            FixedTime,
            [new KubernetesPlanDryRunObject("apps/v1/Deployment/mcp-ns/demo", "{}")],
            [],
            "dry-run ok");

    private static KubernetesPlanDiff CreateDiff() =>
        new(
            new KubernetesObjectRef("apps/v1", "Deployment", "mcp-ns", "demo"),
            "Update",
            "scaled to 3",
            "+  replicas: 3\n-  replicas: 1",
            "{}",
            "{}",
            ["/spec/replicas"],
            [],
            ["/spec/replicas"]);
}
