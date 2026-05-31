using System.Diagnostics.Metrics;
using System.Net;
using InfraGate.Observer.Audit;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class HttpAnomalyHandoffSinkTests
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
        var sink = CreateSink(handler);
        var batch = new AnomalyHandoffBatch
        {
            CycleId = "cycle-1",
            EmittedAt = DateTimeOffset.UtcNow,
            Reports = [],
        };

        await sink.PublishAsync(batch, CancellationToken.None);

        Assert.False(requestSent);
    }

    [Fact]
    public async Task PublishAsync_AcceptedResponse_DoesNotIncrementCounters()
    {
        using var meter = new Meter("http-anomaly-accepted-test");
        using var failedProbe = ListenForCounter(meter, ObserverMetrics.HandoffHttpFailedCounterName);
        using var backpressureProbe = ListenForCounter(meter, ObserverMetrics.HandoffHttpBackpressureCounterName);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var sink = CreateSink(handler, meter);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        Assert.Empty(failedProbe.Measurements);
        Assert.Empty(backpressureProbe.Measurements);
    }

    [Fact]
    public async Task PublishAsync_TooManyRequests_IncrementsBackpressureCounter()
    {
        using var meter = new Meter("http-anomaly-backpressure-test");
        using var probe = ListenForCounter(meter, ObserverMetrics.HandoffHttpBackpressureCounterName);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var sink = CreateSink(handler, meter);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task PublishAsync_NonSuccessResponse_IncrementsFailedCounter(HttpStatusCode statusCode)
    {
        using var meter = new Meter($"http-anomaly-fail-test-{(int)statusCode}");
        using var probe = ListenForCounter(meter, ObserverMetrics.HandoffHttpFailedCounterName);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(statusCode));
        var sink = CreateSink(handler, meter);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
    }

    private static HttpAnomalyHandoffSink CreateSink(
        StubHttpHandler handler,
        Meter? meter = null,
        IObserverAuditOutbox? auditOutbox = null)
    {
        const string url = "http://planner/handoff/anomalies";
        var client = new HttpClient(handler) { BaseAddress = new Uri(url) };
        return new HttpAnomalyHandoffSink(
            client, url, NullLogger<HttpAnomalyHandoffSink>.Instance, auditOutbox, meter);
    }

    private static AnomalyHandoffBatch BatchWithReport() => new()
    {
        CycleId = "cycle-1",
        EmittedAt = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero),
        Reports =
        [
            new AnomalyReport
            {
                AnomalyId = "anomaly-1",
                CycleId = "cycle-1",
                DetectedAt = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero),
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

    // ── Audit outbox ─────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_AcceptedResponse_EmitsHandoffPublishedEvent()
    {
        var auditOutbox = Substitute.For<IObserverAuditOutbox>();
        auditOutbox.AppendAsync(Arg.Any<ObserverAuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1L));
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var sink = CreateSink(handler, auditOutbox: auditOutbox);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        await auditOutbox.Received(1).AppendAsync(
            Arg.Is<ObserverAuditEntry>(e =>
                e.EventName == ObserverAuditEvents.HandoffPublished &&
                e.Outcome == "published"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task PublishAsync_NonSuccessResponse_EmitsHandoffFailedEvent(HttpStatusCode statusCode)
    {
        var auditOutbox = Substitute.For<IObserverAuditOutbox>();
        auditOutbox.AppendAsync(Arg.Any<ObserverAuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1L));
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(statusCode));
        var sink = CreateSink(handler, auditOutbox: auditOutbox);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        await auditOutbox.Received(1).AppendAsync(
            Arg.Is<ObserverAuditEntry>(e =>
                e.EventName == ObserverAuditEvents.HandoffFailed &&
                e.Outcome == "failed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_TooManyRequests_DoesNotEmitAuditEvent()
    {
        var auditOutbox = Substitute.For<IObserverAuditOutbox>();
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var sink = CreateSink(handler, auditOutbox: auditOutbox);

        await sink.PublishAsync(BatchWithReport(), CancellationToken.None);

        await auditOutbox.DidNotReceive().AppendAsync(
            Arg.Any<ObserverAuditEntry>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_NullAuditOutbox_DoesNotThrowOnSuccess()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var sink = CreateSink(handler, auditOutbox: null);

        var ex = await Record.ExceptionAsync(() => sink.PublishAsync(BatchWithReport(), CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task PublishAsync_NullAuditOutbox_DoesNotThrowOnFailure()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sink = CreateSink(handler, auditOutbox: null);

        var ex = await Record.ExceptionAsync(() => sink.PublishAsync(BatchWithReport(), CancellationToken.None));
        Assert.Null(ex);
    }

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
