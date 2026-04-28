using InfraGate.McpServer;

namespace InfraGate.McpServer.Tests;

public sealed class ApprovalStoreTests
{
    [Fact]
    public async Task GetApprovedPlanAsync_DeniesUnapprovedPlan()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, CancellationToken.None);

        var approved = await store.GetApprovedPlanAsync(created.Plan.Id, CancellationToken.None);

        Assert.False(approved.IsApproved);
        Assert.Contains("not approved", approved.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetApprovedPlanAsync_ApprovesMatchingHash()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, CancellationToken.None);
        await File.WriteAllTextAsync(created.ApprovedPath, created.Hash, CancellationToken.None);

        var approved = await store.GetApprovedPlanAsync(created.Plan.Id, CancellationToken.None);

        Assert.True(approved.IsApproved);
        Assert.Equal(plan.Id, approved.Plan?.Id);
        Assert.Equal(created.Hash, approved.Hash);
    }

    [Fact]
    public async Task ApprovePendingPlanAsync_WritesServerApprovalForMatchingHash()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, CancellationToken.None);

        var approved = await store.ApprovePendingPlanAsync(created.Plan.Id, created.Hash, CancellationToken.None);

        Assert.True(approved.IsApproved);
        Assert.Equal(plan.Id, approved.Plan?.Id);
        Assert.Equal(created.Hash, approved.Hash);
        Assert.True(File.Exists(created.ApprovedPath));
        Assert.Equal(created.Hash, (await File.ReadAllTextAsync(created.ApprovedPath)).Trim());
    }

    [Fact]
    public async Task GetApprovedPlanAsync_DeniesPlanChangedAfterApproval()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, CancellationToken.None);
        await File.WriteAllTextAsync(created.ApprovedPath, created.Hash, CancellationToken.None);
        await File.AppendAllTextAsync(created.PendingPath, Environment.NewLine, CancellationToken.None);

        var approved = await store.GetApprovedPlanAsync(created.Plan.Id, CancellationToken.None);

        Assert.False(approved.IsApproved);
        Assert.Contains("changed after approval", approved.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApprovePendingPlanAsync_DeniesPlanChangedDuringApproval()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, CancellationToken.None);
        await File.AppendAllTextAsync(created.PendingPath, Environment.NewLine, CancellationToken.None);

        var approved = await store.ApprovePendingPlanAsync(created.Plan.Id, created.Hash, CancellationToken.None);

        Assert.False(approved.IsApproved);
        Assert.Contains("changed during approval", approved.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(created.ApprovedPath));
    }

    private static ApprovalStore CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        return new ApprovalStore(new K8sMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { K8sMcpOptions.DefaultNamespace },
            root));
    }

    private static K8sPlan CreatePlan() =>
        new(
            ApprovalStore.NewPlanId(),
            "scale",
            K8sMcpOptions.DefaultNamespace,
            DateTimeOffset.UtcNow,
            "Scale deployment.",
            new Dictionary<string, string>
            {
                ["name"] = "mcp-api-demo",
                ["replicas"] = "1"
            },
            [new K8sObjectRef("apps/v1", "Deployment", K8sMcpOptions.DefaultNamespace, "mcp-api-demo")],
            Manifest: null);
}
