namespace InfraGate.AgentLlm.Tests.UnitTests;

public sealed class ToolCallingAgentFactoryTests
{
    private sealed class CapturingChatClient : IChatClient
    {
        public ChatOptions? CapturedOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CapturedOptions = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

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

    private static ToolCallingAgentFactory CreateFactory(CapturingChatClient client) =>
        new(new FakeChatClientFactory(client));

    [Fact]
    public async Task Create_NullResponseFormat_AgentRunsWithoutResponseFormat()
    {
        var client = new CapturingChatClient();
        var sut = CreateFactory(client);
        var (agent, _) = sut.Create("test", "instructions", Array.Empty<AITool>(), 4);

        await agent.RunAsync("hello");

        Assert.Null(client.CapturedOptions?.ResponseFormat);
    }

    [Fact]
    public async Task Create_WithJsonResponseFormat_PropagatesResponseFormat()
    {
        var client = new CapturingChatClient();
        var sut = CreateFactory(client);
        var format = ChatResponseFormat.Json;
        var (agent, _) = sut.Create("test", "instructions", Array.Empty<AITool>(), 4, format);

        await agent.RunAsync("hello");

        Assert.Equal(format, client.CapturedOptions?.ResponseFormat);
    }

    [Fact]
    public void Create_ExistingCallSites_UnchangedWithoutResponseFormat()
    {
        var client = new CapturingChatClient();
        var sut = CreateFactory(client);

        var (agent, getCount) = sut.Create("agent-1", "do things", Array.Empty<AITool>(), 8);

        Assert.NotNull(agent);
        Assert.Equal(0, getCount());
    }
}
