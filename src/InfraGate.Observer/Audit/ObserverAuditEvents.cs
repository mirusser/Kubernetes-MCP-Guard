namespace InfraGate.Observer.Audit;

internal static class ObserverAuditEvents
{
    public const string AnomalyDetected = "anomaly.detected";
    public const string AnomalySuppressed = "anomaly.suppressed";
    public const string AnomalyResolved = "anomaly.resolved";
    public const string HandoffPublished = "handoff.published";
    public const string HandoffFailed = "handoff.failed";
    public const string HandoffToolServed = "handoff.tool_served";
    public const string HandoffToolDenied = "handoff.tool_denied";

    public static class Outcomes
    {
        public const string Received = "received";
        public const string Denied = "denied";
        public const string ToolError = "tool_error";
        public const string Served = "served";
    }

    public static class Subjects
    {
        public const string Planner = "service:planner";
    }
}
