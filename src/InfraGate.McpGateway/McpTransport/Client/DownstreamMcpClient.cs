using System.Text.Json.Nodes;
using InfraGate.Approvals.Execution;
using InfraGate.DownstreamAuth;
using InfraGate.McpGateway.DownstreamAuth;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

internal sealed class DownstreamMcpClient(
    DownstreamProcessDescriptor descriptor,
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
        McpClient mcpClient = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        await callLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await WithAuthRetryAsync(async token =>
            {
                JsonObject? meta = BuildAuthMeta(token);
                RequestOptions? requestOptions = meta is not null ? new RequestOptions { Meta = meta } : null;
                CallToolResult result = await mcpClient.CallToolAsync(
                    toolName,
                    arguments,
                    progress: null,
                    options: requestOptions,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                string text = string.Join(
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Downstream call to '{ToolName}' threw an unhandled exception", toolName);
            return $"(DownstreamCallFailed) {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            callLock.Release();
        }
    }

    public async Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        McpClient mcpClient = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        return await WithAuthRetryAsync(async token =>
        {
            JsonObject? meta = BuildAuthMeta(token);
            RequestOptions? requestOptions = meta is not null ? new RequestOptions { Meta = meta } : null;
            IList<McpClientTool> tools = await mcpClient.ListToolsAsync(requestOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
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

            StdioClientTransportOptions transportOptions = CreateTransportOptions();
            var transport = new StdioClientTransport(transportOptions, loggerFactory);
            McpClientOptions clientOptions = await CreateClientOptionsAsync(cancellationToken).ConfigureAwait(false);

            // MCP 2026-07-28 uses per-request metadata, which is supplied by ListToolsAsync
            // and CallToolAsync below. InitializeMeta covers the SDK's standards-compliant
            // fallback when the peer negotiates an older initialize-based protocol revision.
            client = await McpClient.CreateAsync(
                transport,
                clientOptions,
                loggerFactory,
                cancellationToken).ConfigureAwait(false);

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

    internal static McpClientOptions CreateClientOptions(string token) =>
        new() { InitializeMeta = BuildAuthMeta(token) };

    private async Task<McpClientOptions> CreateClientOptionsAsync(CancellationToken cancellationToken)
    {
        if (!IsDownstreamAuthRequired())
        {
            return CreateClientOptions(string.Empty);
        }

        string token = await tokenProvider.GetServiceTokenAsync(cancellationToken).ConfigureAwait(false);
        McpClientOptions clientOptions = CreateClientOptions(token);
        if (clientOptions.InitializeMeta is null)
        {
            throw new McpException(
                $"{DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired}: " +
                "downstream initialization credential is missing.");
        }

        return clientOptions;
    }

    private bool IsDownstreamAuthRequired() => descriptor.AuthRequired;

    internal StdioClientTransportOptions CreateTransportOptions()
    {
        var environmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach ((string name, string? value) in descriptor.EnvironmentVariables)
        {
            environmentVariables[name] = value;
        }

        foreach (string name in descriptor.AllowedEnvironmentVariables)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
            {
                environmentVariables.TryAdd(name, value);
            }
        }

        return new StdioClientTransportOptions
        {
            Name = descriptor.Name,
            Command = descriptor.Command,
            Arguments = [.. descriptor.Arguments],
            WorkingDirectory = descriptor.WorkingDirectory,
            EnvironmentVariables = environmentVariables,
            InheritEnvironmentVariables = false,
            ShutdownTimeout = TimeSpan.FromSeconds(10),
            StandardErrorLines = line => logger.LogWarning("[downstream-server stderr] {Line}", line)
        };
    }

}
