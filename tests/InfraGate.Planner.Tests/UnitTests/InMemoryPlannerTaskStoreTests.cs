using A2A;
using InfraGate.Planner.Tasks;
using TaskStatus = A2A.TaskStatus;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class InMemoryPlannerTaskStoreTests
{
    [Fact]
    public async Task TryCreateTaskAsync_SameContextIdTwice_CreatesOnlyFirstTask()
    {
        var store = new InMemoryPlannerTaskStore();

        bool firstCreated = await store.TryCreateTaskAsync(
            "task-1",
            CreateTask("task-1", "ctx-1"),
            CancellationToken.None);
        bool secondCreated = await store.TryCreateTaskAsync(
            "task-2",
            CreateTask("task-2", "ctx-1"),
            CancellationToken.None);

        Assert.True(firstCreated);
        Assert.False(secondCreated);
        var listed = await store.ListTasksAsync(new ListTasksRequest { ContextId = "ctx-1" }, CancellationToken.None);
        Assert.Equal("task-1", Assert.Single(listed.Tasks).Id);
    }

    [Fact]
    public async Task SaveTaskAsync_ContextClaimedByDifferentTask_ThrowsInvalidOperationException()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateTask("task-1", "ctx-1"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveTaskAsync("task-2", CreateTask("task-2", "ctx-1"), CancellationToken.None));

        Assert.Contains("already claimed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveTaskAsync_SameTaskId_UpdatesExistingTask()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateTask("task-1", "ctx-1"), CancellationToken.None);

        var updated = CreateTask("task-1", "ctx-1");
        updated.Status = new TaskStatus { State = TaskState.Completed, Timestamp = DateTimeOffset.UtcNow };
        await store.SaveTaskAsync("task-1", updated, CancellationToken.None);

        var retrieved = await store.GetTaskAsync("task-1", CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal(TaskState.Completed, retrieved.Status.State);
    }

    [Fact]
    public async Task DeleteTaskAsync_ExistingTask_RemovesTaskAndClaim()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateTask("task-1", "ctx-1"), CancellationToken.None);

        await store.DeleteTaskAsync("task-1", CancellationToken.None);

        var retrieved = await store.GetTaskAsync("task-1", CancellationToken.None);
        Assert.Null(retrieved);

        bool canCreateAgain = await store.TryCreateTaskAsync("task-2", CreateTask("task-2", "ctx-1"), CancellationToken.None);
        Assert.True(canCreateAgain);
    }

    [Fact]
    public async Task DeleteTaskAsync_NonExistentTask_DoesNotThrow()
    {
        var store = new InMemoryPlannerTaskStore();

        var ex = await Record.ExceptionAsync(
            () => store.DeleteTaskAsync("nonexistent", CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ListTasksAsync_ByContextId_ReturnsOnlyMatchingTasks()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateTask("task-1", "ctx-1"), CancellationToken.None);
        await store.TryCreateTaskAsync("task-2", CreateTask("task-2", "ctx-2"), CancellationToken.None);

        var response = await store.ListTasksAsync(
            new ListTasksRequest { ContextId = "ctx-1" },
            CancellationToken.None);

        Assert.Single(response.Tasks);
        Assert.Equal("task-1", response.Tasks[0].Id);
        Assert.Equal(1, response.TotalSize);
    }

    [Fact]
    public async Task ListTasksAsync_ByStatus_ReturnsOnlyMatchingTasks()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateTask("task-1", "ctx-1"), CancellationToken.None);
        var completed = CreateTask("task-2", "ctx-2");
        completed.Status = new TaskStatus { State = TaskState.Completed, Timestamp = DateTimeOffset.UtcNow };
        await store.TryCreateTaskAsync("task-2", completed, CancellationToken.None);

        var response = await store.ListTasksAsync(
            new ListTasksRequest { Status = TaskState.Completed },
            CancellationToken.None);

        Assert.Single(response.Tasks);
        Assert.Equal("task-2", response.Tasks[0].Id);
        Assert.Equal(1, response.TotalSize);
    }

    private static AgentTask CreateTask(string id, string contextId) =>
        new()
        {
            Id = id,
            ContextId = contextId,
            Status = new TaskStatus { State = TaskState.Submitted, Timestamp = DateTimeOffset.UtcNow },
        };
}
