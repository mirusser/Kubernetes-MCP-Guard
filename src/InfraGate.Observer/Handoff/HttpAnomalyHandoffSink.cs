using System.Diagnostics.Metrics;
using InfraGate.Observer.Audit;
using InfraGate.Observer.Diagnostics;

namespace InfraGate.Observer.Handoff;

internal sealed class HttpAnomalyHandoffSink : IAnomalyHandoffSink
{
    private readonly HttpClient httpClient;
    private readonly string plannerHandoffUrl;
    private readonly IObserverAuditOutbox? auditOutbox;
    private readonly ILogger<HttpAnomalyHandoffSink> logger;
    private readonly Counter<long> httpFailedCounter;
    private readonly Counter<long> httpBackpressureCounter;

    public HttpAnomalyHandoffSink(
        HttpClient httpClient,
        string plannerHandoffUrl,
        ILogger<HttpAnomalyHandoffSink> logger,
        IObserverAuditOutbox? auditOutbox = null,
        Meter? meter = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrEmpty(plannerHandoffUrl);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.plannerHandoffUrl = plannerHandoffUrl;
        this.auditOutbox = auditOutbox;
        this.logger = logger;
        this.httpFailedCounter = ObserverMetrics.CreateHandoffHttpFailedCounter(meter);
        this.httpBackpressureCounter = ObserverMetrics.CreateHandoffHttpBackpressureCounter(meter);
    }

    public async Task PublishAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Reports.Count == 0)
        {
            return;
        }

        using var response = await httpClient
            .PostAsJsonAsync(plannerHandoffUrl, batch, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            ObserverLogEvents.LogHandoffHttpBackpressure(logger);
            httpBackpressureCounter.Add(1);
            return;
        }

        if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
        {
            ObserverLogEvents.LogHandoffHttpFailed(logger, (int)response.StatusCode);
            httpFailedCounter.Add(1);

            if (auditOutbox is not null)
            {
                await EmitHandoffFailedAsync(batch, (int)response.StatusCode, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        if (auditOutbox is not null)
        {
            await EmitHandoffPublishedAsync(batch, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task EmitHandoffPublishedAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken) =>
        auditOutbox!.AppendAsync(new ObserverAuditEntry(
            EventName: ObserverAuditEvents.HandoffPublished,
            Payload: new
            {
                batchSize = batch.Reports.Count,
                anomalyIds = batch.Reports.Select(r => r.AnomalyId).ToArray(),
                sinkType = "http",
            },
            ActorSubject: "service:observer",
            CycleId: batch.CycleId,
            Outcome: "published"),
        cancellationToken);

    private Task EmitHandoffFailedAsync(
        AnomalyHandoffBatch batch,
        int? statusCode,
        string? errorClass,
        CancellationToken cancellationToken) =>
        auditOutbox!.AppendAsync(new ObserverAuditEntry(
            EventName: ObserverAuditEvents.HandoffFailed,
            Payload: new
            {
                batchSize = batch.Reports.Count,
                statusCode,
                errorClass,
                sinkType = "http",
            },
            ActorSubject: "service:observer",
            CycleId: batch.CycleId,
            Outcome: "failed"),
        cancellationToken);
}
