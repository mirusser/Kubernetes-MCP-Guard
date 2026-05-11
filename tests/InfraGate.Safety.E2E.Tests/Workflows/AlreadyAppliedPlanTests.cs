using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class AlreadyAppliedPlanTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task ApplyApprovedPlan_AppliedTwice_SecondCallIsRefusedAndAudited()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        var requestText = await fixture.DownstreamClient.CallToolAsync(
            McpGatewayConventions.ToolNames.RequestRestartDeployment,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.Namespace] = fixture.Namespace,
                [McpGatewayConventions.ToolArguments.Name] = "nginx-demo"
            },
            CancellationToken.None);
        var planId = SafetyE2EFixture.ParsePlanId(requestText);
        var pendingPath = fixture.ApprovalStore.GetPendingPath(planId);
        var hash = await ApprovalStore.ComputeSha256Async(pendingPath, CancellationToken.None);
        await File.WriteAllTextAsync(fixture.ApprovalStore.GetApprovedPath(planId), hash, CancellationToken.None);

        var firstApply = await fixture.DownstreamClient.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            },
            CancellationToken.None);
        var secondApply = await fixture.DownstreamClient.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            },
            CancellationToken.None);

        Assert.Contains($"Applied plan: {planId}", firstApply, StringComparison.Ordinal);
        Assert.StartsWith("Refused:", secondApply, StringComparison.Ordinal);
        Assert.Contains("already applied", secondApply, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(fixture.ApprovalStore.GetAppliedPath(planId)));

        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.PlanApplied &&
            evt.GetProperty("payload").TryGetProperty("planId", out var first) &&
            first.GetString() == planId);
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.ApplyDenied &&
            evt.GetProperty("payload").TryGetProperty("planId", out var denied) &&
            denied.GetString() == planId);
    }
}
