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

    private static AgentTask CreateTask(string id, string contextId) =>
        new()
        {
            Id = id,
            ContextId = contextId,
            Status = new TaskStatus { State = TaskState.Submitted, Timestamp = DateTimeOffset.UtcNow },
        };
}
