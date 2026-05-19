using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class KubernetesPlanReviewRendererTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly IPlanReviewRenderer Renderer = new KubernetesPlanReviewRenderer();

    [Fact]
    public void RenderReviewContent_ContainsObjectsAndDryRunAndDiffs()
    {
        var plan = CreatePlan(withManifest: false);

        var html = Renderer.RenderReviewContent(plan);

        Assert.Contains("<h2>Objects</h2>", html);
        Assert.Contains("apps/v1 Deployment mcp-ns/demo", html);
        Assert.Contains("<h2>Dry-run Results</h2>", html);
        Assert.Contains("Server-side dry-run: succeeded", html);
        Assert.Contains("<h2>Diff</h2>", html);
        Assert.Contains("scaled to 3", html);
    }

    [Fact]
    public void RenderReviewContent_WithPolicyFindings_RendersThem()
    {
        var plan = CreatePlan(withFindings: true);

        var html = Renderer.RenderReviewContent(plan);

        Assert.Contains("<h2>Policy Findings</h2>", html);
        Assert.Contains("Warn", html);
        Assert.Contains("W001", html);
        Assert.Contains("Memory limit not set.", html);
    }

    [Fact]
    public void RenderReviewContent_WithManifest_RendersManifestCard()
    {
        var plan = CreatePlan(withManifest: true);

        var html = Renderer.RenderReviewContent(plan);

        Assert.Contains("Submitted Manifest", html);
        Assert.Contains("apiVersion: apps/v1", html);
    }

    [Fact]
    public void RenderReviewContent_WithoutManifest_OmitsManifestCard()
    {
        var plan = CreatePlan(withManifest: false);

        var html = Renderer.RenderReviewContent(plan);

        Assert.DoesNotContain("Submitted Manifest", html);
    }

    [Fact]
    public void RenderApprovalRequiredMessage_ContainsPlanDetails()
    {
        var plan = CreatePlan(withManifest: false);

        var message = Renderer.RenderApprovalRequiredMessage(plan, "http://gateway.test/approvals/ch-1", DateTimeOffset.MaxValue);

        Assert.Contains("PlanId:", message);
        Assert.Contains("plan-xyz", message);
        Assert.Contains("Operation:", message);
        Assert.Contains("scale", message);
        Assert.Contains("Namespace:", message);
        Assert.Contains("mcp-ns", message);
        Assert.Contains("apps/v1 Deployment mcp-ns/demo", message);
        Assert.Contains("Intent Digest:", message);
        Assert.Contains("Review Digest:", message);
    }

    private static IPlanReview CreatePlan(bool withManifest = false, bool withFindings = false)
    {
        var payload = CreatePayload(withManifest, withFindings);
        var envelope = KubernetesApprovalAdapter.CreateEnvelope(
            "plan-xyz",
            "scale",
            FixedTime,
            new PlanRequester("requester", "test"),
            payload);

        return KubernetesApprovalAdapter.Materialize(envelope);
    }

    private static KubernetesPlanPayload CreatePayload(bool includeManifest, bool includeFindings)
    {
        return new KubernetesPlanPayload(
            "mcp-ns",
            "Scale deployment.",
            new Dictionary<string, string> { ["replicas"] = "3" },
            [new KubernetesObjectRef("apps/v1", "Deployment", "mcp-ns", "demo")])
        {
            DryRun = new KubernetesPlanDryRun(
                "succeeded",
                FixedTime,
                [new KubernetesPlanDryRunObject("apps/v1/Deployment/mcp-ns/demo", "{}")],
                [],
                "dry-run ok"),
            Diffs =
            [
                new KubernetesPlanDiff(
                    new KubernetesObjectRef("apps/v1", "Deployment", "mcp-ns", "demo"),
                    "Update",
                    "scaled to 3",
                    "+  replicas: 3\n-  replicas: 1",
                    "{}",
                    "{}",
                    ["/spec/replicas"],
                    [],
                    ["/spec/replicas"])
            ],
            PolicyFindings = includeFindings
                ? [new KubernetesPlanPolicyFinding("Warn", "W001", "deployment/demo", "Memory limit not set.")]
                : [],
            Manifest = includeManifest
                ? "apiVersion: apps/v1\nkind: Deployment"
                : null
        };
    }
}
