using Microsoft.Extensions.Logging;

namespace InfraGate.Executor.Diagnostics;

internal static partial class ExecutorLogEvents
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Executor MCP client connected to {GatewayBaseUrl}")]
    public static partial void LogStartupConnected(ILogger logger, string gatewayBaseUrl);

    [LoggerMessage(Level = LogLevel.Error, Message = "Executor MCP client failed to connect to {GatewayBaseUrl} (authority={Authority}, scope={Scope}, clientId={ClientId})")]
    public static partial void LogStartupConnectionFailed(ILogger logger, string gatewayBaseUrl, string authority, string scope, string clientId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Executor health check: token not yet acquired")]
    public static partial void LogHealthCheckStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Executor health check failed")]
    public static partial void LogHealthCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handoff batch received: cycleId={CycleId} proposals={ProposalCount}")]
    public static partial void LogHandoffBatchReceived(ILogger logger, string cycleId, int proposalCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Executor handoff rejected: concurrency cap reached for cycleId={CycleId} proposals={ProposalCount}")]
    public static partial void LogHandoffCapacityRejected(ILogger logger, string cycleId, int proposalCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "executor.watch.started planId={PlanId}")]
    public static partial void LogWatchStarted(ILogger logger, string planId);

    [LoggerMessage(Level = LogLevel.Information, Message = "executor.watch.approved planId={PlanId}")]
    public static partial void LogWatchApproved(ILogger logger, string planId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "executor.watch.timeout planId={PlanId}")]
    public static partial void LogWatchTimeout(ILogger logger, string planId);

    [LoggerMessage(Level = LogLevel.Error, Message = "executor.watch.failed planId={PlanId}")]
    public static partial void LogWatchFailed(ILogger logger, string planId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "executor.execute.succeeded planId={PlanId}")]
    public static partial void LogExecuteSucceeded(ILogger logger, string planId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "executor.execute.failed planId={PlanId}")]
    public static partial void LogExecuteFailed(ILogger logger, string planId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "executor.execute.blocked planId={PlanId}")]
    public static partial void LogExecuteBlocked(ILogger logger, string planId);
}
