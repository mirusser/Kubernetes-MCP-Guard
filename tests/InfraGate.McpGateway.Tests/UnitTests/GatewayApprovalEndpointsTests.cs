using InfraGate.Approvals;
using InfraGate.McpGateway;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class GatewayApprovalEndpointsTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

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

        var result = GatewayApprovalEndpoints.RenderApprovalPage(page, null);

        Assert.Contains("Approval Unavailable", result);
        Assert.Contains("Challenge expired.", result);
    }

    [Fact]
    public void RenderApprovalPage_CanDecide_DelegatesToForm()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();
        var page = new ApprovalPageModel(true, null, challenge, plan);

        var result = GatewayApprovalEndpoints.RenderApprovalPage(page, "test-token");

        Assert.Contains("Review Kubernetes Plan", result);
        Assert.Contains(plan.Id, result);
    }

    [Fact]
    public void RenderApprovalForm_ContainsPlanMetadata()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains(plan.Id, result);
        Assert.Contains(plan.Operation, result);
        Assert.Contains(plan.Namespace, result);
        Assert.Contains(plan.Description, result);
        Assert.Contains(challenge.PlanHash, result);
        Assert.Contains(challenge.RequesterSubject, result);
        Assert.Contains(challenge.ExpiresAtUtc.ToString("O"), result);
    }

    [Fact]
    public void RenderApprovalForm_ContainsObjectsSection()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("<h2>Objects</h2>", result);
        Assert.Contains("apps/v1 Deployment", result);
        Assert.Contains("mcp-ns/demo</li>", result);
    }

    [Fact]
    public void RenderApprovalForm_ContainsDryRunSection()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("Server-side dry-run: succeeded", result);
        Assert.Contains("<h3>Dry-run Objects</h3>", result);
        Assert.Contains("<h3>Admission Warnings</h3>", result);
    }

    [Fact]
    public void RenderApprovalForm_ContainsDiffSection()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("<h2>Diff</h2>", result);
        Assert.Contains("scaled replicas to 3", result);
        Assert.Contains("replicas: " + "3", result);
    }

    [Fact]
    public void RenderApprovalForm_ApproveButtonDisabled_WhenDenyPolicyFinding()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();
        plan = plan with
        {
            PolicyFindings = new[]
            {
                new K8sPlanPolicyFinding("Deny", "POL-001", "deployment/demo", "Not allowed in production.")
            }
        };

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("<button type=\"submit\" class=\"approve\" disabled>", result);
    }

    [Fact]
    public void RenderApprovalForm_ContainsAntiForgeryToken()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "abc123");

        Assert.Contains("abc123", result);
    }

    [Fact]
    public void RenderPolicyFindings_Empty_ReturnsNone()
    {
        var result = GatewayApprovalEndpoints.RenderPolicyFindings([]);

        Assert.Contains("None", result);
        Assert.DoesNotContain("<ul>", result);
    }

    [Fact]
    public void RenderPolicyFindings_WithFindings_RendersList()
    {
        var findings = new[]
        {
            new K8sPlanPolicyFinding("Warn", "W001", "deployment/demo", "Memory limit not set."),
            new K8sPlanPolicyFinding("Info", "I001", "service/demo", "Consider adding labels.")
        };

        var result = GatewayApprovalEndpoints.RenderPolicyFindings(findings);

        Assert.Contains("<span class=\"badge badge-warn\">Warn</span>", result);
        Assert.Contains("[W001]", result);
        Assert.Contains("Memory limit not set.", result);
        Assert.Contains("deployment/demo", result);
        Assert.Contains("<span class=\"badge badge-info\">Info</span>", result);
        Assert.Contains("[I001]", result);
    }

    [Fact]
    public void RenderDiffs_Empty_ReturnsErrorMessage()
    {
        var result = GatewayApprovalEndpoints.RenderDiffs([]);

        Assert.Contains("No diff was recorded", result);
        Assert.Contains("class=\"error\"", result);
    }

    [Fact]
    public void RenderDiffs_WithDiffs_RendersDiffSections()
    {
        var diffs = new[]
        {
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
        };

        var result = GatewayApprovalEndpoints.RenderDiffs(diffs);

        Assert.Contains("scaled replicas to 3", result);
        Assert.Contains("Update", result);
        Assert.Contains("apps/v1 Deployment mcp-ns/demo", result);
        Assert.Contains("+  replicas: 3", result);
        Assert.Contains("-  replicas: 1", result);
    }

    [Fact]
    public void RenderPathList_Empty_ReturnsNone()
    {
        var result = GatewayApprovalEndpoints.RenderPathList([]);

        Assert.Equal("None", result);
    }

    [Fact]
    public void RenderPathList_WithPaths_ReturnsCommaSeparated()
    {
        var result = GatewayApprovalEndpoints.RenderPathList(["/spec/replicas", "/metadata/labels"]);

        Assert.Contains("<code>/spec/replicas</code>", result);
        Assert.Contains("<code>/metadata/labels</code>", result);
        Assert.Contains(", ", result);
    }

    [Fact]
    public void RenderDiffPaths_HasChangedPathsContent()
    {
        var diff = new K8sPlanDiff(
            new K8sObjectRef("v1", "ConfigMap", "mcp-ns", "cfg"),
            "Create",
            "created ConfigMap",
            "+  key: value",
            null,
            "{\"data\":{\"key\":\"value\"}}",
            ["/data/key"],
            [],
            []);

        var result = GatewayApprovalEndpoints.RenderDiffPaths(diff);

        Assert.Contains("<summary>Changed paths</summary>", result);
        Assert.Contains("Added</span>", result);
        Assert.Contains("Removed</span>", result);
        Assert.Contains("Changed</span>", result);
        Assert.Contains("<code>/data/key</code>", result);
    }

    [Fact]
    public void RenderDiff_EncodesHtmlInSummary()
    {
        var diff = new K8sPlanDiff(
            new K8sObjectRef("v1", "Service", "mcp-ns", "<script>"),
            "Create",
            "created <script>alert()</script>",
            "",
            null,
            "{}",
            [],
            [],
            []);

        var result = GatewayApprovalEndpoints.RenderDiff(diff);

        Assert.DoesNotContain("<script>alert", result);
        Assert.Contains("&lt;script&gt;alert", result);
    }

    [Fact]
    public void RenderDocument_DarkModeDefault()
    {
        var result = GatewayApprovalEndpoints.RenderDocument("Title", "<p>body</p>");

        Assert.Contains("color-scheme: dark", result);
        Assert.DoesNotContain("color-scheme: light dark", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderApprovalForm_DisplaysPlanCreatedAtUtc()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("Plan Created", result);
        Assert.Contains(plan.CreatedAtUtc.ToString("O"), result);
    }

    [Fact]
    public void RenderApprovalForm_DisplaysChallengeCreatedAtUtc()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("Challenge Created", result);
        Assert.Contains(challenge.CreatedAtUtc.ToString("O"), result);
    }

    [Fact]
    public void RenderApprovalForm_DisplaysParameters()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("Parameters (1)", result);
        Assert.Contains("<dt>scale</dt>", result);
        Assert.Contains("<code>3</code>", result);
    }

    [Fact]
    public void RenderApprovalForm_OmitsParameters_WhenEmpty()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan() with { Parameters = new Dictionary<string, string>() };

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("<p>None</p>", result);
        Assert.DoesNotContain("Parameters (", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderApprovalForm_DisplaysManifest_WhenPresent()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("Submitted Manifest", result);
        Assert.Contains("View manifest", result);
        Assert.Contains("apiVersion: apps/v1", result);
    }

    [Fact]
    public void RenderApprovalForm_OmitsManifest_WhenNull()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan() with { Manifest = null };

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.DoesNotContain("Submitted Manifest", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderApprovalForm_DisplaysRequesterAuthType()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("Requester Auth", result);
        Assert.Contains("OAuth", result);
    }

    [Fact]
    public void RenderApprovalForm_DisplaysChallengeStatus()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("Challenge Status", result);
        Assert.Contains("<span class=\"badge badge-pending\">pending</span>", result);
    }

    [Fact]
    public void RenderApprovalForm_HasCardLayout()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("<section class=\"card\">", result);
        Assert.Contains("<h2>Plan Summary</h2>", result);
        Assert.Contains("<h2>Dry-run Results</h2>", result);
    }

    [Fact]
    public void RenderApprovalForm_UsesKvGridLayout()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();

        var result = GatewayApprovalEndpoints.RenderApprovalForm(challenge, plan, "token");

        Assert.Contains("class=\"kv-grid\"", result);
        Assert.Contains("class=\"kv-label\"", result);
        Assert.Contains("class=\"kv-value\"", result);
    }

    private static ApprovalChallenge CreateChallenge()
    {
        return new ApprovalChallenge(
            Id: "chall-abc123",
            PlanId: "plan-xyz789",
            PlanHash: "abcdef0123456789",
            RequesterSubject: "user@example.com",
            RequesterAuthenticationType: "OAuth",
            CreatedAtUtc: FixedTime,
            ExpiresAtUtc: FixedTime.AddHours(1),
            Status: "pending",
            ApproverSubject: null,
            DecidedAtUtc: null);
    }

    private static K8sPlan CreatePlan()
    {
        return new K8sPlan(
            id: "plan-xyz789",
            operation: "apply_manifest",
            namespaceName: "mcp-ns",
            createdAtUtc: FixedTime,
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
            PolicyFindings = [],
        };
    }
}
