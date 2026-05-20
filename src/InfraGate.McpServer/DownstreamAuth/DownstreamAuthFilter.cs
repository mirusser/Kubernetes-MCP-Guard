using InfraGate.DownstreamAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.McpServer.DownstreamAuth;

/// <summary>
/// MCP request filter that validates the downstream service JWT on listTools and callTool requests.
/// The token must appear in request params _meta under the key defined by
/// <see cref="DownstreamAuthConventions.MetaKey"/> as a Bearer token.
/// </summary>
internal static class DownstreamAuthFilter
{
    // TODO (Task 6/bootstrap gate): Startup validation without a valid service credential
    // is not handled here — that belongs to the bootstrap gate for 'initialize'.

    internal static McpRequestFilter<ListToolsRequestParams, ListToolsResult> ListTools()
    {
        return next => (request, cancellationToken) =>
            ValidateAndContinueAsync<ListToolsRequestParams, ListToolsResult>(
                request,
                next,
                cancellationToken);
    }

    internal static McpRequestFilter<CallToolRequestParams, CallToolResult> CallTool()
    {
        return next => (request, cancellationToken) =>
            ValidateAndContinueAsync<CallToolRequestParams, CallToolResult>(
                request,
                next,
                cancellationToken);
    }

    private static async ValueTask<TResult> ValidateAndContinueAsync<TParams, TResult>(
        RequestContext<TParams> request,
        McpRequestHandler<TParams, TResult> next,
        CancellationToken cancellationToken)
        where TParams : RequestParams
    {
        var validator = request.Services?.GetService<DownstreamTokenValidator>();
        if (validator is null)
        {
            // No validator registered — this means Required=false; pass through
            return await next(request, cancellationToken).ConfigureAwait(false);
        }

        string? rawToken = ExtractBearerToken(request.Params?.Meta);

        var result = await validator.ValidateAsync(rawToken ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsValid)
        {
            var logger = request.Services?.GetService<ILoggerFactory>()
                ?.CreateLogger("InfraGate.McpServer.DownstreamAuthFilter");
            logger?.LogWarning("Downstream auth rejected: {Reason}", result.FailureReason);

            throw new McpException(
                $"downstream_auth_required: {result.FailureReason}");
        }

        return await next(request, cancellationToken).ConfigureAwait(false);
    }

    private static string? ExtractBearerToken(System.Text.Json.Nodes.JsonObject? meta)
    {
        if (meta is null)
        {
            return null;
        }

        if (!meta.TryGetPropertyValue(DownstreamAuthConventions.MetaKey, out var node))
        {
            return null;
        }

        string? value = node?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.StartsWith(DownstreamAuthConventions.BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value[DownstreamAuthConventions.BearerPrefix.Length..];
        }

        return value;
    }
}
