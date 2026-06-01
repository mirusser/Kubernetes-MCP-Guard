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

    [LoggerMessage(Level = LogLevel.Warning, Message = "planner.handoff.backpressure: batch queue full, dropped cycleId={CycleId}")]
    public static partial void LogHandoffBatchBackpressure(ILogger logger, string cycleId);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "planner.observer_channel.tool_request_failed cycleId={CycleId} toolName={ToolName}")]
    public static partial void LogToolRequestFailed(ILogger logger, string cycleId, string toolName, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "planner.llm.provider provider={Provider} model={Model}")]
    public static partial void LogLlmProviderConfigured(ILogger logger, string provider, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "planner.llm.call anomalyId={AnomalyId} iteration={Iteration}")]
    public static partial void LogLlmCallStarting(ILogger logger, string anomalyId, int iteration);

    [LoggerMessage(Level = LogLevel.Information, Message = "planner.llm.call_done anomalyId={AnomalyId} iteration={Iteration} durationMs={DurationMs}")]
    public static partial void LogLlmCallCompleted(ILogger logger, string anomalyId, int iteration, long durationMs);

    [LoggerMessage(Level = LogLevel.Debug, Message = "planner.filter.dropped anomalyId={AnomalyId} reason={Reason}")]
    public static partial void LogFilterDropped(ILogger logger, string anomalyId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "planner.decision.completed anomalyId={AnomalyId} operationType={OperationType}")]
    public static partial void LogDecisionCompleted(ILogger logger, string anomalyId, string operationType);

    [LoggerMessage(Level = LogLevel.Information, Message = "planner.propose.succeeded anomalyId={AnomalyId} planId={PlanId}")]
    public static partial void LogProposePlanSucceeded(ILogger logger, string anomalyId, string planId);

    [LoggerMessage(Level = LogLevel.Information, Message = "planner.handoff.published cycleId={CycleId} proposalCount={ProposalCount}")]
    public static partial void LogHandoffPublished(ILogger logger, string cycleId, int proposalCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planner task reconciliation failed: taskId={TaskId} contextId={ContextId}")]
    public static partial void LogTaskReconciliationFailed(ILogger logger, string taskId, string contextId, Exception exception);
}
