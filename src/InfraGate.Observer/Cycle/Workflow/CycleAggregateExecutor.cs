using System.Diagnostics.Metrics;
using InfraGate.Observer.Audit;
using InfraGate.Observer.Diagnostics;
using InfraGate.Observer.State;
using Microsoft.Agents.AI.Workflows;

namespace InfraGate.Observer.Cycle.Workflow;

[YieldsOutput(typeof(CycleResult))]
internal sealed class CycleAggregateExecutor( // NOSONAR:S107 — DI constructor; all params are required services.
    string id,
    int suppressionWindow,
    int resolutionThreshold,
    TimeSpan wallClockElapsed,
    IAnomalyDedupeStore dedupeStore,
    IAnomalyHandoffSink handoffSink,
    IObserverAuditOutbox? auditOutbox,
    ILogger logger,
    Counter<long>? cycleCountCounter,
    Counter<long>? toolCallsCounter,
    Counter<long>? severityDisagreementCounter,
    Counter<long>? reportsEmittedCounter,
    Histogram<double>? cycleDurationHistogram) : Executor<NamespaceParseResult>(id)
{
    private readonly List<NamespaceParseResult> results = [];

    public override ValueTask HandleAsync(
        NamespaceParseResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        results.Add(message);
        return default;
    }

    protected override async ValueTask OnMessageDeliveryFinishedAsync(
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var cycleId = results.Count > 0 ? results[0].CycleId : Guid.NewGuid().ToString("D");
        var allReports = results.SelectMany(r => r.Reports).ToList();
        var totalToolCalls = results.Sum(r => r.ToolCallsUsed);
        var totalDisagreements = results.Sum(r => r.SeverityDisagreements);

        toolCallsCounter?.Add(totalToolCalls);

        var detectedAt = DateTimeOffset.UtcNow;
        var (dedupedReports, resolvedReports, suppressedReports) = dedupeStore.ProcessReports(
            cycleId, allReports, suppressionWindow, resolutionThreshold, detectedAt);

        var finalReports = new List<AnomalyReport>(dedupedReports.Count + resolvedReports.Count);
        finalReports.AddRange(dedupedReports);
        finalReports.AddRange(resolvedReports);

        if (finalReports.Count > 0)
        {
            var handoffBatch = new AnomalyHandoffBatch
            {
                CycleId = cycleId,
                EmittedAt = detectedAt,
                Reports = finalReports,
            };
            await handoffSink.PublishAsync(handoffBatch, cancellationToken).ConfigureAwait(false);
        }

        if (auditOutbox is not null)
        {
            await EmitAuditEventsAsync(cycleId, dedupedReports, suppressedReports, resolvedReports, cancellationToken)
                .ConfigureAwait(false);
        }

        ObserverLogEvents.LogCycleCompletedDetailed(
            logger, cycleId, finalReports.Count, dedupedReports.Count, resolvedReports.Count,
            totalToolCalls, totalDisagreements, (long)wallClockElapsed.TotalMilliseconds);

        cycleCountCounter?.Add(1,
            new KeyValuePair<string, object?>(ObserverMetrics.ResultTag, ObserverMetrics.ResultCompleted));
        cycleDurationHistogram?.Record(wallClockElapsed.TotalMilliseconds);

        if (severityDisagreementCounter is not null && totalDisagreements > 0)
            severityDisagreementCounter.Add(totalDisagreements);

        if (reportsEmittedCounter is not null)
        {
            foreach (var report in finalReports)
            {
                var statusTag = report.Status switch
                {
                    AnomalyStatus.Active => "active",
                    AnomalyStatus.Resolved => "resolved",
                    _ => "unknown",
                };
                reportsEmittedCounter.Add(1,
                    new KeyValuePair<string, object?>(ObserverMetrics.StatusTag, statusTag));
            }
        }

        var result = new CycleResult
        {
            CycleId = cycleId,
            Reports = finalReports,
            IsTruncated = false,
            ToolCallsUsed = totalToolCalls,
            SeverityDisagreements = totalDisagreements,
            Duration = wallClockElapsed,
        };

        await context.YieldOutputAsync(result, cancellationToken).ConfigureAwait(false);
    }

    private async Task EmitAuditEventsAsync(
        string cycleId,
        IReadOnlyList<AnomalyReport> detectedReports,
        IReadOnlyList<AnomalyReport> suppressedReports,
        IReadOnlyList<AnomalyReport> resolvedReports,
        CancellationToken cancellationToken)
    {
        foreach (var report in detectedReports)
        {
            await auditOutbox!.AppendAsync(new ObserverAuditEntry(
                    EventName: ObserverAuditEvents.AnomalyDetected,
                    Payload: new
                    {
                        report.AnomalyId,
                        kind = report.Kind.ToString("G"),
                        severity = report.Severity.ToString("G"),
                        target = $"{report.Target.Kind}/{report.Target.Namespace}/{report.Target.Name}",
                        report.Summary,
                    },
                    ActorSubject: ObserverConventions.Audit.ServiceObserverSubject,
                    CycleId: cycleId,
                    AnomalyId: report.AnomalyId,
                    DedupeKey: DedupeKeyString(report),
                    Outcome: report.Status == AnomalyStatus.Resolved ? ObserverConventions.Audit.Outcomes.Resolved : ObserverConventions.Audit.Outcomes.Active),
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var report in suppressedReports)
        {
            await auditOutbox!.AppendAsync(new ObserverAuditEntry(
                    EventName: ObserverAuditEvents.AnomalySuppressed,
                    Payload: new
                    {
                        report.AnomalyId,
                        kind = report.Kind.ToString("G"),
                        severity = report.Severity.ToString("G"),
                        target = $"{report.Target.Kind}/{report.Target.Namespace}/{report.Target.Name}",
                    },
                    ActorSubject: ObserverConventions.Audit.ServiceObserverSubject,
                    CycleId: cycleId,
                    AnomalyId: report.AnomalyId,
                    DedupeKey: DedupeKeyString(report),
                    Outcome: ObserverConventions.Audit.Outcomes.Suppressed),
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var report in resolvedReports)
        {
            await auditOutbox!.AppendAsync(new ObserverAuditEntry(
                    EventName: ObserverAuditEvents.AnomalyResolved,
                    Payload: new
                    {
                        report.AnomalyId,
                        kind = report.Kind.ToString("G"),
                        target = $"{report.Target.Kind}/{report.Target.Namespace}/{report.Target.Name}",
                    },
                    ActorSubject: ObserverConventions.Audit.ServiceObserverSubject,
                    CycleId: cycleId,
                    AnomalyId: report.AnomalyId,
                    DedupeKey: DedupeKeyString(report),
                    Outcome: ObserverConventions.Audit.Outcomes.Resolved),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static string DedupeKeyString(AnomalyReport report) =>
        $"{report.Kind:G}/{report.Target.Kind}/{report.Target.Namespace}/{report.Target.Name}";
}