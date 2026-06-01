using Microsoft.Agents.AI.A2A;

namespace InfraGate.Planner.Handoff;

internal sealed class A2AExecutorDispatchClient(A2AAgent agent) : IExecutorDispatchClient
{
    public async Task<ExecutorDispatchResult> DispatchAsync(
        string contextId,
        string planId,
        CancellationToken cancellationToken)
    {
        var session = await agent.CreateSessionAsync(contextId).ConfigureAwait(false);
        var response = await agent.RunAsync(
            planId,
            session,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        string json = response.Text
            ?? throw new InvalidOperationException("Executor A2A response did not include an outcome.");

        return JsonSerializer.Deserialize<ExecutorDispatchResult>(json)
            ?? throw new InvalidOperationException("Executor A2A response could not be deserialized.");
    }
}
