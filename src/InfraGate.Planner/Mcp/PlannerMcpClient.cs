using InfraGate.Planner.Diagnostics;
using ModelContextProtocol.Client;

namespace InfraGate.Planner.Mcp;

internal sealed class PlannerMcpClient : IPlannerMcpClient, IAsyncDisposable
{
    private McpClient? mcpClient;
    private readonly ILogger<PlannerMcpClient> logger;
    private readonly ILoggerFactory loggerFactory;

    public PlannerMcpClient(
        IOptions<PlannerOptions> options,
        IClientCredentialsTokenProvider tokenProvider,
        ILogger<PlannerMcpClient> logger,
        ILoggerFactory loggerFactory)
    {
        this.logger = logger;
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

        var bearerLogger = loggerFactory.CreateLogger<ClientCredentialsBearerHandler>();
        var bearerHandler = new ClientCredentialsBearerHandler(TokenProvider, bearerLogger);
        var httpClient = new HttpClient(bearerHandler)
        {
            BaseAddress = new Uri(GatewayBaseUrl)
        };

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(GatewayBaseUrl),
                Name = PlannerConventions.DefaultClientId,
            },
            httpClient,
            loggerFactory,
            ownsHttpClient: true);

        mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        PlannerToolWhitelist.AssertAllowed(toolName);

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
