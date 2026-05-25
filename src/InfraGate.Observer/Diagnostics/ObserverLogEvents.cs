using Microsoft.Extensions.Logging;

namespace InfraGate.Observer.Diagnostics;

internal static partial class ObserverLogEvents
{
    // ── Startup / Shutdown ─────────────────────────────────────

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Observer starting with cadence {CadenceSeconds}s")]
    public static partial void LogObserverStarting(ILogger logger, int cadenceSeconds);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Observer stopping")]
    public static partial void LogObserverStopping(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "observer.startup.connected Gateway={Gateway} AllowedNamespaces={AllowedNamespaces}")]
    public static partial void LogStartupConnected(ILogger logger, string gateway, string allowedNamespaces);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "observer.startup.connection_failed Gateway={Gateway} Authority={Authority} Scope={Scope} ClientId={ClientId}")]
    public static partial void LogStartupConnectionFailed(ILogger logger, string gateway, string authority, string scope, string clientId, Exception ex);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Connected to MCP gateway at {GatewayBaseUrl}")]
    public static partial void LogMcpConnected(ILogger logger, string gatewayBaseUrl);

    // ── Health ──────────────────────────────────────────────────

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Health check: token has not been acquired yet")]
    public static partial void LogHealthCheckStarting(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Health check: token acquisition failed")]
    public static partial void LogHealthCheckFailed(ILogger logger, Exception ex);

    // ── Cycle lifecycle ─────────────────────────────────────────

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Observation cycle cancelled: host shutting down")]
    public static partial void LogCycleCancelled(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Observation cycle truncated: wall-clock cap reached")]
    public static partial void LogCycleTruncated(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Observation cycle skipped: previous cycle still executing")]
    public static partial void LogCycleSkipped(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unhandled exception in observation cycle")]
    public static partial void LogCycleError(ILogger logger, Exception ex);

    // ── Cycle results ───────────────────────────────────────────

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "observer.cycle.completed CycleId={CycleId} Reports={ReportCount} Duration={DurationMs}ms")]
    public static partial void LogCycleCompleted(ILogger logger, string cycleId, int reportCount, long durationMs);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "observer.cycle.truncated CycleId={CycleId} ToolCalls={ToolCalls} Duration={DurationMs}ms")]
    public static partial void LogCycleTruncatedWithDetails(ILogger logger, string cycleId, int toolCalls, long durationMs);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Cycle {CycleId} complete. Reports={ReportCount} Emitted={Emitted} Resolved={Resolved} ToolCalls={ToolCalls} Disagreements={Disagreements} Duration={DurationMs}ms")]
    public static partial void LogCycleCompletedDetailed(ILogger logger, string cycleId, int reportCount, int emitted, int resolved, int toolCalls, int disagreements, long durationMs); // NOSONAR:S107 — structured audit log with many dimensions.

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Cycle {CycleId} truncated — emitting no reports. ToolCalls={ToolCalls}")]
    public static partial void LogTruncatedNoReports(ILogger logger, string cycleId, int toolCalls);

    // ── Analysis ────────────────────────────────────────────────

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Severity disagreement: LLM={LlmSeverity} Classifier={ClassifierSeverity} Rule={Rule} Kind={Kind} Target={Target}")]
    public static partial void LogSeverityDisagreement(ILogger logger, string llmSeverity, string classifierSeverity, string rule, string kind, string target);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Tool call failed: {ToolName}")]
    public static partial void LogToolCallFailed(ILogger logger, string toolName, Exception ex);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to fetch {ToolName} for namespace {Namespace}")]
    public static partial void LogSnapshotFetchFailed(ILogger logger, string toolName, string @namespace, Exception ex);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to extract JSON array from LLM output for namespace {Namespace}")]
    public static partial void LogJsonArrayExtractFailed(ILogger logger, string @namespace);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to parse LLM output as JSON array for namespace {Namespace}")]
    public static partial void LogJsonParseFailed(ILogger logger, string @namespace, Exception ex);

    // ── Handoff ─────────────────────────────────────────────────

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Handoff sink '{SinkName}' failed: {ErrorMessage}")]
    public static partial void LogHandoffSinkFailed(ILogger logger, string sinkName, string errorMessage, Exception ex);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Anomaly Report: CycleId={CycleId} AnomalyId={AnomalyId} Kind={Kind} Severity={Severity} Status={Status} Target={Target} Summary={Summary}")]
    public static partial void LogAnomalyReport(ILogger logger, string cycleId, string anomalyId, string kind, string severity, string status, string target, string summary); // NOSONAR:S107 — structured audit log with many dimensions.

    // ── Observe‑Now ──────────────────────────────────────────────

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "On-demand observation cycle triggered")]
    public static partial void LogObserveNowTriggered(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "On-demand observation cycle timed out")]
    public static partial void LogObserveNowTimeout(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "On-demand observation cycle failed")]
    public static partial void LogObserveNowError(ILogger logger, Exception ex);


}
