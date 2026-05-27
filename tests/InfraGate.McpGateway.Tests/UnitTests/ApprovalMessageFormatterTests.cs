using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.Approval;
using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ApprovalMessageFormatterTests
{
    private static readonly PlanRequester TestRequester = new("test-subject", "oauth-jwt");
    private static readonly DateTimeOffset TestExpiry =
        new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RenderApprovalRequiredMessage_ContainsPlanId()
    {
        var plan = BuildPlan("plan-abc-123");
        string url = "http://gateway.test/approvals/plan-abc-123";

        string message = ApprovalMessageFormatter.RenderApprovalRequiredMessage(plan, url, TestExpiry);

        Assert.Contains("plan-abc-123", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderApprovalRequiredMessage_ContainsOperation()
    {
        var plan = BuildPlan();

        string message = ApprovalMessageFormatter.RenderApprovalRequiredMessage(
            plan, "http://gateway.test/approvals/x", TestExpiry);

        Assert.Contains(KubernetesAdapterConventions.PlanOperations.Restart, message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderApprovalRequiredMessage_ContainsDescription()
    {
        var plan = BuildPlan();

        string message = ApprovalMessageFormatter.RenderApprovalRequiredMessage(
            plan, "http://gateway.test/approvals/x", TestExpiry);

        Assert.Contains("Restart Deployment 'nginx-demo'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderApprovalRequiredMessage_ContainsApprovalUrl()
    {
        var plan = BuildPlan();
        const string url = "http://gateway.test/approvals/some-plan-id";

        string message = ApprovalMessageFormatter.RenderApprovalRequiredMessage(plan, url, TestExpiry);

        Assert.Contains(url, message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderApprovalRequiredMessage_ContainsExpiryTimestamp()
    {
        var plan = BuildPlan();

        string message = ApprovalMessageFormatter.RenderApprovalRequiredMessage(
            plan, "http://gateway.test/approvals/x", TestExpiry);

        Assert.Contains("2026-06-01", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderApprovalRequiredMessage_ContainsWaitForApprovalInstruction()
    {
        var plan = BuildPlan("plan-xyz");

        string message = ApprovalMessageFormatter.RenderApprovalRequiredMessage(
            plan, "http://gateway.test/approvals/plan-xyz", TestExpiry);

        Assert.Contains("wait_for_plan_approval", message, StringComparison.Ordinal);
        Assert.Contains("plan-xyz", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderApprovalRequiredMessage_ContainsTargetKindAndName()
    {
        var plan = BuildPlan();

        string message = ApprovalMessageFormatter.RenderApprovalRequiredMessage(
            plan, "http://gateway.test/approvals/x", TestExpiry);

        Assert.Contains("Deployment", message, StringComparison.Ordinal);
        Assert.Contains("nginx-demo", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderApprovalRequiredMessage_ContainsDigests()
    {
        var plan = BuildPlan();

        string message = ApprovalMessageFormatter.RenderApprovalRequiredMessage(
            plan, "http://gateway.test/approvals/x", TestExpiry);

        Assert.Contains("Intent Digest:", message, StringComparison.Ordinal);
        Assert.Contains("Review Digest:", message, StringComparison.Ordinal);
    }

    private static KubernetesPlan BuildPlan(string? planId = null)
    {
        var payload = new KubernetesPlanPayload(
            "default",
            "Restart Deployment 'nginx-demo' in namespace 'default'.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.PlanParameters.Name] = "nginx-demo",
            },
            [new KubernetesObjectRef("apps/v1", "Deployment", "default", "nginx-demo")]);

        var typedEnvelope = KubernetesApprovalAdapter.CreateEnvelope(
            planId ?? ApprovalIds.NewPlanId(),
            KubernetesAdapterConventions.PlanOperations.Restart,
            DateTimeOffset.UtcNow,
            TestRequester,
            payload);

        return KubernetesApprovalAdapter.Materialize(typedEnvelope);
    }
}
