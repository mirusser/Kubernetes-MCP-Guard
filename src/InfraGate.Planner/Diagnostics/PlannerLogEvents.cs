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
}
