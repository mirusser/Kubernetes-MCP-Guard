namespace InfraGate.Observer.Audit;

internal static class ObserverAuditEvents
{
    public const string AnomalyDetected = "anomaly.detected";
    public const string AnomalySuppressed = "anomaly.suppressed";
    public const string AnomalyResolved = "anomaly.resolved";
    public const string HandoffPublished = "handoff.published";
    public const string HandoffFailed = "handoff.failed";
}
