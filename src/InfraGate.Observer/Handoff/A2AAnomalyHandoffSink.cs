using System.Diagnostics.Metrics;
using InfraGate.Observer.Audit;
using InfraGate.Observer.Diagnostics;

namespace InfraGate.Observer.Handoff;

internal sealed class A2AAnomalyHandoffSink(
    IPlannerHandoffClient client,
    ILogger<A2AAnomalyHandoffSink> logger,
    IObserverAuditOutbox? auditOutbox = null,
    Meter? meter = null) : IAnomalyHandoffSink
{
    private readonly Counter<long> failedCounter = ObserverMetrics.CreateHandoffHttpFailedCounter(meter);

    public async Task PublishAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Reports.Count == 0)
            return;

        try
        {
            foreach (var report in batch.Reports)
            {
                ObserverLogEvents.LogHandoffA2ASending(
                    logger, batch.CycleId, report.AnomalyId,
                    report.Kind.ToString(), report.Severity.ToString(), report.Status.ToString());

                await client.SendAsync(
                    report.AnomalyId,
                    batch with { Reports = [report] },
                    cancellationToken).ConfigureAwait(false);
            }

            ObserverLogEvents.LogHandoffA2ASent(logger, batch.CycleId, batch.Reports.Count);

            if (auditOutbox is not null)
                await EmitHandoffPublishedAsync(batch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ObserverLogEvents.LogHandoffA2AFailed(logger, ex.GetType().Name, ex);
            failedCounter.Add(1);

            if (auditOutbox is not null)
                await EmitHandoffFailedAsync(batch, ex.GetType().Name, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task EmitHandoffPublishedAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken) =>
        auditOutbox!.AppendAsync(new ObserverAuditEntry(
            EventName: ObserverAuditEvents.HandoffPublished,
            Payload: new
            {
                batchSize = batch.Reports.Count,
                anomalyIds = batch.Reports.Select(r => r.AnomalyId).ToArray(),
                sinkType = "a2a",
            },
            ActorSubject: "service:observer",
            CycleId: batch.CycleId,
            Outcome: "published"),
        cancellationToken);

    private Task EmitHandoffFailedAsync(
        AnomalyHandoffBatch batch,
        string errorClass,
        CancellationToken cancellationToken) =>
        auditOutbox!.AppendAsync(new ObserverAuditEntry(
            EventName: ObserverAuditEvents.HandoffFailed,
            Payload: new
            {
                batchSize = batch.Reports.Count,
                errorClass,
                sinkType = "a2a",
            },
            ActorSubject: "service:observer",
            CycleId: batch.CycleId,
            Outcome: "failed"),
        cancellationToken);
}
