using System.Text.Json.Nodes;
using InfraGate.Approvals;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Plan;
using InfraGate.DownstreamAuth;
using InfraGate.McpGateway.DownstreamAuth;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

internal sealed class DownstreamMcpClient(
    McpGatewayOptions options,
    IDownstreamServiceTokenProvider tokenProvider,
    ILogger<DownstreamMcpClient> logger,
    ILoggerFactory loggerFactory) : IDownstreamMcpClient, IToolCaller, IAsyncDisposable
{
    private readonly SemaphoreSlim clientLock = new(1, 1);
    private readonly SemaphoreSlim callLock = new(1, 1);
    private McpClient? client;

    public async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var mcpClient = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        await callLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await WithAuthRetryAsync(async token =>
            {
                var meta = BuildAuthMeta(token);
                var requestOptions = meta is not null ? new RequestOptions { Meta = meta } : null;
                var result = await mcpClient.CallToolAsync(
                    toolName,
                    arguments,
                    progress: null,
                    options: requestOptions,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var text = string.Join(
                    Environment.NewLine,
                    result.Content.OfType<TextContentBlock>().Select(content => content.Text));

                if (result.IsError == true)
                {
                    logger.LogError("Downstream tool '{ToolName}' returned IsError=true. Args={ArgKeys}: {Text}",
                        toolName,
                        string.Join(",", arguments.Keys),
                        text);
                }

                return text;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            callLock.Release();
        }
    }

    public async Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var mcpClient = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        return await WithAuthRetryAsync(async token =>
        {
            var meta = BuildAuthMeta(token);
            var requestOptions = meta is not null ? new RequestOptions { Meta = meta } : null;
            var tools = await mcpClient.ListToolsAsync(requestOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
            return tools
                .Select(t => new DownstreamTool(
                    t.Name,
                    t.Description ?? string.Empty,
                    t.ProtocolTool.Annotations?.ReadOnlyHint ?? false,
                    t.ProtocolTool.Annotations?.DestructiveHint ?? false,
                    t.JsonSchema))
                .ToList() as IReadOnlyList<DownstreamTool>;
        }, cancellationToken).ConfigureAwait(false);
    }

    Task<string> IToolCaller.CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct) =>
        CallToolAsync(toolName, arguments, ct);

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        clientLock.Dispose();
        callLock.Dispose();
    }

    private async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (client is not null)
        {
            return client;
        }

        await clientLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (client is not null)
            {
                return client;
            }

            var transportOptions = CreateTransportOptions();
            string? bootstrapLine = await CreateBootstrapLineAsync(cancellationToken).ConfigureAwait(false);
            IClientTransport transport = bootstrapLine is null
                ? new StdioClientTransport(transportOptions)
                : new BootstrapStdioClientTransport(transportOptions, bootstrapLine, loggerFactory);

            // The MCP 2025-11-25 schema allows initialize params to carry _meta, but the
            // Microsoft MCP .NET SDK 1.3.0 CreateAsync path does not expose an initialize
            // RequestOptions/Meta hook. Until the SDK catches up, the custom transport
            // writes one InfraGate-private authorization line to stdin before CreateAsync
            // sends initialize. Keep the per-request _meta below: it is still the auth
            // boundary for tools/list and tools/call, and it handles token refresh after
            // the one-time initialize bootstrap has completed.
            client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);

            return client;
        }
        finally
        {
            clientLock.Release();
        }
    }

    internal static bool IsDownstreamAuthRejection(Exception ex)
        => ex is McpException mcpEx
            && mcpEx.Message.Contains(DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired, StringComparison.Ordinal);

    internal async Task<T> WithAuthRetryAsync<T>(
        Func<string, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        string token = await tokenProvider.GetServiceTokenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(token).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsDownstreamAuthRejection(ex))
        {
            logger.LogWarning(ex, "Downstream auth rejected; forcing token refresh and retrying once.");
            string refreshedToken = await tokenProvider.RefreshServiceTokenAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await operation(refreshedToken).ConfigureAwait(false);
            }
            catch (Exception retryEx) when (IsDownstreamAuthRejection(retryEx))
            {
                throw new McpException(
                    $"Downstream service authentication failed after token refresh. " +
                    $"Check {DownstreamAuthConventions.EnvironmentVariables.GatewayClientId} configuration.",
                    retryEx);
            }
        }
    }

    internal static JsonObject? BuildAuthMeta(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var meta = new JsonObject();
        meta[DownstreamAuthConventions.MetaKey] = token;
        return meta;
    }

    internal static string? BuildBootstrapLine(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        return $"{DownstreamAuthConventions.BootstrapLineKey}: {token}";
    }

    private async Task<string?> CreateBootstrapLineAsync(CancellationToken cancellationToken)
    {
        if (!IsDownstreamAuthRequired())
        {
            return null;
        }

        string token = await tokenProvider.GetServiceTokenAsync(cancellationToken).ConfigureAwait(false);
        string? bootstrapLine = BuildBootstrapLine(token);
        if (bootstrapLine is null)
        {
            throw new McpException(
                $"{DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired}: " +
                "downstream bootstrap credential is missing.");
        }

        return bootstrapLine;
    }

    private bool IsDownstreamAuthRequired() => options.DownstreamAuth?.Required ?? false;

    internal StdioClientTransportOptions CreateTransportOptions()
    {
        string[] arguments = string.IsNullOrWhiteSpace(options.DownstreamAssembly)
            ? [
                McpGatewayConventions.DownstreamProcess.RunArgument,
                McpGatewayConventions.DownstreamProcess.ProjectArgument,
                options.DownstreamProject
            ]
            : [options.DownstreamAssembly];

        var environmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in McpGatewayConventions.DownstreamProcess.AllowedEnvironmentVariables)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
            {
                environmentVariables[name] = value;
            }
        }

        return new StdioClientTransportOptions
        {
            Name = McpGatewayConventions.DownstreamProcess.Name,
            Command = McpGatewayConventions.DownstreamProcess.Command,
            Arguments = arguments,
            WorkingDirectory = options.WorkingDirectory,
            EnvironmentVariables = environmentVariables,
            ShutdownTimeout = TimeSpan.FromSeconds(10),
            StandardErrorLines = line => logger.LogWarning("[downstream-server stderr] {Line}", line)
        };
    }

}
