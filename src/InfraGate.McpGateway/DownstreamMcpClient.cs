using InfraGate.Approvals;
using InfraGate.DownstreamAuth;
using InfraGate.McpGateway.DownstreamAuth;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;

namespace InfraGate.McpGateway;

internal sealed class DownstreamMcpClient : IDownstreamMcpClient, IToolCaller, IAsyncDisposable
{
    private readonly McpGatewayOptions options;
    private readonly IDownstreamServiceTokenProvider tokenProvider;
    private readonly ILogger<DownstreamMcpClient> logger;
    private readonly SemaphoreSlim clientLock = new(1, 1);
    private readonly SemaphoreSlim callLock = new(1, 1);
    private McpClient? client;

    internal DownstreamMcpClient(
        McpGatewayOptions options,
        IDownstreamServiceTokenProvider tokenProvider,
        ILogger<DownstreamMcpClient> logger)
    {
        this.options = options;
        this.tokenProvider = tokenProvider;
        this.logger = logger;
    }

    public async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var mcpClient = await GetClientAsync(cancellationToken);
        var token = await tokenProvider.GetServiceTokenAsync(cancellationToken).ConfigureAwait(false);
        await callLock.WaitAsync(cancellationToken);
        try
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
        }
        finally
        {
            callLock.Release();
        }
    }

    public async Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var mcpClient = await GetClientAsync(cancellationToken);
        var token = await tokenProvider.GetServiceTokenAsync(cancellationToken).ConfigureAwait(false);
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

            // TODO (Task 6 bootstrap gate): write "io.infragate.downstream.authorization: Bearer <token>\n"
            // to the child process stdin BEFORE McpClient.CreateAsync fires the initialize request.
            // StdioClientTransport does not expose stdin before connect; this requires a custom transport
            // or process wrapper. The per-request _meta below covers listTools and callTool. The
            // initialize gap is noted and deferred to a follow-up.
            client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

            return client;
        }
        finally
        {
            clientLock.Release();
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
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        };
    }

}
