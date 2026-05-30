using Microsoft.Agents.AI;

namespace InfraGate.AgentLlm;

/// <summary>
/// Builds a <see cref="ChatClientAgent"/> with native function invocation, a per-call iterations cap,
/// and a counter that tracks how many tool calls the agent makes across a single run.
/// </summary>
public sealed class ToolCallingAgentFactory(IChatClientFactory chatClientFactory)
{
    /// <summary>
    /// Creates a <see cref="ChatClientAgent"/> whose underlying <see cref="IChatClient"/> pipeline
    /// includes <see cref="FunctionInvokingChatClient"/> capped at <paramref name="maxToolIterations"/>
    /// iterations per request. Each tool in <paramref name="tools"/> is wrapped with a counting
    /// decorator so that every actual invocation increments the returned counter.
    /// </summary>
    public (AIAgent Agent, Func<int> GetToolCallCount) Create(
        string name,
        string instructions,
        IReadOnlyList<AITool> tools,
        int maxToolIterations,
        ChatResponseFormat? responseFormat = null)
    {
        var count = 0;

        var countedTools = tools
            .Select<AITool, AITool>(t => t is AIFunction f
                ? new CountingAiFunction(f, () => Interlocked.Increment(ref count))
                : t)
            .ToList();

        var chatClient = chatClientFactory.Create()
            .AsBuilder()
            .UseFunctionInvocation(configure: c => c.MaximumIterationsPerRequest = maxToolIterations)
            .Build();

        var agentOptions = new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = countedTools,
                ResponseFormat = responseFormat,
            },
        };

        AIAgent agent = new ChatClientAgent(chatClient, agentOptions);
        agent = agent.AsBuilder().UseOpenTelemetry().Build();

        return (agent, () => Volatile.Read(ref count));
    }

    private sealed class CountingAiFunction(AIFunction inner, Action onInvoked)
        : DelegatingAIFunction(inner)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            onInvoked();
            return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
    }
}
