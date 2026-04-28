using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.McpGateway;

public sealed class DownstreamMcpClient(McpGatewayOptions options) : IDownstreamMcpClient, IAsyncDisposable
{
    private readonly SemaphoreSlim clientLock = new(1, 1);
    private readonly SemaphoreSlim callLock = new(1, 1);
    private McpClient? client;
    private McpServer? activeUpstreamServer;

    public async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken,
        McpServer? upstreamServer = null)
    {
        var mcpClient = await GetClientAsync(cancellationToken);
        await callLock.WaitAsync(cancellationToken);
        try
        {
            activeUpstreamServer = upstreamServer;
            var result = await mcpClient.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

            return string.Join(
                Environment.NewLine,
                result.Content.OfType<TextContentBlock>().Select(content => content.Text));
        }
        finally
        {
            activeUpstreamServer = null;
            callLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync();
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

        await clientLock.WaitAsync(cancellationToken);
        try
        {
            if (client is not null)
            {
                return client;
            }

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "infra-gate-downstream",
                Command = "dotnet",
                Arguments = ["run", "--project", options.DownstreamProject],
                WorkingDirectory = options.WorkingDirectory,
                ShutdownTimeout = TimeSpan.FromSeconds(10)
            });

            client = await McpClient.CreateAsync(
                transport,
                new McpClientOptions
                {
                    Capabilities = new ClientCapabilities
                    {
                        Elicitation = new ElicitationCapability
                        {
                            Form = new FormElicitationCapability()
                        }
                    },
                    Handlers = new McpClientHandlers
                    {
                        ElicitationHandler = HandleElicitationAsync
                    }
                },
                cancellationToken: cancellationToken);

            return client;
        }
        finally
        {
            clientLock.Release();
        }
    }

    private async ValueTask<ElicitResult> HandleElicitationAsync(
        ElicitRequestParams? requestParams,
        CancellationToken cancellationToken)
    {
        if (activeUpstreamServer is null || requestParams is null)
        {
            return new ElicitResult { Action = "decline" };
        }

        try
        {
            return await activeUpstreamServer.ElicitAsync(requestParams, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ModelContextProtocol.McpException)
        {
            return new ElicitResult { Action = "decline" };
        }
    }
}
