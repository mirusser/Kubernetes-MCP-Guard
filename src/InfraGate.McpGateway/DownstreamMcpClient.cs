using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

public sealed class DownstreamMcpClient(McpGatewayOptions options) : IDownstreamMcpClient, IAsyncDisposable
{
    private readonly SemaphoreSlim clientLock = new(1, 1);
    private readonly SemaphoreSlim callLock = new(1, 1);
    private McpClient? client;

    public async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var mcpClient = await GetClientAsync(cancellationToken);
        await callLock.WaitAsync(cancellationToken);
        try
        {
            var result = await mcpClient.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

            return string.Join(
                Environment.NewLine,
                result.Content.OfType<TextContentBlock>().Select(content => content.Text));
        }
        finally
        {
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

            var transport = new StdioClientTransport(CreateTransportOptions());

            client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

            return client;
        }
        finally
        {
            clientLock.Release();
        }
    }

    internal StdioClientTransportOptions CreateTransportOptions()
    {
        string[] arguments = string.IsNullOrWhiteSpace(options.DownstreamAssembly)
            ? [
                McpGatewayConventions.DownstreamProcess.RunArgument,
                McpGatewayConventions.DownstreamProcess.ProjectArgument,
                options.DownstreamProject
            ]
            : [options.DownstreamAssembly];

        return new StdioClientTransportOptions
        {
            Name = McpGatewayConventions.DownstreamProcess.Name,
            Command = McpGatewayConventions.DownstreamProcess.Command,
            Arguments = arguments,
            WorkingDirectory = options.WorkingDirectory,
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        };
    }

}
