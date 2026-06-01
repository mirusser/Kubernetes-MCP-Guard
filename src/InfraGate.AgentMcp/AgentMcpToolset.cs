using InfraGate.ClientCredentials;

namespace InfraGate.AgentMcp;

internal sealed class AgentMcpToolset(
    AgentMcpOptions options,
    IClientCredentialsTokenProvider tokenProvider,
    ILoggerFactory loggerFactory) : IAgentMcpToolset
{
    private McpClient? mcpClient;

    public string GatewayBaseUrl => options.GatewayBaseUrl;

    public bool IsConnected => mcpClient is not null;

    // For tests: inject an already-connected McpClient directly without going through ConnectAsync.
    internal static AgentMcpToolset CreateFromClient(McpClient client, AgentMcpOptions opts)
    {
        var toolset = new AgentMcpToolset(opts, null!, null!);
        toolset.mcpClient = client;
        return toolset;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (mcpClient is not null)
        {
            return;
        }

        var bearerLogger = loggerFactory.CreateLogger<ClientCredentialsBearerHandler>();
        var bearerHandler = new ClientCredentialsBearerHandler(tokenProvider, bearerLogger)
        {
            InnerHandler = new SocketsHttpHandler()
        };
        var httpClient = new HttpClient(bearerHandler)
        {
            BaseAddress = new Uri(GatewayBaseUrl)
        };

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(GatewayBaseUrl),
                Name = options.ClientName,
            },
            httpClient,
            loggerFactory,
            ownsHttpClient: true);

        mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken cancellationToken)
    {
        EnsureConnected();
        try
        {
            return await ListToolsFilteredAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (IsSessionDead(ex, cancellationToken))
        {
            await ReconnectAsync(cancellationToken).ConfigureAwait(false);
            return await ListToolsFilteredAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<CallToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        try
        {
            return await mcpClient!.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (IsSessionDead(ex, cancellationToken))
        {
            await ReconnectAsync(cancellationToken).ConfigureAwait(false);
            return await mcpClient!.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private void EnsureConnected()
    {
        if (mcpClient is null)
            throw new InvalidOperationException("MCP toolset is not connected. Call ConnectAsync first.");
    }

    private async Task<IReadOnlyList<AITool>> ListToolsFilteredAsync(CancellationToken cancellationToken)
    {
        var tools = await mcpClient!.ListToolsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return tools
            .Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint == true)
            .Cast<AITool>()
            .ToList();
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        if (mcpClient is not null)
        {
            try { await mcpClient.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
            mcpClient = null;
        }

        await ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    // True when the OperationCanceledException came from the MCP session's own CTS,
    // not from the caller — meaning the session is dead and needs to be rebuilt.
    private static bool IsSessionDead(OperationCanceledException ex, CancellationToken callerToken)
        => ex.CancellationToken != callerToken && !callerToken.IsCancellationRequested;

    public async ValueTask DisposeAsync()
    {
        if (mcpClient is not null)
        {
            await mcpClient.DisposeAsync().ConfigureAwait(false);
        }
    }
}
