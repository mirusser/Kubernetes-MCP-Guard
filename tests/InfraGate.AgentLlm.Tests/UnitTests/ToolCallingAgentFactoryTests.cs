using System.Diagnostics.Metrics;
using InfraGate.AgentGuardrails;
using Microsoft.Agents.AI;

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

    private static ToolCallingAgentFactory CreateFactory(IChatClient client, AgentGuardrailMetrics? guardrailMetrics = null) =>
        new(new FakeChatClientFactory(client), guardrailMetrics);

    private sealed class FunctionCallChatClient(string functionToCall) : IChatClient
    {
        private int callCount;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                AIContent callContent = new FunctionCallContent(
                    "call-1", functionToCall, new Dictionary<string, object?>(StringComparer.Ordinal));
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [callContent])));
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

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

    [Fact]
    public async Task Create_WithGuardrailPolicy_BlocksToolAndExcludesFromCount()
    {
        var client = new FunctionCallChatClient("bad_tool");
        using var testMeter = new Meter("test-guardrail-factory");
        var metrics = new AgentGuardrailMetrics(testMeter);
        var sut = CreateFactory(client, metrics);

        var policy = new AgentGuardrailPolicy(new HashSet<string>(StringComparer.Ordinal) { "good_tool" });
        bool executed = false;
        var badTool = AIFunctionFactory.Create(() => { executed = true; return "ok"; }, "bad_tool");

        var (agent, getCount) = sut.Create("agent-guardrail", "instruct", [badTool], 4, null, policy);

        await agent.RunAsync("hello");

        Assert.False(executed);
        Assert.Equal(0, getCount()); // Blocked tools don't invoke the CountingAiFunction
    }

    [Fact]
    public async Task Create_WithGuardrailPolicy_AllowsToolAndIncludesInCount()
    {
        var client = new FunctionCallChatClient("good_tool");
        using var testMeter = new Meter("test-guardrail-factory-allowed");
        var metrics = new AgentGuardrailMetrics(testMeter);
        var sut = CreateFactory(client, metrics);

        var policy = new AgentGuardrailPolicy(new HashSet<string>(StringComparer.Ordinal) { "good_tool" });
        bool executed = false;
        var goodTool = AIFunctionFactory.Create(() => { executed = true; return "ok"; }, "good_tool");

        var (agent, getCount) = sut.Create("agent-guardrail", "instruct", [goodTool], 4, null, policy);

        await agent.RunAsync("hello");

        Assert.True(executed);
        Assert.Equal(1, getCount()); // Allowed tools invoke the CountingAiFunction
    }
}
