using System.Diagnostics;
using Microsoft.Agents.AI;

namespace InfraGate.AgentLlm.Tests.UnitTests;

public sealed class ToolCallingAgentFactoryOtelTests
{
    private sealed class CapturingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeChatClientFactory(IChatClient client) : IChatClientFactory
    {
        public IChatClient Create() => client;
    }

    [Fact]
    public void Create_ReturnsAIAgent_NotChatClientAgent()
    {
        var factory = new ToolCallingAgentFactory(new FakeChatClientFactory(new CapturingChatClient()));

        var (agent, _) = factory.Create("test", "instructions", [], 4);

        Assert.IsAssignableFrom<AIAgent>(agent);
        Assert.IsNotType<ChatClientAgent>(agent);
    }

    [Fact]
    public async Task Create_WithActivityListener_EmitsInvokeAgentSpan()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name is
                "Experimental.Microsoft.Agents.AI" or
                "Experimental.Microsoft.Extensions.AI",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = captured.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var factory = new ToolCallingAgentFactory(new FakeChatClientFactory(new CapturingChatClient()));
        var (agent, _) = factory.Create("test", "instructions", [], 4);

        await agent.RunAsync("hello");

        Assert.NotEmpty(captured);
    }
}
