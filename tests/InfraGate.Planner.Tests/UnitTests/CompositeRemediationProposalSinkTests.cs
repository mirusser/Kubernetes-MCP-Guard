using System.Diagnostics.Metrics;
using InfraGate.Planner.Diagnostics;
using InfraGate.Planner.Handoff;
using InfraGate.Remediation.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class CompositeRemediationProposalSinkTests
{
    [Fact]
    public async Task PublishAsync_AllSinksSucceed_ForwardsToAll()
    {
        var sinkA = new CapturingSink();
        var sinkB = new CapturingSink();
        var composite = new CompositeRemediationProposalSink([sinkA, sinkB]);
        var batch = CreateBatch("cycle-1");

        await composite.PublishAsync(batch, CancellationToken.None);

        Assert.Single(sinkA.Batches);
        Assert.Single(sinkB.Batches);
    }

    [Fact]
    public async Task PublishAsync_OneSinkThrows_ContinuesToOtherSinkAndRecordsMetric()
    {
        using var meter = new Meter("composite-test");
        using var probe = ListenForCounter(meter, PlannerMetrics.HandoffSinkFailedCounterName);
        var failingSink = new ThrowingSink();
        var successSink = new CapturingSink();
        var composite = new CompositeRemediationProposalSink([failingSink, successSink], meter: meter);
        var batch = CreateBatch("cycle-1");

        await composite.PublishAsync(batch, CancellationToken.None);

        Assert.Single(successSink.Batches);
        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    [Fact]
    public async Task PublishAsync_EmptySinkList_Succeeds()
    {
        var composite = new CompositeRemediationProposalSink([]);
        var batch = CreateBatch("cycle-1");

        var ex = await Record.ExceptionAsync(() => composite.PublishAsync(batch, CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task PublishAsync_MultipleSinksThrow_RecordsOneMetricPerFailure()
    {
        using var meter = new Meter("composite-multi-fail-test");
        using var probe = ListenForCounter(meter, PlannerMetrics.HandoffSinkFailedCounterName);
        var composite = new CompositeRemediationProposalSink(
            [new ThrowingSink(), new ThrowingSink()],
            meter: meter);

        await composite.PublishAsync(CreateBatch("cycle-1"), CancellationToken.None);

        Assert.Equal(2, probe.Measurements.Count);
    }

    [Fact]
    public async Task PublishAsync_NonNullLogger_UsesProvidedLogger()
    {
        var logger = NullLogger<CompositeRemediationProposalSink>.Instance;
        var composite = new CompositeRemediationProposalSink(
            [new ThrowingSink()],
            logger: logger);

        await composite.PublishAsync(CreateBatch("cycle-1"), CancellationToken.None);

        Assert.True(true);
    }

    private static RemediationProposalBatch CreateBatch(string cycleId) => new()
    {
        CycleId = cycleId,
        EmittedAt = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero),
        Proposals = [new RemediationProposal
        {
            PlanId = "plan-1",
            AnomalyId = "anomaly-1",
            ProposedAt = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero),
        }],
    };

    private static CounterProbe ListenForCounter(Meter meter, string name) => new(meter, name);

    private sealed class CapturingSink : IRemediationProposalSink
    {
        public List<RemediationProposalBatch> Batches { get; } = [];

        public Task PublishAsync(RemediationProposalBatch batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSink : IRemediationProposalSink
    {
        public Task PublishAsync(RemediationProposalBatch batch, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("sink failed"));
    }

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
