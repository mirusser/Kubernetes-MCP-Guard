using System.Text.Json;
using A2A;
using InfraGate.Observer.Contracts;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Cycle;
using InfraGate.Planner.Handoff;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerHandoffAgentHandlerTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static RequestContext ContextWithBatch(AnomalyHandoffBatch batch)
    {
        string json = JsonSerializer.Serialize(batch);
        return new RequestContext
        {
            TaskId = "task-1",
            ContextId = "ctx-1",
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
        CapturingAuditOutbox? auditOutbox = null) =>
        new(
            queue ?? new AnomalyBatchQueue(),
            NullLogger<PlannerHandoffAgentHandler>.Instance,
            auditOutbox);

    private static async Task<List<StreamResponse>> ExecuteAndDrainAsync(
        PlannerHandoffAgentHandler handler,
        RequestContext context)
    {
        var events = new List<StreamResponse>();
        var eventQueue = new AgentEventQueue();
        var readerTask = DrainAsync(eventQueue, events);
        await handler.ExecuteAsync(context, eventQueue, CancellationToken.None);
        eventQueue.Complete(null);
        await readerTask;
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
        Assert.Equal(batch.CycleId, dequeued!.CycleId);
        Assert.Single(dequeued.Reports);
    }

    [Fact]
    public async Task ExecuteAsync_ValidBatch_EnqueuesTerminalMessage()
    {
        var handler = CreateHandler();
        var batch = SampleBatch();

        var events = await ExecuteAndDrainAsync(handler, ContextWithBatch(batch));

        Assert.Single(events);
        Assert.Equal(StreamResponseCase.Message, events[0].PayloadCase);
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
    }

    [Fact]
    public async Task ExecuteAsync_NullAuditOutbox_DoesNotThrow()
    {
        var handler = new PlannerHandoffAgentHandler(
            new AnomalyBatchQueue(),
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
