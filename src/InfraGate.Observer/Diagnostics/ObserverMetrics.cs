using System.Diagnostics.Metrics;

namespace InfraGate.Observer.Diagnostics;

internal static class ObserverMetrics
{
    internal const string MeterName = "InfraGate.Observer";
    internal const string MeterVersion = "1.0";

    // ── Counter names ──────────────────────────────────────────

    internal const string CycleCountCounterName = "infragate.observer.cycle.count";
    internal const string ToolCallsCounterName = "infragate.observer.tool_calls";
    internal const string SeverityDisagreementCounterName = "infragate.observer.severity.disagreement";
    internal const string HandoffFailedCounterName = "infragate.observer.handoff.failed";
    internal const string HandoffHttpFailedCounterName = "infragate.observer.handoff.http_failed";
    internal const string HandoffHttpBackpressureCounterName = "infragate.observer.handoff.http_backpressure";
    internal const string LlmTokensCounterName = "infragate.observer.llm.tokens";
    internal const string ReportsEmittedCounterName = "infragate.observer.reports.emitted";
    internal const string SnapshotFetchErrorsCounterName = "infragate.observer.snapshot.fetch_errors";

    // ── Histogram names ────────────────────────────────────────

    internal const string CycleDurationHistogramName = "infragate.observer.cycle.duration";

    // ── Tag names ──────────────────────────────────────────────

    internal const string ResultTag = "result";
    internal const string StatusTag = "status";
    internal const string ToolNameTag = "tool_name";
    internal const string SinkNameTag = "sink_name";

    // ── Tag values ─────────────────────────────────────────────

    internal const string ResultCompleted = "completed";
    internal const string ResultTruncated = "truncated";
    internal const string ResultError = "error";

    // ── Meter ──────────────────────────────────────────────────

    internal static readonly Meter Meter = new(MeterName, MeterVersion);

    // ── Counter factories ──────────────────────────────────────

    internal static Counter<long> CreateCycleCountCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(CycleCountCounterName);
    }

    internal static Counter<long> CreateToolCallsCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(ToolCallsCounterName);
    }

    internal static Counter<long> CreateSeverityDisagreementCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(SeverityDisagreementCounterName);
    }

    internal static Counter<long> CreateHandoffFailedCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(HandoffFailedCounterName);
    }

    internal static Counter<long> CreateHandoffHttpFailedCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(HandoffHttpFailedCounterName);
    }

    internal static Counter<long> CreateHandoffHttpBackpressureCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(HandoffHttpBackpressureCounterName);
    }

    internal static Counter<long> CreateLlmTokensCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(LlmTokensCounterName);
    }

    internal static Counter<long> CreateReportsEmittedCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(ReportsEmittedCounterName);
    }

    internal static Counter<long> CreateSnapshotFetchErrorsCounter(Meter? meter = null)
    {
        return (meter ?? Meter).CreateCounter<long>(SnapshotFetchErrorsCounterName);
    }

    // ── Histogram factories ────────────────────────────────────

    internal static Histogram<double> CreateCycleDurationHistogram(Meter? meter = null)
    {
        return (meter ?? Meter).CreateHistogram<double>(CycleDurationHistogramName);
    }
}
