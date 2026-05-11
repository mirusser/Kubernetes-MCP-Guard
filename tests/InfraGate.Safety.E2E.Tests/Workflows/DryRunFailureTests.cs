using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class DryRunFailureTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task RequestApplyManifest_FailingStrictDryRun_DoesNotCreatePendingPlanAndAudits()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        // Strict field validation rejects unknown fields under spec, causing dry-run to fail.
        var manifest = $$"""
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: safety-e2e-bogus-field
                         namespace: {{fixture.Namespace}}
                       spec:
                         replicas: 1
                         bogusUnknownField: "this should fail strict validation"
                         selector:
                           matchLabels:
                             app: safety-e2e-bogus-field
                         template:
                           metadata:
                             labels:
                               app: safety-e2e-bogus-field
                           spec:
                             containers:
                             - name: app
                               image: nginx:1.27-alpine
                       """;

        var pendingDirectory = Path.Combine(fixture.ApprovalRoot, ApprovalConventions.Storage.PendingDirectory);
        var pendingBefore = Directory.Exists(pendingDirectory)
            ? Directory.GetFiles(pendingDirectory).Length
            : 0;

        var response = await fixture.DownstreamClient.CallToolAsync(
            McpGatewayConventions.ToolNames.RequestApplyManifest,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.Namespace] = fixture.Namespace,
                [McpGatewayConventions.ToolArguments.Manifest] = manifest
            },
            CancellationToken.None);

        var pendingAfter = Directory.Exists(pendingDirectory)
            ? Directory.GetFiles(pendingDirectory).Length
            : 0;
        Assert.Equal(pendingBefore, pendingAfter);
        Assert.DoesNotContain("PlanId:", response, StringComparison.Ordinal);
        Assert.Contains("dry-run", response, StringComparison.OrdinalIgnoreCase);

        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.DryRunFailed);
    }

    [Fact]
    public async Task ApplyApprovedPlan_PreApplyDryRunFailsAfterApproval_IsRefusedAndAudited()
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

        // Replace the planned target name with one that no longer exists in the cluster — pre-apply dry-run will fail.
        var pendingJson = await File.ReadAllTextAsync(pendingPath, CancellationToken.None);
        var tamperedJson = pendingJson.Replace(
            "\"name\":\"nginx\"",
            "\"name\":\"deployment-that-does-not-exist\"",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(pendingPath, tamperedJson, CancellationToken.None);

        var applyText = await fixture.DownstreamClient.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            },
            CancellationToken.None);

        Assert.StartsWith("Refused:", applyText, StringComparison.Ordinal);

        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.ApplyDenied);
    }
}
