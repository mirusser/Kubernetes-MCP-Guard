using InfraGate.AgentMcp;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class TestAgentMcpToolset : IAgentMcpToolset
{
    public string GatewayBaseUrl => "http://localhost:test";
    public bool IsConnected { get; private set; }

    public int CallCount { get; private set; }
    public Dictionary<string, int> CallCountByTool { get; } = new(StringComparer.Ordinal);
    public bool WasCancelled { get; private set; }

    public IReadOnlyList<AITool> ToolsToReturn { get; set; } = [];
    public Func<string, Task<CallToolResult>>? CallToolHandler { get; set; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolsToReturn);
    }

    public Task<CallToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            WasCancelled = true;
        }

        CallCount++;
        CallCountByTool[toolName] = CallCountByTool.TryGetValue(toolName, out int count) ? count + 1 : 1;

        if (CallToolHandler is not null)
        {
            return CallToolHandler(toolName);
        }

        return Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = "{}" }] });
    }

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
