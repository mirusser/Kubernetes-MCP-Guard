using System.Diagnostics.Metrics;
using System.Net;
using InfraGate.Planner.Diagnostics;
using InfraGate.Planner.Handoff;
using InfraGate.Remediation.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class HttpRemediationProposalSinkTests
{
    [Fact]
    public async Task PublishAsync_EmptyBatch_DoesNotSendHttpRequest()
    {
        bool requestSent = false;
        var handler = new StubHttpHandler(_ =>
        {
            requestSent = true;
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var sink = CreateSink(handler, "http://executor/handoff");
        var batch = EmptyBatch();

        await sink.PublishAsync(batch, CancellationToken.None);

        Assert.False(requestSent);
    }

    [Fact]
    public async Task PublishAsync_AcceptedResponse_DoesNotIncrementCounters()
    {
        using var meter = new Meter("http-sink-accepted-test");
        using var failedProbe = ListenForCounter(meter, PlannerMetrics.HandoffHttpFailedCounterName);
        using var backpressureProbe = ListenForCounter(meter, PlannerMetrics.HandoffHttpBackpressureCounterName);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var sink = CreateSink(handler, "http://executor/handoff", meter);

        await sink.PublishAsync(BatchWithOneProposal(), CancellationToken.None);

        Assert.Empty(failedProbe.Measurements);
        Assert.Empty(backpressureProbe.Measurements);
    }

    [Fact]
    public async Task PublishAsync_TooManyRequestsResponse_IncrementsBackpressureCounter()
    {
        using var meter = new Meter("http-sink-backpressure-test");
        using var probe = ListenForCounter(meter, PlannerMetrics.HandoffHttpBackpressureCounterName);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var sink = CreateSink(handler, "http://executor/handoff", meter);

        await sink.PublishAsync(BatchWithOneProposal(), CancellationToken.None);

        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task PublishAsync_NonSuccessResponse_IncrementsFailedCounter(HttpStatusCode statusCode)
    {
        using var meter = new Meter($"http-sink-fail-test-{(int)statusCode}");
        using var probe = ListenForCounter(meter, PlannerMetrics.HandoffHttpFailedCounterName);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(statusCode));
        var sink = CreateSink(handler, "http://executor/handoff", meter);

        await sink.PublishAsync(BatchWithOneProposal(), CancellationToken.None);

        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    private static HttpRemediationProposalSink CreateSink(
        StubHttpHandler handler,
        string url,
        Meter? meter = null)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri(url) };
        return new HttpRemediationProposalSink(
            client,
            url,
            NullLogger<HttpRemediationProposalSink>.Instance,
            meter);
    }

    private static RemediationProposalBatch EmptyBatch() => new()
    {
        CycleId = "cycle-1",
        EmittedAt = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero),
        Proposals = [],
    };

    private static RemediationProposalBatch BatchWithOneProposal() => new()
    {
        CycleId = "cycle-1",
        EmittedAt = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero),
        Proposals = [new RemediationProposal
        {
            PlanId = "plan-1",
            AnomalyId = "anomaly-1",
            ProposedAt = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero),
        }],
    };

    private static CounterProbe ListenForCounter(Meter meter, string name) => new(meter, name);

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
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
