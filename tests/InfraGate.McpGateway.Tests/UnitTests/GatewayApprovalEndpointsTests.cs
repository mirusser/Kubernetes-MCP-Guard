using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class GatewayApprovalEndpointsTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly IPlanReviewRenderer Renderer = new KubernetesPlanReviewRenderer();

    [Fact]
    public void Html_EncodesSpecialCharacters()
    {
        var result = GatewayApprovalEndpoints.Html("<script>alert('x')</script> & \"quotes\"");

        Assert.Contains("&lt;script&gt;", result);
        Assert.Contains("&amp;", result);
        Assert.Contains("&quot;", result);
    }

    [Fact]
    public void Html_PreservesPlainText()
    {
        var result = GatewayApprovalEndpoints.Html("hello-world-123");

        Assert.Equal("hello-world-123", result);
    }

    [Fact]
    public void RenderDocument_ProducesValidHtmlShell()
    {
        var result = GatewayApprovalEndpoints.RenderDocument("Test Title", "<p>body content</p>");

        Assert.Contains("<!doctype html>", result);
        Assert.Contains("<html lang=\"en\">", result);
        Assert.Contains("<title>Test Title - InfraGate</title>", result);
        Assert.Contains("<p>body content</p>", result);
        Assert.Contains("</html>", result);
    }

    [Fact]
    public void RenderDocument_EncodesTitle()
    {
        var result = GatewayApprovalEndpoints.RenderDocument("<b>XSS</b>", "<p>body</p>");

        Assert.Contains("<title>&lt;b&gt;XSS&lt;/b&gt; - InfraGate</title>", result);
        Assert.DoesNotContain("<title><b>XSS</b>", result);
    }

    [Fact]
    public void RenderDecisionPage_Succeeded_RendersApprovalRecorded()
    {
        var result = GatewayApprovalEndpoints.RenderDecisionPage(new ApprovalDecisionResult(true, "Plan was approved."));

        Assert.Contains("Approval Recorded", result);
        Assert.Contains("Plan was approved.", result);
        Assert.Contains("class=\"success\"", result);
    }

    [Fact]
    public void RenderDecisionPage_Failed_RendersApprovalFailed()
    {
        var result = GatewayApprovalEndpoints.RenderDecisionPage(new ApprovalDecisionResult(false, "Hash mismatch."));

        Assert.Contains("Approval Failed", result);
        Assert.Contains("Hash mismatch.", result);
        Assert.Contains("class=\"error\"", result);
    }

    [Fact]
    public void RenderApprovalPage_CannotDecide_ShowsError()
    {
        var page = new ApprovalPageModel(false, "Challenge expired.", null, null);

        var result = GatewayApprovalEndpoints.RenderApprovalPage(page, Renderer, null);

        Assert.Contains("Approval Unavailable", result);
        Assert.Contains("Challenge expired.", result);
    }

    [Fact]
    public void RenderApprovalPage_CanDecide_DelegatesToForm()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();
        var page = new ApprovalPageModel(true, null, challenge, plan);

        var result = GatewayApprovalEndpoints.RenderApprovalPage(page, Renderer, "test-token");

        Assert.Contains("Review Plan", result);
        Assert.Contains(plan.Envelope.Id, result);
    }

    [Fact]
    public void RenderApprovalForm_ContainsPlanMetadata()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, Renderer, "token");

        Assert.Contains(plan.Envelope.Id, result);
        Assert.Contains(plan.Envelope.Operation, result);
        Assert.Contains(plan.Envelope.IntentDigest.Value, result);
        Assert.Contains(plan.Envelope.ReviewDigest.Value, result);
        Assert.Contains(challenge.RequesterSubject, result);
        Assert.Contains(challenge.ExpiresAtUtc.ToString("O"), result);
    }

    [Fact]
    public void RenderApprovalForm_ContainsAntiForgeryToken()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, Renderer, "abc123");

        Assert.Contains("abc123", result);
    }

    [Fact]
    public void RenderApprovalForm_ApproveButtonDisabled_WhenCannotBeApproved()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan(canBeApproved: false);

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, Renderer, "token");

        Assert.Contains("<button type=\"submit\" class=\"approve\" disabled>", result);
    }

    [Fact]
    public void RenderApprovalForm_DisplaysChallengeCreatedAtUtc()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, Renderer, "token");

        Assert.Contains("Challenge Created", result);
        Assert.Contains(challenge.CreatedAtUtc.ToString("O"), result);
    }

    [Fact]
    public void RenderApprovalForm_DisplaysChallengeStatus()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, Renderer, "token");

        Assert.Contains("Challenge Status", result);
        Assert.Contains("<span class=\"badge badge-pending\">pending</span>", result);
    }

    [Fact]
    public void RenderApprovalForm_HasCardLayout()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, Renderer, "token");

        Assert.Contains("<section class=\"card\">", result);
        Assert.Contains("<h2>Plan Summary</h2>", result);
    }

    [Fact]
    public void RenderApprovalForm_UsesKvGridLayout()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, Renderer, "token");

        Assert.Contains("class=\"kv-grid\"", result);
        Assert.Contains("class=\"kv-label\"", result);
        Assert.Contains("class=\"kv-value\"", result);
    }

    [Fact]
    public void RenderDocument_DarkModeDefault()
    {
        var result = GatewayApprovalEndpoints.RenderDocument("Title", "<p>body</p>");

        Assert.Contains("color-scheme: dark", result);
        Assert.DoesNotContain("color-scheme: light dark", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderApprovalForm_DisplaysRequesterAuthType()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, Renderer, "token");

        Assert.Contains("Requester Auth", result);
        Assert.Contains("OAuth", result);
    }

    private static ApprovalChallenge CreateChallenge()
    {
        return new ApprovalChallenge(
            Id: "chall-abc123",
            PlanId: "plan-xyz789",
            PendingPlanHash: "abcdef0123456789",
            RequesterSubject: "user@example.com",
            RequesterAuthenticationType: "OAuth",
            CreatedAtUtc: FixedTime,
            ExpiresAtUtc: FixedTime.AddHours(1),
            Status: "pending",
            ApproverSubject: null,
            DecidedAtUtc: null);
    }

    private static IPlanReview CreatePlan(bool canBeApproved = true)
    {
        var payload = new KubernetesPlanPayload(
            namespaceName: "mcp-ns",
            description: "Scale deployment demo to 3 replicas.",
            parameters: new Dictionary<string, string> { ["scale"] = "3" },
            objects: [new K8sObjectRef("apps/v1", "Deployment", "mcp-ns", "demo")])
        {
            Manifest = "apiVersion: apps/v1\nkind: Deployment\nmetadata:\n  name: demo\n  namespace: mcp-ns\nspec:\n  replicas: 3",
            DryRun = new K8sPlanDryRun(
                "succeeded",
                FixedTime,
                [new K8sPlanDryRunObject("apps/v1/Deployment/mcp-ns/demo", "{\"kind\":\"Deployment\"}")],
                ["299 - admission warning"],
                "dry-run completed"),
            Diffs =
            [
                new K8sPlanDiff(
                    new K8sObjectRef("apps/v1", "Deployment", "mcp-ns", "demo"),
                    "Update",
                    "scaled replicas to 3",
                    "+  replicas: 3\n-  replicas: 1",
                    "{\"spec\":{\"replicas\":1}}",
                    "{\"spec\":{\"replicas\":3}}",
                    ["/spec/replicas"],
                    [],
                    ["/spec/replicas"])
            ],
            PolicyFindings = canBeApproved
                ? []
                : [new K8sPlanPolicyFinding("Deny", "POL-001", "deployment/demo", "Not allowed.")],
        };
        var envelope = KubernetesApprovalAdapter.CreateEnvelope(
            "plan-xyz789",
            "apply_manifest",
            FixedTime,
            new PlanRequester("user@example.com", "OAuth"),
            payload);

        return KubernetesApprovalAdapter.Materialize(envelope);
    }
}
