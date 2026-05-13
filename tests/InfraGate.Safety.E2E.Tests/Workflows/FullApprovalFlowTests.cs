using System.Net;
using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class FullApprovalFlowTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task RestartDeployment_ApprovedThroughBrowser_AppliesExactPlanAndAudits()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        await using var client = await fixture.CreateHttpMcpClientAsync();
        var requestText = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.RequestRestartDeployment,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.Namespace] = fixture.Namespace,
                [McpGatewayConventions.ToolArguments.Name] = "nginx-demo"
            });
        var planId = SafetyE2EFixture.ParsePlanId(requestText);

        var approvalRequired = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });

        Assert.Contains("Approval required.", approvalRequired, StringComparison.Ordinal);
        Assert.Contains("Approval URL:", approvalRequired, StringComparison.Ordinal);
        Assert.DoesNotContain("Applied plan:", approvalRequired, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.ApprovalStore.GetAppliedPath(planId)));

        var challengeId = SafetyE2EFixture.ParseChallengeId(approvalRequired);
        using var unauthenticatedBrowser = fixture.CreateApprovalBrowser();
        var unauthenticatedPage = await unauthenticatedBrowser.GetAsync($"/approvals/{challengeId}");
        Assert.Equal(HttpStatusCode.Redirect, unauthenticatedPage.StatusCode);
        Assert.Contains(
            McpGatewayConventions.Approvals.LoginPath,
            unauthenticatedPage.Headers.Location?.ToString(),
            StringComparison.Ordinal);

        using var browser = await fixture.CreateAuthenticatedApprovalBrowserAsync(challengeId, client.Subject);
        var page = await browser.GetAsync($"/approvals/{challengeId}");
        page.EnsureSuccessStatusCode();
        var pageText = await page.Content.ReadAsStringAsync();
        Assert.Contains($"<code>{planId}</code>", pageText, StringComparison.Ordinal);
        Assert.Contains("Plan Hash", pageText, StringComparison.Ordinal);
        Assert.Contains("Server-side dry-run: succeeded", pageText, StringComparison.Ordinal);
        Assert.Contains("<h2>Diff</h2>", pageText, StringComparison.Ordinal);
        Assert.Contains($"{fixture.Namespace}/nginx-demo", pageText, StringComparison.Ordinal);

        SafetyE2EFixture.AddResponseCookies(browser, page);
        var approvalResponse = await SafetyE2EFixture.PostApprovalAsync(
            browser,
            challengeId,
            SafetyE2EFixture.ParseAntiforgeryToken(pageText));
        Assert.Contains("was approved", approvalResponse, StringComparison.Ordinal);
        Assert.True(File.Exists(fixture.ApprovalStore.GetApprovedPath(planId)));

        var acceptedResult = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });

        Assert.Contains($"Applied plan: {planId}", acceptedResult, StringComparison.Ordinal);
        Assert.Contains("Restarted apps/v1 Deployment", acceptedResult, StringComparison.Ordinal);
        Assert.True(File.Exists(fixture.ApprovalStore.GetAppliedPath(planId)));

        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.ApprovalChallengeApproved &&
            evt.GetProperty("payload").TryGetProperty("id", out var approvedChallengeId) &&
            approvedChallengeId.GetString() == challengeId);
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.PlanApplied &&
            evt.GetProperty("payload").TryGetProperty("planId", out var appliedPlanId) &&
            appliedPlanId.GetString() == planId);
    }
}
