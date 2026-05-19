using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class DangerousManifestTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task RequestApplyManifest_PrivilegedContainer_DoesNotCreatePendingPlan()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        var manifest = $$"""
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: safety-e2e-privileged
                         namespace: {{fixture.Namespace}}
                       spec:
                         replicas: 1
                         selector:
                           matchLabels:
                             app: safety-e2e-privileged
                         template:
                           metadata:
                             labels:
                               app: safety-e2e-privileged
                           spec:
                             containers:
                             - name: app
                               image: nginx:1.27-alpine
                               securityContext:
                                 privileged: true
                       """;

        var pendingDirectory = Path.Combine(fixture.ApprovalRoot, ApprovalConventions.Storage.PendingDirectory);
        var pendingBefore = Directory.Exists(pendingDirectory)
            ? Directory.GetFiles(pendingDirectory).Length
            : 0;

        await using var client = await fixture.CreateHttpMcpClientAsync();
        var response = await client.CallToolAsync(
            "request_apply_manifest",
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.ToolArguments.Namespace] = fixture.Namespace,
                [KubernetesAdapterConventions.ToolArguments.Manifest] = manifest
            });

        var pendingAfter = Directory.Exists(pendingDirectory)
            ? Directory.GetFiles(pendingDirectory).Length
            : 0;
        Assert.Equal(pendingBefore, pendingAfter);
        Assert.DoesNotContain("PlanId:", response, StringComparison.Ordinal);
        Assert.Contains("policy", response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("privileged", response, StringComparison.OrdinalIgnoreCase);
    }
}
