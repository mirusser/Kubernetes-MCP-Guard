using InfraGate.ClientCredentials;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace InfraGate.Observer.Mcp;

internal sealed class ObserverMcpClient : IObserverMcpClient, IAsyncDisposable
{
    private McpClient? mcpClient;
    private readonly ILogger<ObserverMcpClient> logger;
    private readonly ILoggerFactory loggerFactory;

    public ObserverMcpClient(
        IOptions<ObserverOptions> options,
        IClientCredentialsTokenProvider tokenProvider,
        ILogger<ObserverMcpClient> logger,
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
                Name = "infra-gate-observer",
            },
            httpClient,
            loggerFactory,
            ownsHttpClient: true);

        mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Connected to MCP gateway at {GatewayBaseUrl}", GatewayBaseUrl);
    }

    public async Task<string> GetToolResultAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
    {
        ToolWhitelist.AssertAllowed(toolName);

        if (mcpClient is null)
        {
            throw new InvalidOperationException("MCP client is not connected. Call ConnectAsync first.");
        }

        var result = await mcpClient.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return System.Text.Json.JsonSerializer.Serialize(result);
    }

    public async ValueTask DisposeAsync()
    {
        if (mcpClient is not null)
        {
            await mcpClient.DisposeAsync().ConfigureAwait(false);
        }
    }
}
