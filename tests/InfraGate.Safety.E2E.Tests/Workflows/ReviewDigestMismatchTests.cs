using System.Text.Json;
using System.Text.Json.Nodes;
using InfraGate.Approvals;
using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class ReviewDigestMismatchTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task ApplyApprovedPlan_ReviewDigestChangedAfterBrowserApproval_RefusesAndAudits()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        await using var client = await fixture.CreateHttpMcpClientAsync();
        var requestText = await client.CallToolAsync(
            "request_restart_deployment",
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.ToolArguments.Namespace] = fixture.Namespace,
                [KubernetesAdapterConventions.ToolArguments.Name] = "nginx-demo"
            });
        var planId = SafetyE2EFixture.ParsePlanId(requestText);
        var approvalRequired = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });
        var challengeId = SafetyE2EFixture.ParseChallengeId(approvalRequired);
        await fixture.ApproveChallengeInBrowserAsync(challengeId, client.Subject);

        var pendingPath = fixture.ApprovalStore.GetPendingPath(planId);
        var pendingJson = await File.ReadAllTextAsync(pendingPath, CancellationToken.None);
        var root = JsonNode.Parse(pendingJson)
            ?? throw new InvalidOperationException("Pending plan JSON was empty.");
        root["payload"]!["parameters"]!["name"] = "deployment-that-does-not-exist";
        var rewriteOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        var rewritten = root.ToJsonString(rewriteOptions);
        await File.WriteAllTextAsync(pendingPath, rewritten, CancellationToken.None);

        var applyText = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });

        Assert.Contains("Refused", applyText, StringComparison.Ordinal);
        Assert.Contains("no longer matches", applyText, StringComparison.Ordinal);
        Assert.DoesNotContain("Applied plan:", applyText, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.ApprovalStore.GetAppliedPath(planId)));
        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.PlanRequested);
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.ApprovalChallengeApproved);
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.ApplyDenied &&
            evt.GetProperty("payload").GetProperty("planId").GetString() == planId);
    }
}
