using System.Text.Json;
using A2A;
using InfraGate.Observer.Contracts;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Cycle;
using InfraGate.Planner.Handoff;
using InfraGate.Planner.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerHandoffAgentHandlerTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static RequestContext ContextWithBatch(
        AnomalyHandoffBatch batch,
        string taskId = "task-1",
        string contextId = "anomaly-1")
    {
        string json = JsonSerializer.Serialize(batch);
        return new RequestContext
        {
            TaskId = taskId,
            ContextId = contextId,
            StreamingResponse = false,
            Message = new Message
            {
                MessageId = "msg-1",
                Role = Role.User,
                Parts = [new Part { Text = json }],
            },
        };
    }

    private static AnomalyHandoffBatch SampleBatch(string cycleId = "cycle-1") => new()
    {
        CycleId = cycleId,
        EmittedAt = new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero),
        Reports =
        [
            new AnomalyReport
            {
                AnomalyId = "anomaly-1",
                CycleId = cycleId,
                DetectedAt = new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero),
                Kind = AnomalyKind.DeploymentUnavailable,
                Target = new ResourceRef { ApiVersion = "apps/v1", Kind = "Deployment", Namespace = "default", Name = "nginx" },
                Severity = Severity.High,
                Status = AnomalyStatus.Active,
                Summary = "Deployment is unavailable",
                Evidence = [],
                Annotations = new Dictionary<string, string>(),
            },
        ],
    };

    private static PlannerHandoffAgentHandler CreateHandler(
        AnomalyBatchQueue? queue = null,
        IPlannerTaskStore? taskStore = null,
        CapturingAuditOutbox? auditOutbox = null) =>
        new(
            queue ?? new AnomalyBatchQueue(),
            taskStore ?? new InMemoryPlannerTaskStore(),
            NullLogger<PlannerHandoffAgentHandler>.Instance,
            auditOutbox);

    private static async Task<List<StreamResponse>> ExecuteAndDrainAsync(
        PlannerHandoffAgentHandler handler,
        RequestContext context)
    {
        var events = new List<StreamResponse>();
        var eventQueue = new AgentEventQueue();
        var readerTask = DrainAsync(eventQueue, events);
        try
        {
            await handler.ExecuteAsync(context, eventQueue, CancellationToken.None);
        }
        finally
        {
            eventQueue.Complete(null);
            await readerTask;
        }
        return events;
    }

    private static async Task DrainAsync(AgentEventQueue queue, List<StreamResponse> events)
    {
        await foreach (var e in queue.WithCancellation(CancellationToken.None))
            events.Add(e);
    }

    // ── Batch enqueue ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ValidBatch_EnqueuesAnomalyBatch()
    {
        var batchQueue = new AnomalyBatchQueue();
        var handler = CreateHandler(queue: batchQueue);
        var batch = SampleBatch();

        await ExecuteAndDrainAsync(handler, ContextWithBatch(batch));

        bool available = batchQueue.Reader.TryRead(out var dequeued);
        Assert.True(available);
        Assert.Equal("task-1", dequeued!.TaskId);
        Assert.Equal("anomaly-1", dequeued.ContextId);
        Assert.Equal(batch.CycleId, dequeued.Batch.CycleId);
        Assert.Single(dequeued.Batch.Reports);
    }

    [Fact]
    public async Task ExecuteAsync_ValidBatch_EnqueuesSubmittedTask()
    {
        var handler = CreateHandler();
        var batch = SampleBatch();

        var events = await ExecuteAndDrainAsync(handler, ContextWithBatch(batch));

        Assert.Single(events);
        Assert.Equal(StreamResponseCase.Task, events[0].PayloadCase);
        Assert.Equal("task-1", events[0].Task!.Id);
        Assert.Equal("anomaly-1", events[0].Task!.ContextId);
        Assert.Equal(TaskState.Submitted, events[0].Task!.Status.State);
    }

    [Fact]
    public async Task ExecuteAsync_ContextIdDoesNotMatchAnomalyId_ThrowsInvalidParams()
    {
        var taskStore = new InMemoryPlannerTaskStore();
        var handler = CreateHandler(taskStore: taskStore);
        var batch = SampleBatch();

        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            ExecuteAndDrainAsync(handler, ContextWithBatch(batch, contextId: "different-anomaly")));

        Assert.Equal(A2AErrorCode.InvalidParams, ex.ErrorCode);
        var listed = await taskStore.ListTasksAsync(new ListTasksRequest(), CancellationToken.None);
        Assert.Empty(listed.Tasks);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleReports_ThrowsInvalidParams()
    {
        var handler = CreateHandler();
        var batch = SampleBatch();
        var firstReport = Assert.Single(batch.Reports);
        batch = batch with
        {
            Reports =
            [
                firstReport,
                firstReport with { AnomalyId = "anomaly-2" },
            ],
        };

        var ex = await Assert.ThrowsAsync<A2AException>(() =>
            ExecuteAndDrainAsync(handler, ContextWithBatch(batch)));

        Assert.Equal(A2AErrorCode.InvalidParams, ex.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateContextId_AcknowledgesWithoutEnqueueingSecondBatch()
    {
        var batchQueue = new AnomalyBatchQueue();
        var taskStore = new InMemoryPlannerTaskStore();
        var handler = CreateHandler(batchQueue, taskStore);
        var batch = SampleBatch();

        var firstEvents = await ExecuteAndDrainAsync(
            handler,
            ContextWithBatch(batch, taskId: "task-1", contextId: "anomaly-1"));
        var duplicateEvents = await ExecuteAndDrainAsync(
            handler,
            ContextWithBatch(batch, taskId: "task-2", contextId: "anomaly-1"));

        Assert.Equal(StreamResponseCase.Task, Assert.Single(firstEvents).PayloadCase);
        Assert.Equal(StreamResponseCase.Message, Assert.Single(duplicateEvents).PayloadCase);
        Assert.True(batchQueue.Reader.TryRead(out _));
        Assert.False(batchQueue.Reader.TryRead(out _));
        var listed = await taskStore.ListTasksAsync(
            new ListTasksRequest { ContextId = "anomaly-1" },
            CancellationToken.None);
        Assert.Equal("task-1", Assert.Single(listed.Tasks).Id);
    }

    // ── Audit outbox ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ValidBatch_EmitsHandoffReceivedAuditEvent()
    {
        var auditOutbox = new CapturingAuditOutbox();
        var handler = CreateHandler(auditOutbox: auditOutbox);
        var batch = SampleBatch("cycle-audit-1");

        await ExecuteAndDrainAsync(handler, ContextWithBatch(batch));

        Assert.Single(auditOutbox.Entries);
        var entry = auditOutbox.Entries[0];
        Assert.Equal(PlannerAuditEvents.HandoffReceived, entry.EventName);
        Assert.Equal("service:observer", entry.ActorSubject);
        Assert.Equal("received", entry.Outcome);
        var payload = JsonSerializer.SerializeToElement(entry.Payload);
        Assert.Equal("task-1", payload.GetProperty("taskId").GetString());
        Assert.Equal("anomaly-1", payload.GetProperty("contextId").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_NullAuditOutbox_DoesNotThrow()
    {
        var handler = new PlannerHandoffAgentHandler(
            new AnomalyBatchQueue(),
            new InMemoryPlannerTaskStore(),
            NullLogger<PlannerHandoffAgentHandler>.Instance,
            auditOutbox: null);
        var batch = SampleBatch();

        var ex = await Record.ExceptionAsync(() => ExecuteAndDrainAsync(handler, ContextWithBatch(batch)));

        Assert.Null(ex);
    }

    // ── CancelAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CancelAsync_IsNoOp()
    {
        var handler = CreateHandler();
        var context = new RequestContext
        {
            TaskId = "t",
            ContextId = "c",
            StreamingResponse = false,
            Message = new Message { MessageId = "m", Role = Role.User, Parts = [] },
        };
        var eventQueue = new AgentEventQueue();

        var ex = await Record.ExceptionAsync(() =>
            handler.CancelAsync(context, eventQueue, CancellationToken.None));

        Assert.Null(ex);
    }

    // ── Fake audit outbox ─────────────────────────────────────────────

    private sealed class CapturingAuditOutbox : IPlannerAuditOutbox
    {
        public List<PlannerAuditEntry> Entries { get; } = [];

        public Task<long> AppendAsync(PlannerAuditEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.FromResult((long)Entries.Count);
        }

        public Task<long> AppendAsync(
            PlannerAuditEntry entry,
            Npgsql.NpgsqlConnection connection,
            Npgsql.NpgsqlTransaction transaction,
            CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.FromResult((long)Entries.Count);
        }
    }
}
