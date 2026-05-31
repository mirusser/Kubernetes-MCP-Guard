using System.Diagnostics.Metrics;
using InfraGate.Observer.Audit;
using InfraGate.Observer.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class A2AAnomalyHandoffSinkTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static A2AAnomalyHandoffSink CreateSink(
        FakeA2AAgent agent,
        Meter? meter = null,
        CapturingAuditOutbox? auditOutbox = null) =>
        new(agent, NullLogger<A2AAnomalyHandoffSink>.Instance, auditOutbox, meter);

    private static AnomalyHandoffBatch BatchWithReport(string cycleId = "cycle-1") => new()
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

    private static AnomalyHandoffBatch EmptyBatch() => new()
    {
        CycleId = "cycle-empty",
        EmittedAt = DateTimeOffset.UtcNow,
        Reports = [],
    };

    // ── Skip empty batch ─────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_EmptyBatch_DoesNotCallAgent()
    {
        var agent = new FakeA2AAgent();
        var sink = CreateSink(agent);

        await sink.PublishAsync(EmptyBatch(), CancellationToken.None);

        Assert.False(agent.WasInvoked);
    }

    // ── Success path ─────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_ValidBatch_InvokesAgent()
    {
        var agent = new FakeA2AAgent();
        var sink = CreateSink(agent);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        Assert.True(agent.WasInvoked);
    }

    [Fact]
    public async Task PublishAsync_ValidBatch_SendsSerializedJson()
    {
        var agent = new FakeA2AAgent();
        var sink = CreateSink(agent);
        var batch = BatchWithReport("cycle-json");

        await sink.PublishAsync(batch, CancellationToken.None);

        Assert.NotNull(agent.LastMessage);
        var deserialized = JsonSerializer.Deserialize<AnomalyHandoffBatch>(agent.LastMessage);
        Assert.NotNull(deserialized);
        Assert.Equal("cycle-json", deserialized.CycleId);
    }

    [Fact]
    public async Task PublishAsync_Success_EmitsHandoffPublishedAuditEvent()
    {
        var auditOutbox = new CapturingAuditOutbox();
        var agent = new FakeA2AAgent();
        var sink = CreateSink(agent, auditOutbox: auditOutbox);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        Assert.Single(auditOutbox.Entries);
        var entry = auditOutbox.Entries[0];
        Assert.Equal(ObserverAuditEvents.HandoffPublished, entry.EventName);
        Assert.Equal("published", entry.Outcome);
    }

    [Fact]
    public async Task PublishAsync_Success_DoesNotIncrementFailedCounter()
    {
        using var meter = new Meter("a2a-sink-success-test");
        using var probe = new CounterProbe(meter, ObserverMetrics.HandoffHttpFailedCounterName);
        var agent = new FakeA2AAgent();
        var sink = CreateSink(agent, meter: meter);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        Assert.Empty(probe.Measurements);
    }

    // ── Failure path ─────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_AgentThrows_EmitsHandoffFailedAuditEvent()
    {
        var auditOutbox = new CapturingAuditOutbox();
        var agent = new FakeA2AAgent(shouldThrow: true);
        var sink = CreateSink(agent, auditOutbox: auditOutbox);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        Assert.Single(auditOutbox.Entries);
        var entry = auditOutbox.Entries[0];
        Assert.Equal(ObserverAuditEvents.HandoffFailed, entry.EventName);
        Assert.Equal("failed", entry.Outcome);
    }

    [Fact]
    public async Task PublishAsync_AgentThrows_IncrementsFailedCounter()
    {
        using var meter = new Meter("a2a-sink-fail-test");
        using var probe = new CounterProbe(meter, ObserverMetrics.HandoffHttpFailedCounterName);
        var agent = new FakeA2AAgent(shouldThrow: true);
        var sink = CreateSink(agent, meter: meter);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    [Fact]
    public async Task PublishAsync_AgentThrows_DoesNotRethrow()
    {
        var agent = new FakeA2AAgent(shouldThrow: true);
        var sink = CreateSink(agent);

        var ex = await Record.ExceptionAsync(() => sink.PublishAsync(BatchWithReport(), CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task PublishAsync_NullAuditOutbox_DoesNotThrowOnSuccess()
    {
        var agent = new FakeA2AAgent();
        var sink = new A2AAnomalyHandoffSink(agent, NullLogger<A2AAnomalyHandoffSink>.Instance);

        var ex = await Record.ExceptionAsync(() => sink.PublishAsync(BatchWithReport(), CancellationToken.None));

        Assert.Null(ex);
    }

    // ── Fake agent ────────────────────────────────────────────────────

    internal sealed class FakeA2AAgent(bool shouldThrow = false) : AIAgent
    {
        public bool WasInvoked { get; private set; }
        public string? LastMessage { get; private set; }

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => new(new FakeSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
            => new(default(JsonElement));

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
            => new(new FakeSession());

        protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            LastMessage = messages.FirstOrDefault()?.Text;
            if (shouldThrow)
                throw new InvalidOperationException("Simulated A2A handoff failure");
            return Task.FromResult(new AgentResponse([]));
        }

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Streaming not used by handoff sink");

        private sealed class FakeSession : AgentSession { }
    }

    // ── Fake audit outbox ─────────────────────────────────────────────

    private sealed class CapturingAuditOutbox : IObserverAuditOutbox
    {
        public List<ObserverAuditEntry> Entries { get; } = [];

        public Task<long> AppendAsync(ObserverAuditEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.FromResult((long)Entries.Count);
        }

        public Task<long> AppendAsync(
            ObserverAuditEntry entry,
            Npgsql.NpgsqlConnection connection,
            Npgsql.NpgsqlTransaction transaction,
            CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.FromResult((long)Entries.Count);
        }
    }

    // ── Counter probe ─────────────────────────────────────────────────

    private sealed class CounterProbe : IDisposable
    {
        private readonly MeterListener listener;

        public CounterProbe(Meter meter, string counterName)
        {
            listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == meter && instrument.Name == counterName)
                    l.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>(
                (_, measurement, tags, _) => Measurements.Add(new Measurement<long>(measurement, tags)));
            listener.Start();
        }

        public List<Measurement<long>> Measurements { get; } = [];

        public void Dispose() => listener.Dispose();
    }
}
