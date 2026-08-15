using InfraGate.McpGateway;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway.Tests.Fakes;

/// <summary>
/// Shared fake downstream MCP client for testing. Returns simple text responses.
/// </summary>
internal sealed class FakeDownstreamMcpClient : IDownstreamMcpClient
{
    private readonly Func<string, IReadOnlyDictionary<string, object?>, string>? responseProvider;

    public FakeDownstreamMcpClient(string fixedResponse = "fake response")
    {
        responseProvider = (_, _) => fixedResponse;
    }

    public FakeDownstreamMcpClient(Func<string, IReadOnlyDictionary<string, object?>, string> responseProvider)
    {
        this.responseProvider = responseProvider;
    }

    public Task<DownstreamCallResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        if (responseProvider is null)
        {
            return Task.FromResult(new DownstreamCallResult(
                [new TextContentBlock { Text = "fake response" }],
                IsError: false,
                Meta: null));
        }

        string text = responseProvider(toolName, arguments);
        return Task.FromResult(new DownstreamCallResult(
            [new TextContentBlock { Text = text }],
            IsError: false,
            Meta: null));
    }

    public Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<DownstreamTool>>([]);
    }
}
