using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class PlanHashMismatchTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task ApplyApprovedPlan_PendingFileChangedAfterApproval_IsRefusedAndAudited()
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
        await File.AppendAllTextAsync(pendingPath, Environment.NewLine, CancellationToken.None);

        var applyText = await fixture.DownstreamClient.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            },
            CancellationToken.None);

        Assert.StartsWith("Refused:", applyText, StringComparison.Ordinal);
        Assert.Contains("changed after approval", applyText, StringComparison.OrdinalIgnoreCase);
        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.ApplyDenied &&
            evt.GetProperty("payload").TryGetProperty("planId", out var planIdProp) &&
            planIdProp.GetString() == planId);
    }
}
