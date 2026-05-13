using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class PlanHashMismatchTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task ApplyApprovedPlan_PendingFileChangedAfterBrowserApproval_RequiresNewApprovalAndAudits()
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
        var challengeId = SafetyE2EFixture.ParseChallengeId(approvalRequired);
        var approvalResponse = await fixture.ApproveChallengeInBrowserAsync(challengeId, client.Subject);
        Assert.Contains("was approved", approvalResponse, StringComparison.Ordinal);

        var pendingPath = fixture.ApprovalStore.GetPendingPath(planId);
        await File.AppendAllTextAsync(pendingPath, Environment.NewLine, CancellationToken.None);

        var applyText = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });

        Assert.Contains("Approval required.", applyText, StringComparison.Ordinal);
        Assert.Contains("Approval URL:", applyText, StringComparison.Ordinal);
        Assert.DoesNotContain("Applied plan:", applyText, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.ApprovalStore.GetAppliedPath(planId)));
        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.ApprovalHashMismatch &&
            evt.GetProperty("payload").TryGetProperty("planId", out var planIdProp) &&
            planIdProp.GetString() == planId);
    }
}
