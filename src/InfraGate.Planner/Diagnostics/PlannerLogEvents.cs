using Microsoft.Extensions.Logging;

namespace InfraGate.Planner.Diagnostics;

internal static partial class PlannerLogEvents
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Planner MCP client connected to {GatewayBaseUrl}; allowed namespaces: {AllowedNamespaces}")]
    public static partial void LogStartupConnected(ILogger logger, string gatewayBaseUrl, string allowedNamespaces);

    [LoggerMessage(Level = LogLevel.Error, Message = "Planner MCP client failed to connect to {GatewayBaseUrl} (authority={Authority}, scope={Scope}, clientId={ClientId})")]
    public static partial void LogStartupConnectionFailed(ILogger logger, string gatewayBaseUrl, string authority, string scope, string clientId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planner health check: token not yet acquired")]
    public static partial void LogHealthCheckStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Planner health check failed")]
    public static partial void LogHealthCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handoff batch received: cycleId={CycleId} reports={ReportCount}")]
    public static partial void LogHandoffBatchReceived(ILogger logger, string cycleId, int reportCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planner rejected remediation decision for anomaly {AnomalyId}: unsupported operation {OperationType}")]
    public static partial void LogDecisionInvalidOperation(ILogger logger, string anomalyId, string operationType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planner rejected remediation decision for anomaly {AnomalyId}: invalid arguments for operation {OperationType}")]
    public static partial void LogDecisionInvalidArguments(ILogger logger, string anomalyId, string operationType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planner decision timed out for anomaly {AnomalyId}")]
    public static partial void LogDecisionTimedOut(ILogger logger, string anomalyId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planner propose_plan failed for anomaly {AnomalyId}")]
    public static partial void LogProposePlanFailed(ILogger logger, string anomalyId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planner propose_plan response for anomaly {AnomalyId} did not include a planId")]
    public static partial void LogProposePlanMissingPlanId(ILogger logger, string anomalyId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Planner batch processing failed for cycle {CycleId}")]
    public static partial void LogBatchProcessingFailed(ILogger logger, string cycleId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Remediation proposal emitted: cycleId={CycleId} anomalyId={AnomalyId} planId={PlanId} proposedAt={ProposedAt}")]
    public static partial void LogRemediationProposal(ILogger logger, string cycleId, string anomalyId, string planId, DateTimeOffset proposedAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "planner.handoff.failed sink={SinkName}: {ErrorMessage}")]
    public static partial void LogHandoffSinkFailed(ILogger logger, string sinkName, string errorMessage, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "planner.handoff.http_failed statusCode={StatusCode}")]
    public static partial void LogHandoffHttpFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "planner.handoff.http_backpressure: executor returned 429")]
    public static partial void LogHandoffHttpBackpressure(ILogger logger);
}
