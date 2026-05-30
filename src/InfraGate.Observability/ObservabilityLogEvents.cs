using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace InfraGate.Observability;

internal static partial class ObservabilityLogEvents
{
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "span {SpanName} op={OperationName} model={Model} agent={AgentName} in={InputTokens} out={OutputTokens} duration={DurationMs}ms status={Status} traceId={TraceId} spanId={SpanId}")]
    internal static partial void LogSpanCompleted(
        ILogger logger,
        string spanName,
        string? operationName,
        string? model,
        string? agentName,
        string? inputTokens,
        string? outputTokens,
        double durationMs,
        ActivityStatusCode status,
        string traceId,
        string spanId);
}
