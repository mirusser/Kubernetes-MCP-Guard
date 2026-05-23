using System.Diagnostics.Metrics;
using InfraGate.Observer.Diagnostics;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class CompositeAnomalyHandoffSinkTests
{
    [Fact]
    public async Task PublishAsync_InvokesAllSinks()
    {
        var sink1 = new TrackingSink("sink1");
        var sink2 = new TrackingSink("sink2");
        var composite = new CompositeAnomalyHandoffSink([sink1, sink2]);

        var batch = CreateTestBatch();

        await composite.PublishAsync(batch, CancellationToken.None);

        Assert.True(sink1.WasCalled);
        Assert.True(sink2.WasCalled);
        Assert.Same(batch, sink1.ReceivedBatch);
        Assert.Same(batch, sink2.ReceivedBatch);
    }

    [Fact]
    public async Task PublishAsync_ThrowingSinkDoesNotPreventOthers()
    {
        var errSink = new ThrowingSink("err");
        var trackingSink = new TrackingSink("tracking");
        var composite = new CompositeAnomalyHandoffSink([errSink, trackingSink]);

        var batch = CreateTestBatch();

        await composite.PublishAsync(batch, CancellationToken.None);

        Assert.True(trackingSink.WasCalled);
    }

    [Fact]
    public async Task PublishAsync_AllSinksInvokedEvenIfEarlierThrows()
    {
        var errSink = new ThrowingSink("err");
        var sink1 = new TrackingSink("sink1");
        var sink2 = new TrackingSink("sink2");
        var composite = new CompositeAnomalyHandoffSink([sink1, errSink, sink2]);

        var batch = CreateTestBatch();

        await composite.PublishAsync(batch, CancellationToken.None);

        Assert.True(sink1.WasCalled);
        Assert.True(sink2.WasCalled);
    }

    [Fact]
    public async Task PublishAsync_ThrowingSink_IncrementsHandoffFailedCounter()
    {
        using var meter = new Meter(ObserverMetrics.MeterName, ObserverMetrics.MeterVersion);
        var recordedMeasurements = new List<Measurement<long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, listenerForInstrument) =>
        {
            if (instrument.Meter.Name == ObserverMetrics.MeterName
                && instrument.Name == ObserverMetrics.HandoffFailedCounterName)
            {
                listenerForInstrument.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
            {
                recordedMeasurements.Add(new Measurement<long>(measurement, tags));
            });
        listener.Start();

        var errSink = new ThrowingSink("faulty-sink");
        var composite = new CompositeAnomalyHandoffSink([errSink], meter: meter);

        var batch = CreateTestBatch();

        await composite.PublishAsync(batch, CancellationToken.None);

        Assert.Single(recordedMeasurements);
        var recorded = recordedMeasurements[0];
        Assert.Equal(1L, recorded.Value);

        var sinkNameTag = recorded.Tags.ToArray().FirstOrDefault(
            t => t.Key == ObserverMetrics.SinkNameTag);
        Assert.NotEqual(default, sinkNameTag);
        Assert.Equal("ThrowingSink", sinkNameTag.Value);
    }

    private static AnomalyHandoffBatch CreateTestBatch()
    {
        return new AnomalyHandoffBatch
        {
            CycleId = "cycle-001",
            EmittedAt = DateTimeOffset.UtcNow,
            Reports = [],
        };
    }

    private sealed class TrackingSink : IAnomalyHandoffSink
    {
        public string Name { get; }
        public bool WasCalled { get; private set; }
        public AnomalyHandoffBatch? ReceivedBatch { get; private set; }

        public TrackingSink(string name) => Name = name;

        public Task PublishAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedBatch = batch;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSink : IAnomalyHandoffSink
    {
        private readonly string name;

        public ThrowingSink(string name) => this.name = name;

        public Task PublishAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException($"Sink '{name}' failed");
        }
    }
}
