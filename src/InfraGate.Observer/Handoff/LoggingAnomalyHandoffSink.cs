using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Logging;

namespace InfraGate.Observer.Handoff;

internal sealed class LoggingAnomalyHandoffSink : IAnomalyHandoffSink
{
    private readonly ILogger<LoggingAnomalyHandoffSink> logger;

    public LoggingAnomalyHandoffSink(ILogger<LoggingAnomalyHandoffSink> logger)
    {
        this.logger = logger;
    }

    public Task PublishAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken)
    {
        foreach (var report in batch.Reports)
        {
            ObserverLogEvents.LogAnomalyReport(
                logger,
                batch.CycleId,
                report.AnomalyId,
                report.Kind.ToString(),
                report.Severity.ToString(),
                report.Status.ToString(),
                $"{report.Target.Kind}/{report.Target.Name}",
                report.Summary);
        }

        return Task.CompletedTask;
    }
}
