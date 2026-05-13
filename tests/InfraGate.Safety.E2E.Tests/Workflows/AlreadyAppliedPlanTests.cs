using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class AlreadyAppliedPlanTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task ApplyApprovedPlan_AppliedTwice_SecondCallIsRefused()
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

        var firstApply = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });
        var secondApply = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });

        Assert.Contains($"Applied plan: {planId}", firstApply, StringComparison.Ordinal);
        Assert.StartsWith("Refused:", secondApply, StringComparison.Ordinal);
        Assert.Contains("already applied", secondApply, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(fixture.ApprovalStore.GetAppliedPath(planId)));

        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.PlanApplied &&
            evt.GetProperty("payload").TryGetProperty("planId", out var first) &&
            first.GetString() == planId);
    }
}
