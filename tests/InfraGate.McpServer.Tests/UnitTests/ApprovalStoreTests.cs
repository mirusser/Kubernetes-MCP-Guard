using InfraGate.Approvals;
using InfraGate.McpServer;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class ApprovalStoreTests
{
    private const string TargetNamespace = K8SMcpOptions.DefaultNamespace;

    [Fact]
    public async Task GetApprovedPlanAsync_DeniesUnapprovedPlan()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, TargetNamespace, CancellationToken.None);

        var approved = await store.GetApprovedPlanAsync(created.Envelope.Id, CancellationToken.None);

        Assert.False(approved.IsApproved);
        Assert.Contains("not approved", approved.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetApprovedPlanAsync_ApprovesMatchingHash()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, TargetNamespace, CancellationToken.None);
        await File.WriteAllTextAsync(created.ApprovedPath, created.Hash, CancellationToken.None);

        var approved = await store.GetApprovedPlanAsync(created.Envelope.Id, CancellationToken.None);

        Assert.True(approved.IsApproved);
        Assert.Equal(plan.Id, approved.Envelope?.Id);
        Assert.Equal(created.Hash, approved.Hash);
    }

    [Fact]
    public async Task ApprovePendingPlanAsync_WritesServerApprovalForMatchingHash()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, TargetNamespace, CancellationToken.None);

        var approved = await store.ApprovePendingPlanAsync(created.Envelope.Id, created.Hash, CancellationToken.None);

        Assert.True(approved.IsApproved);
        Assert.Equal(plan.Id, approved.Envelope?.Id);
        Assert.Equal(created.Hash, approved.Hash);
        Assert.True(File.Exists(created.ApprovedPath));
        Assert.Equal(created.Hash, (await File.ReadAllTextAsync(created.ApprovedPath)).Trim());
    }

    [Fact]
    public async Task GetApprovedPlanAsync_DeniesPlanChangedAfterApproval()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, TargetNamespace, CancellationToken.None);
        await File.WriteAllTextAsync(created.ApprovedPath, created.Hash, CancellationToken.None);
        await File.AppendAllTextAsync(created.PendingPath, Environment.NewLine, CancellationToken.None);

        var approved = await store.GetApprovedPlanAsync(created.Envelope.Id, CancellationToken.None);

        Assert.False(approved.IsApproved);
        Assert.Contains("changed after approval", approved.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApprovePendingPlanAsync_DeniesPlanChangedDuringApproval()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, TargetNamespace, CancellationToken.None);
        await File.AppendAllTextAsync(created.PendingPath, Environment.NewLine, CancellationToken.None);

        var approved = await store.ApprovePendingPlanAsync(created.Envelope.Id, created.Hash, CancellationToken.None);

        Assert.False(approved.IsApproved);
        Assert.Contains("changed during approval", approved.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(created.ApprovedPath));
    }

    [Fact]
    public async Task ApprovePendingPlanAsync_DeniesWrongHash()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, TargetNamespace, CancellationToken.None);

        var approved = await store.ApprovePendingPlanAsync(created.Envelope.Id, "0000000000000000", CancellationToken.None);

        Assert.False(approved.IsApproved);
        Assert.Contains("changed during approval", approved.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(created.ApprovedPath));
    }

    [Fact]
    public async Task GetApprovedPlanAsync_DeniesAlreadyAppliedPlan()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, TargetNamespace, CancellationToken.None);
        await File.WriteAllTextAsync(created.ApprovedPath, created.Hash, CancellationToken.None);
        await store.MarkAppliedAsync(created.Envelope, TargetNamespace, created.Hash, CancellationToken.None);

        var approved = await store.GetApprovedPlanAsync(created.Envelope.Id, CancellationToken.None);

        Assert.False(approved.IsApproved);
        Assert.Contains("already applied", approved.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(store.GetAppliedPath(created.Envelope.Id)));
    }

    [Fact]
    public async Task GetPendingPlanAsync_DeniesAlreadyAppliedPlan()
    {
        var store = CreateStore();
        var plan = CreatePlan();
        var created = await store.CreatePlanAsync(plan, TargetNamespace, CancellationToken.None);
        await File.WriteAllTextAsync(created.ApprovedPath, created.Hash, CancellationToken.None);
        await store.MarkAppliedAsync(created.Envelope, TargetNamespace, created.Hash, CancellationToken.None);

        var pending = await store.GetPendingPlanAsync(created.Envelope.Id, CancellationToken.None);

        Assert.False(pending.IsPending);
        Assert.Contains("already applied", pending.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPendingPlanAsync_LegacyRawPlan_ReturnsReRequestMessage()
    {
        var store = CreateStore();
        string planId = ApprovalStore.NewPlanId();
        Directory.CreateDirectory(store.PendingDirectory);
        await File.WriteAllTextAsync(
            store.GetPendingPath(planId),
            $$"""
              {
                "id": "{{planId}}",
                "operation": "scale",
                "namespace": "{{TargetNamespace}}",
                "createdAtUtc": "2026-05-15T00:00:00Z"
              }
              """,
            CancellationToken.None);

        var pending = await store.GetPendingPlanAsync(planId, CancellationToken.None);

        Assert.False(pending.IsPending);
        Assert.Contains("old approval file format", pending.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Re-request", pending.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ApprovalStore CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        return new ApprovalStore(new ApprovalStoreOptions(root));
    }

    private static PlanEnvelope<Dictionary<string, string>> CreatePlan() =>
        new(
            ApprovalStore.NewPlanId(),
            "dummy",
            "scale",
            DateTimeOffset.UtcNow,
            new PlanRequester("test-subject", "test"),
            new Dictionary<string, string>
            {
                ["name"] = "mcp-api-demo",
                ["replicas"] = "1"
            });
}
