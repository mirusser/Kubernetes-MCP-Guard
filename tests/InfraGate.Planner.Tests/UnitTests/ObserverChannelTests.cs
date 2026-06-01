using System.Text.Json;
using InfraGate.Observer.Contracts;
using InfraGate.Planner.Handoff;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class ObserverChannelTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static ObserverChannel CreateChannel(FakeA2AAgent agent) =>
        new(agent, NullLogger<ObserverChannel>.Instance);

    // ── Tool request delivery ─────────────────────────────────────────

    [Fact]
    public async Task SendToolRequestAsync_DeliversToolRequestEnvelope()
    {
        var responsePayload = new ToolResponsePayload { IsError = false, ResultJson = "pod events text" };
        var agent = new FakeA2AAgent(respondWith: JsonSerializer.Serialize(responsePayload));
        var channel = CreateChannel(agent);

        var result = await channel.SendToolRequestAsync("cycle-1", "get_k8s_events", null);

        Assert.True(agent.WasInvoked);
        var envelope = JsonSerializer.Deserialize<ObserverInboundEnvelope>(agent.LastMessage!);
        Assert.NotNull(envelope);
        Assert.Equal(ObserverInboundIntents.ToolRequest, envelope.Intent);
        Assert.Equal("cycle-1", envelope.CycleId);
        Assert.Equal("get_k8s_events", envelope.ToolRequest?.ToolName);
    }

    [Fact]
    public async Task SendToolRequestAsync_DeserializesResult()
    {
        var responsePayload = new ToolResponsePayload { IsError = false, ResultJson = "the k8s output" };
        var agent = new FakeA2AAgent(respondWith: JsonSerializer.Serialize(responsePayload));
        var channel = CreateChannel(agent);

        var result = await channel.SendToolRequestAsync("cycle-1", "get_k8s_events", null);

        Assert.False(result.IsError);
        Assert.Equal("the k8s output", result.ResultJson);
    }

    [Fact]
    public async Task SendToolRequestAsync_AgentThrows_ReturnsIsError()
    {
        var agent = new FakeA2AAgent(shouldThrow: true);
        var channel = CreateChannel(agent);

        var result = await channel.SendToolRequestAsync("cycle-1", "get_k8s_events", null);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task SendToolRequestAsync_AgentThrows_DoesNotRethrow()
    {
        var agent = new FakeA2AAgent(shouldThrow: true);
        var channel = CreateChannel(agent);

        var ex = await Record.ExceptionAsync(() =>
            channel.SendToolRequestAsync("cycle-1", "get_k8s_events", null));

        Assert.Null(ex);
    }

    // ── Fake agent ────────────────────────────────────────────────────

    internal sealed class FakeA2AAgent(bool shouldThrow = false, string? respondWith = null) : AIAgent
    {
        public bool WasInvoked { get; private set; }
        public string? LastMessage { get; private set; }

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => new(new FakeSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
            => new(default(JsonElement));

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
            => new(new FakeSession());

        protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            LastMessage = messages.FirstOrDefault()?.Text;
            if (shouldThrow)
                throw new InvalidOperationException("Simulated observer channel failure");
            if (respondWith is not null)
                return Task.FromResult(new AgentResponse([new ChatMessage(ChatRole.Assistant, respondWith)]));
            return Task.FromResult(new AgentResponse([]));
        }

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Streaming not used by channel");

        private sealed class FakeSession : AgentSession { }
    }
}
