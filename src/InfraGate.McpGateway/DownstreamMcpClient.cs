using InfraGate.Approvals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

public sealed class DownstreamMcpClient : IDownstreamMcpClient, IToolCaller, IAsyncDisposable
{
    private readonly McpGatewayOptions options;
    private readonly ILogger<DownstreamMcpClient> logger;
    private readonly SemaphoreSlim clientLock = new(1, 1);
    private readonly SemaphoreSlim callLock = new(1, 1);
    private McpClient? client;

    public DownstreamMcpClient(McpGatewayOptions options, ILogger<DownstreamMcpClient> logger)
    {
        this.options = options;
        this.logger = logger;
    }

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
        }
        finally
        {
            callLock.Release();
        }
    }

    public async Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var mcpClient = await GetClientAsync(cancellationToken);
        var tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
        return tools
            .Select(t => new DownstreamTool(
                t.Name,
                t.Description ?? string.Empty,
                t.ProtocolTool.Annotations?.ReadOnlyHint ?? false,
                t.ProtocolTool.Annotations?.DestructiveHint ?? false,
                t.JsonSchema))
            .ToList();
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

        var environmentVariables = new Dictionary<string, string?>();
        foreach (string? key in Environment.GetEnvironmentVariables().Keys)
        {
            if (key is null)
            {
                continue;
            }

            string? value = Environment.GetEnvironmentVariable(key);
            if (value is not null)
            {
                environmentVariables[key] = value;
            }
        }

        return new StdioClientTransportOptions
        {
            Name = McpGatewayConventions.DownstreamProcess.Name,
            Command = McpGatewayConventions.DownstreamProcess.Command,
            Arguments = arguments,
            WorkingDirectory = options.WorkingDirectory,
            EnvironmentVariables = environmentVariables,
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        };
    }

}
