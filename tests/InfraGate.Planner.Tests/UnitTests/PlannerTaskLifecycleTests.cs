using A2A;
using InfraGate.Planner.Tasks;
using TaskStatus = A2A.TaskStatus;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerTaskLifecycleTests
{
    [Fact]
    public async Task StartWorkAsync_SubmittedTask_PersistsWorkingStatus()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateSubmittedTask(), CancellationToken.None);
        var lifecycle = new PlannerTaskLifecycle(store, new ChannelEventNotifier());

        await lifecycle.StartWorkAsync("task-1", "anomaly-1", CancellationToken.None);

        var task = await store.GetTaskAsync("task-1", CancellationToken.None);
        Assert.Equal(TaskState.Working, task!.Status.State);
    }

    [Fact]
    public async Task AddPlanArtifactAsync_WorkingTask_PersistsPlanReference()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateSubmittedTask(), CancellationToken.None);
        var lifecycle = new PlannerTaskLifecycle(store, new ChannelEventNotifier());
        await lifecycle.StartWorkAsync("task-1", "anomaly-1", CancellationToken.None);

        await lifecycle.AddPlanArtifactAsync("task-1", "anomaly-1", "plan-123", CancellationToken.None);

        var task = await store.GetTaskAsync("task-1", CancellationToken.None);
        Assert.Equal(TaskState.Working, task!.Status.State);
        var artifact = Assert.Single(task.Artifacts!);
        Assert.Equal(PlannerTaskStoreConventions.Artifacts.PlanReferenceId, artifact.ArtifactId);
        Assert.Equal("plan-123", Assert.Single(artifact.Parts).Text);
    }

    [Fact]
    public async Task CompleteNoActionAsync_WorkingTask_PersistsCompletedStatus()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateSubmittedTask(), CancellationToken.None);
        var lifecycle = new PlannerTaskLifecycle(store, new ChannelEventNotifier());
        await lifecycle.StartWorkAsync("task-1", "anomaly-1", CancellationToken.None);

        await lifecycle.CompleteNoActionAsync("task-1", "anomaly-1", CancellationToken.None);

        var task = await store.GetTaskAsync("task-1", CancellationToken.None);
        Assert.Equal(TaskState.Completed, task!.Status.State);
        Assert.Equal(
            PlannerTaskStoreConventions.DomainStates.Unremediable,
            task.Status.Message!.Parts.Single().Text);
    }

    [Fact]
    public async Task RequireApprovalAsync_WorkingTask_PersistsAuthRequiredStatus()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateSubmittedTask(), CancellationToken.None);
        var lifecycle = new PlannerTaskLifecycle(store, new ChannelEventNotifier());
        await lifecycle.StartWorkAsync("task-1", "anomaly-1", CancellationToken.None);

        await lifecycle.RequireApprovalAsync("task-1", "anomaly-1", CancellationToken.None);

        var task = await store.GetTaskAsync("task-1", CancellationToken.None);
        Assert.Equal(TaskState.AuthRequired, task!.Status.State);
        Assert.Equal(
            PlannerTaskStoreConventions.DomainStates.Waiting,
            task.Status.Message!.Parts.Single().Text);
    }

    [Fact]
    public async Task FailAsync_WorkingTask_PersistsFailedStatusAndReason()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateSubmittedTask(), CancellationToken.None);
        var lifecycle = new PlannerTaskLifecycle(store, new ChannelEventNotifier());
        await lifecycle.StartWorkAsync("task-1", "anomaly-1", CancellationToken.None);

        await lifecycle.FailAsync("task-1", "anomaly-1", "MCP unavailable", CancellationToken.None);

        var task = await store.GetTaskAsync("task-1", CancellationToken.None);
        Assert.Equal(TaskState.Failed, task!.Status.State);
        Assert.Equal("MCP unavailable", task.Status.Message!.Parts.Single().Text);
    }

    [Fact]
    public async Task CompleteAsync_WorkingTask_PersistsCompletedStatusAndOutcome()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateSubmittedTask(), CancellationToken.None);
        var lifecycle = new PlannerTaskLifecycle(store, new ChannelEventNotifier());
        await lifecycle.StartWorkAsync("task-1", "anomaly-1", CancellationToken.None);

        await lifecycle.CompleteAsync("task-1", "anomaly-1", "applied", CancellationToken.None);

        var task = await store.GetTaskAsync("task-1", CancellationToken.None);
        Assert.Equal(TaskState.Completed, task!.Status.State);
        Assert.Equal("applied", task.Status.Message!.Parts.Single().Text);
    }

    [Fact]
    public async Task RejectAsync_WorkingTask_PersistsRejectedStatusAndReason()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateSubmittedTask(), CancellationToken.None);
        var lifecycle = new PlannerTaskLifecycle(store, new ChannelEventNotifier());
        await lifecycle.StartWorkAsync("task-1", "anomaly-1", CancellationToken.None);

        await lifecycle.RejectAsync("task-1", "anomaly-1", "approval rejected", CancellationToken.None);

        var task = await store.GetTaskAsync("task-1", CancellationToken.None);
        Assert.Equal(TaskState.Rejected, task!.Status.State);
        Assert.Equal("approval rejected", task.Status.Message!.Parts.Single().Text);
    }

    private static AgentTask CreateSubmittedTask() =>
        new()
        {
            Id = "task-1",
            ContextId = "anomaly-1",
            Status = new TaskStatus
            {
                State = TaskState.Submitted,
                Timestamp = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            },
        };
}
