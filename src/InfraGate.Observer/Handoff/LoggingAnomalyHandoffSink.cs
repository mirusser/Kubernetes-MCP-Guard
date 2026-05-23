using Microsoft.Extensions.Logging;

namespace InfraGate.Observer.Handoff;

internal sealed partial class LoggingAnomalyHandoffSink : IAnomalyHandoffSink
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
            LogReport(
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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Anomaly Report: CycleId={CycleId} AnomalyId={AnomalyId} Kind={Kind} Severity={Severity} Status={Status} Target={Target} Summary={Summary}")]
    private static partial void LogReport(
        ILogger logger,
        string cycleId,
        string anomalyId,
        string kind,
        string severity,
        string status,
        string target,
        string summary);
}
