using InfraGate.Approvals;
using InfraGate.McpServer;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sManagerRequestTests
{
    [Fact]
    public async Task RequestApplyManifestAsync_CreatesPlan_ForSupportedManifest()
    {
        var manager = CreateManager("demo");

        var result = await manager.RequestApplyManifestAsync("demo", ValidManifest, CancellationToken.None);

        Assert.Contains("Operation: apply", result);
        Assert.Contains("apps/v1 Deployment demo/demo", result);
        Assert.Contains("v1 Service demo/demo", result);
        Assert.Contains("v1 ConfigMap demo/demo-config", result);
    }

    [Fact]
    public async Task RequestApplyManifestAsync_RejectsDisallowedNamespace()
    {
        var manager = CreateManager("demo");

        var result = await manager.RequestApplyManifestAsync("other", ValidManifest, CancellationToken.None);

        Assert.Contains("Namespace 'other' is not allowed", result);
    }

    [Fact]
    public async Task RequestScaleDeploymentAsync_RejectsReplicaCountOutsideBounds()
    {
        var manager = CreateManager("demo");

        var result = await manager.RequestScaleDeploymentAsync("demo", "demo", 6, CancellationToken.None);

        Assert.Contains("Replicas must be between 0 and 5", result);
    }

    [Fact]
    public async Task RequestScaleDeploymentAsync_DirectsApprovalThroughMcpServer()
    {
        var manager = CreateManager("demo");

        var result = await manager.RequestScaleDeploymentAsync("demo", "demo", 4, CancellationToken.None);

        Assert.Contains("Status: pending Gateway approval", result);
        Assert.Contains("The Gateway will return a browser approval URL before applying it", result);
        Assert.DoesNotContain("./scripts/approve-plan.sh", result);
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_RefusesPendingPlanWithoutApproval()
    {
        var manager = CreateManager("demo");
        var request = await manager.RequestScaleDeploymentAsync("demo", "demo", 4, CancellationToken.None);
        var planId = request
            .Split(Environment.NewLine)
            .Single(line => line.StartsWith("PlanId:", StringComparison.Ordinal))
            ["PlanId: ".Length..];

        var result = await manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("Refused:", result);
        Assert.Contains("not approved", result, StringComparison.OrdinalIgnoreCase);
    }

    private static K8sManager CreateManager(string namespaceName)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        var options = new K8sMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { namespaceName },
            root);

        return new K8sManager(options, new ApprovalStore(new ApprovalStoreOptions(root)), client: null!);
    }

    private const string ValidManifest = """
                                         apiVersion: apps/v1
                                         kind: Deployment
                                         metadata:
                                           name: demo
                                         spec:
                                           replicas: 1
                                           selector:
                                             matchLabels:
                                               app: demo
                                           template:
                                             metadata:
                                               labels:
                                                 app: demo
                                             spec:
                                               containers:
                                                 - name: nginx
                                                   image: nginx:1.27-alpine
                                         ---
                                         apiVersion: v1
                                         kind: Service
                                         metadata:
                                           name: demo
                                         spec:
                                           selector:
                                             app: demo
                                           ports:
                                             - port: 80
                                               targetPort: 80
                                         ---
                                         apiVersion: v1
                                         kind: ConfigMap
                                         metadata:
                                           name: demo-config
                                         data:
                                           hello: world
                                         """;
}
