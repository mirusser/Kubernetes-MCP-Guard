namespace InfraGate.AgentGuardrails.Tests.UnitTests;

// Proves the composition Observer's and Planner's Program.cs actually build:
// AgentGuardrailPolicy sourced directly from DiagnosticCapabilityProfile.ToolNames (Observer), or
// that same set plus Planner's one explicit additional capability, ask_observer_to_inspect
// (InfraGate.Planner.Llm.AskObserverTool.FunctionName — duplicated here as a literal because this
// project intentionally does not reference InfraGate.Planner). Exercised against the real
// ToolCallGuardrailExtensions middleware, not a re-implementation of it, so a future change to
// either the profile or the middleware surfaces here.
public sealed class DiagnosticCapabilityGuardrailPolicyTests
{
    private const string AskObserverToolName = "ask_observer_to_inspect";

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

    private static async Task<bool> InvokeUnderPolicyAsync(
        string toolName, AgentGuardrailPolicy policy, string agentName)
    {
        bool executed = false;
        var testFunction = AIFunctionFactory.Create(() => { executed = true; return "ok"; }, toolName);

        var fakeChatClient = new FunctionCallChatClient(toolName);
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
        using var testMeter = new Meter($"test-diagnostic-guardrail-{Guid.NewGuid()}");
        agent = agent.AsBuilder()
            .UseToolCallGuardrail(policy, new AgentGuardrailMetrics(testMeter), agentName)
            .Build();

        await agent.RunAsync("go");
        return executed;
    }

    private static AgentGuardrailPolicy BuildObserverPolicy() =>
        new(DiagnosticCapabilityProfile.ToolNames);

    private static AgentGuardrailPolicy BuildPlannerPolicy() =>
        new(new HashSet<string>(DiagnosticCapabilityProfile.ToolNames, StringComparer.Ordinal)
        {
            AskObserverToolName,
        });

    public static IEnumerable<object[]> ProfiledToolNames() =>
        DiagnosticCapabilityProfile.ToolNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(ProfiledToolNames))]
    public async Task ObserverPolicy_EveryProfiledDiagnosticRead_IsAllowed(string toolName)
    {
        var policy = BuildObserverPolicy();

        bool executed = await InvokeUnderPolicyAsync(toolName, policy, "observer-ns1");

        Assert.True(executed);
    }

    [Theory]
    [MemberData(nameof(ProfiledToolNames))]
    public async Task PlannerPolicy_EveryProfiledDiagnosticRead_IsAllowed(string toolName)
    {
        var policy = BuildPlannerPolicy();

        bool executed = await InvokeUnderPolicyAsync(toolName, policy, "planner-abc123");

        Assert.True(executed);
    }

    [Fact]
    public async Task ObserverPolicy_UnprofiledReadOnlyTool_RemainsBlocked()
    {
        // Same "known ReadOnlyHint=true but not reviewed" adversarial name AgentMcp.Tests pins as
        // InProcessMcpServerFixture.UnprofiledReadOnlyToolName.
        var policy = BuildObserverPolicy();

        bool executed = await InvokeUnderPolicyAsync("get_k8s_pods", policy, "observer-ns1");

        Assert.False(executed);
    }

    [Fact]
    public async Task PlannerPolicy_UnprofiledReadOnlyTool_RemainsBlocked()
    {
        var policy = BuildPlannerPolicy();

        bool executed = await InvokeUnderPolicyAsync("get_k8s_pods", policy, "planner-abc123");

        Assert.False(executed);
    }

    [Fact]
    public async Task ObserverPolicy_ProposePlan_RemainsBlocked()
    {
        var policy = BuildObserverPolicy();

        bool executed = await InvokeUnderPolicyAsync("propose_plan", policy, "observer-ns1");

        Assert.False(executed);
    }

    [Fact]
    public async Task PlannerPolicy_ProposePlan_RemainsBlocked()
    {
        // propose_plan is invoked deterministically by application code, never offered to the LLM's
        // tool list — but if it ever were, the guardrail must still reject it.
        var policy = BuildPlannerPolicy();

        bool executed = await InvokeUnderPolicyAsync("propose_plan", policy, "planner-abc123");

        Assert.False(executed);
    }

    [Fact]
    public async Task PlannerPolicy_AskObserverTool_IsAllowed()
    {
        var policy = BuildPlannerPolicy();

        bool executed = await InvokeUnderPolicyAsync(AskObserverToolName, policy, "planner-abc123");

        Assert.True(executed);
    }

    [Fact]
    public async Task ObserverPolicy_AskObserverTool_RemainsBlocked()
    {
        // ask_observer_to_inspect is Planner's explicit additional capability, not a diagnostic
        // read: Observer must never carry it.
        var policy = BuildObserverPolicy();

        bool executed = await InvokeUnderPolicyAsync(AskObserverToolName, policy, "observer-ns1");

        Assert.False(executed);
    }
}
