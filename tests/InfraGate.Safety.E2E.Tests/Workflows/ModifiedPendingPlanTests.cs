using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class ModifiedPendingPlanTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task ApprovePendingPlan_AfterPendingFileMutation_IsRefusedAndAudited()
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
        var originalHash = await ApprovalStore.ComputeSha256Async(pendingPath, CancellationToken.None);

        await File.AppendAllTextAsync(pendingPath, Environment.NewLine, CancellationToken.None);

        var result = await fixture.ApprovalStore.ApprovePendingPlanAsync(planId, originalHash, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Contains("changed during approval", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fixture.ApprovalStore.GetApprovedPath(planId)));

        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.ApprovalHashMismatch &&
            evt.GetProperty("payload").TryGetProperty("planId", out var planIdProp) &&
            planIdProp.GetString() == planId);
    }
}
