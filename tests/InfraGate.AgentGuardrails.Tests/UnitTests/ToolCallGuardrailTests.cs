namespace InfraGate.AgentGuardrails.Tests.UnitTests;

public sealed class ToolCallGuardrailTests
{
    // A fake IChatClient that returns one FunctionCallContent on the first call,
    // then a plain text response on all subsequent calls so the iteration loop terminates.
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

    private static (AIAgent Agent, List<Measurement<long>> Blocked) BuildAgent(
        string agentName,
        string functionToCall,
        AIFunction testFunction,
        AgentGuardrailPolicy policy,
        Meter testMeter)
    {
        var metrics = new AgentGuardrailMetrics(testMeter);

        var blocked = new List<Measurement<long>>();
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == AgentGuardrailConventions.ToolCallBlockedCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            blocked.Add(new Measurement<long>(value, tags)));
        listener.Start();

        var fakeChatClient = new FunctionCallChatClient(functionToCall);
        var chatClient = fakeChatClient
            .AsBuilder()
            .UseFunctionInvocation(configure: c => c.MaximumIterationsPerRequest = 2)
            .Build();

        var agentOptions = new ChatClientAgentOptions
        {
            Name = agentName,
            ChatOptions = new ChatOptions { Tools = [testFunction] },
        };

        AIAgent agent = new ChatClientAgent(chatClient, agentOptions);
        agent = agent.AsBuilder().UseToolCallGuardrail(policy, metrics, agentName).Build();

        return (agent, blocked);
    }

    [Fact]
    public async Task UseToolCallGuardrail_AllowedTool_ExecutesFunctionAndRecordsNoBlockMetric()
    {
        bool executed = false;
        var testFunction = AIFunctionFactory.Create(
            () => { executed = true; return "ok"; }, "get_k8s_status");

        using var testMeter = new Meter("test-guardrail-allowed");
        var policy = new AgentGuardrailPolicy(new HashSet<string>(StringComparer.Ordinal) { "get_k8s_status" });

        var (agent, blocked) = BuildAgent("observer-ns1", "get_k8s_status", testFunction, policy, testMeter);

        await agent.RunAsync("inspect");

        Assert.True(executed);
        Assert.Empty(blocked);
    }

    [Fact]
    public async Task UseToolCallGuardrail_DisallowedTool_BlocksFunctionAndRecordsMetric()
    {
        bool executed = false;
        var testFunction = AIFunctionFactory.Create(
            () => { executed = true; return "ok"; }, "propose_plan");

        using var testMeter = new Meter("test-guardrail-disallowed");
        var policy = new AgentGuardrailPolicy(new HashSet<string>(StringComparer.Ordinal) { "get_k8s_status" });

        var (agent, blocked) = BuildAgent("planner-abc123", "propose_plan", testFunction, policy, testMeter);

        await agent.RunAsync("plan");

        Assert.False(executed);
        Assert.Single(blocked);
        Assert.Equal(1L, blocked[0].Value);

        var tags = blocked[0].Tags.ToArray();
        Assert.Equal("planner-abc123", tags.First(t => t.Key == AgentGuardrailConventions.Tags.AgentName).Value);
        Assert.Equal("propose_plan", tags.First(t => t.Key == AgentGuardrailConventions.Tags.ToolName).Value);
        Assert.Equal(AgentGuardrailConventions.Reasons.ToolNotAllowed, tags.First(t => t.Key == AgentGuardrailConventions.Tags.GuardrailReason).Value);
    }

    [Fact]
    public void UseToolCallGuardrail_Always_ReturnsBuilderForChaining()
    {
        var fakeChatClient = new FunctionCallChatClient("test");
        var chatClient = fakeChatClient
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        AIAgent agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { Name = "chain-test" });

        using var testMeter = new Meter("test-guardrail-chain");
        var metrics = new AgentGuardrailMetrics(testMeter);
        var policy = new AgentGuardrailPolicy(new HashSet<string>(StringComparer.Ordinal) { "allowed" });

        var builder = agent.AsBuilder().UseToolCallGuardrail(policy, metrics, "chain-test");

        Assert.NotNull(builder);
        var builtAgent = builder.Build();
        Assert.NotNull(builtAgent);
    }
}
