using System.Diagnostics.Metrics;
using InfraGate.Observer.Audit;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class A2AAnomalyHandoffSinkTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static A2AAnomalyHandoffSink CreateSink(
        FakePlannerHandoffClient client,
        Meter? meter = null,
        CapturingAuditOutbox? auditOutbox = null) =>
        new(client, NullLogger<A2AAnomalyHandoffSink>.Instance, auditOutbox, meter);

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
        var client = new FakePlannerHandoffClient();
        var sink = CreateSink(client);

        await sink.PublishAsync(EmptyBatch(), CancellationToken.None);

        Assert.False(client.WasInvoked);
    }

    // ── Success path ─────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_ValidBatch_InvokesAgent()
    {
        var client = new FakePlannerHandoffClient();
        var sink = CreateSink(client);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        Assert.True(client.WasInvoked);
    }

    [Fact]
    public async Task PublishAsync_ValidBatch_SendsSerializedJson()
    {
        var client = new FakePlannerHandoffClient();
        var sink = CreateSink(client);
        var batch = BatchWithReport("cycle-json");

        await sink.PublishAsync(batch, CancellationToken.None);

        Assert.Equal("cycle-json", Assert.Single(client.Batches).CycleId);
    }

    [Fact]
    public async Task PublishAsync_MultipleReports_SendsOneBatchPerAnomalyContext()
    {
        var client = new FakePlannerHandoffClient();
        var sink = CreateSink(client);
        var batch = BatchWithReport();
        var firstReport = Assert.Single(batch.Reports);
        batch = batch with
        {
            Reports =
            [
                firstReport,
                firstReport with { AnomalyId = "anomaly-2" },
            ],
        };

        await sink.PublishAsync(batch, CancellationToken.None);

        Assert.Equal(["anomaly-1", "anomaly-2"], client.ContextIds);
        Assert.All(client.Batches, sentBatch => Assert.Single(sentBatch.Reports));
    }

    [Fact]
    public async Task PublishAsync_Success_EmitsHandoffPublishedAuditEvent()
    {
        var auditOutbox = new CapturingAuditOutbox();
        var client = new FakePlannerHandoffClient();
        var sink = CreateSink(client, auditOutbox: auditOutbox);

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
        var client = new FakePlannerHandoffClient();
        var sink = CreateSink(client, meter: meter);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        Assert.Empty(probe.Measurements);
    }

    // ── Failure path ─────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_AgentThrows_EmitsHandoffFailedAuditEvent()
    {
        var auditOutbox = new CapturingAuditOutbox();
        var client = new FakePlannerHandoffClient(shouldThrow: true);
        var sink = CreateSink(client, auditOutbox: auditOutbox);

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
        var client = new FakePlannerHandoffClient(shouldThrow: true);
        var sink = CreateSink(client, meter: meter);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    [Fact]
    public async Task PublishAsync_AgentThrows_DoesNotRethrow()
    {
        var client = new FakePlannerHandoffClient(shouldThrow: true);
        var sink = CreateSink(client);

        var ex = await Record.ExceptionAsync(() => sink.PublishAsync(BatchWithReport(), CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task PublishAsync_NullAuditOutbox_DoesNotThrowOnSuccess()
    {
        var client = new FakePlannerHandoffClient();
        var sink = new A2AAnomalyHandoffSink(client, NullLogger<A2AAnomalyHandoffSink>.Instance);

        var ex = await Record.ExceptionAsync(() => sink.PublishAsync(BatchWithReport(), CancellationToken.None));

        Assert.Null(ex);
    }

    // ── Fake handoff client ───────────────────────────────────────────

    private sealed class FakePlannerHandoffClient(bool shouldThrow = false) : IPlannerHandoffClient
    {
        public bool WasInvoked => ContextIds.Count > 0;
        public List<string> ContextIds { get; } = [];
        public List<AnomalyHandoffBatch> Batches { get; } = [];

        public Task SendAsync(
            string contextId,
            AnomalyHandoffBatch batch,
            CancellationToken cancellationToken)
        {
            if (shouldThrow)
                throw new InvalidOperationException("Simulated A2A handoff failure");

            ContextIds.Add(contextId);
            Batches.Add(batch);
            return Task.CompletedTask;
        }
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
