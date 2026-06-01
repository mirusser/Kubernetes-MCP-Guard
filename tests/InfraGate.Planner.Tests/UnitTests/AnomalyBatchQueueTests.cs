using InfraGate.Observer.Contracts;
using InfraGate.Planner.Cycle;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class AnomalyBatchQueueTests
{
    [Fact]
    public void TryEnqueue_ValidBatch_ReturnsTrue()
    {
        var queue = new AnomalyBatchQueue();
        var workItem = CreateWorkItem("cycle-1");

        bool result = queue.TryEnqueue(workItem);

        Assert.True(result);
    }

    [Fact]
    public async Task TryEnqueue_ThenRead_DequeuesCorrectBatch()
    {
        var queue = new AnomalyBatchQueue();
        var workItem = CreateWorkItem("cycle-1");

        queue.TryEnqueue(workItem);

        var dequeued = await queue.Reader.ReadAsync(CancellationToken.None);
        Assert.Equal("task-1", dequeued.TaskId);
        Assert.Equal("anomaly-1", dequeued.ContextId);
        Assert.Equal(workItem.Batch.CycleId, dequeued.Batch.CycleId);
    }

    [Fact]
    public void Writer_IsAccessible()
    {
        var queue = new AnomalyBatchQueue();

        Assert.NotNull(queue.Writer);
    }

    [Fact]
    public void Reader_IsAccessible()
    {
        var queue = new AnomalyBatchQueue();

        Assert.NotNull(queue.Reader);
    }

    [Fact]
    public void TryEnqueue_MultipleItems_AllAvailableToRead()
    {
        var queue = new AnomalyBatchQueue();

        for (int i = 0; i < 5; i++)
        {
            queue.TryEnqueue(CreateWorkItem($"cycle-{i}"));
        }

        for (int i = 0; i < 5; i++)
        {
            Assert.True(queue.Reader.TryRead(out _));
        }
    }

    private static AnomalyHandoffBatch CreateBatch(string cycleId) => new()
    {
        CycleId = cycleId,
        EmittedAt = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero),
        Reports = [],
    };

    private static PlannerTaskWorkItem CreateWorkItem(string cycleId) =>
        new("task-1", "anomaly-1", CreateBatch(cycleId));
}
