using ModelContextProtocol.Client;

namespace InfraGate.Executor.Mcp;

internal sealed class ExecutorMcpClient : IExecutorMcpClient, IAsyncDisposable
{
    private McpClient? mcpClient;
    private readonly ILoggerFactory loggerFactory;

    public ExecutorMcpClient(
        IOptions<ExecutorOptions> options,
        IClientCredentialsTokenProvider tokenProvider,
        ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;
        GatewayBaseUrl = options.Value.GatewayBaseUrl;
        TokenProvider = tokenProvider;
    }

    public string GatewayBaseUrl { get; }

    public IClientCredentialsTokenProvider TokenProvider { get; }

    public bool IsConnected => mcpClient is not null;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (mcpClient is not null)
        {
            return;
        }

        var httpClient = CreateHttpClient(GatewayBaseUrl, TokenProvider, loggerFactory);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(GatewayBaseUrl),
                Name = ExecutorConventions.DefaultClientId,
            },
            httpClient,
            loggerFactory,
            ownsHttpClient: true);

        mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    internal static HttpClient CreateHttpClient(
        string gatewayBaseUrl,
        IClientCredentialsTokenProvider tokenProvider,
        ILoggerFactory loggerFactory,
        HttpMessageHandler? innerHandler = null)
    {
        var bearerLogger = loggerFactory.CreateLogger<ClientCredentialsBearerHandler>();
        var bearerHandler = new ClientCredentialsBearerHandler(tokenProvider, bearerLogger)
        {
            InnerHandler = innerHandler ?? new SocketsHttpHandler()
        };

        return new HttpClient(bearerHandler)
        {
            BaseAddress = new Uri(gatewayBaseUrl)
        };
    }

    public async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        ExecutorToolWhitelist.AssertAllowed(toolName);

        if (mcpClient is null)
        {
            throw new InvalidOperationException("MCP client is not connected. Call ConnectAsync first.");
        }

        var result = await mcpClient.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return JsonSerializer.Serialize(result);
    }

    public async ValueTask DisposeAsync()
    {
        if (mcpClient is not null)
        {
            await mcpClient.DisposeAsync().ConfigureAwait(false);
        }
    }
}
