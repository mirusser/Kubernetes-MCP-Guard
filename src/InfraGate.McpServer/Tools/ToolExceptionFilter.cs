using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.McpServer;

internal static class ToolExceptionFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> CreateSafetyNet(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        ILogger logger)
    {
        return async (request, cancellationToken) =>
        {
            try
            {
                return await next(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Tool '{ToolName}' timed out", request.Params?.Name);
                var message = KubernetesManagerHelpers.FormatApiException($"Tool '{request.Params?.Name}' timed out", ex);
                return ErrorResult(message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Tool '{ToolName}' failed with unhandled exception", request.Params?.Name);
                var message = KubernetesManagerHelpers.FormatApiException($"Tool '{request.Params?.Name}' failed", ex);
                return ErrorResult(message);
            }
        };
    }

    private static CallToolResult ErrorResult(string text) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }]
    };
}
