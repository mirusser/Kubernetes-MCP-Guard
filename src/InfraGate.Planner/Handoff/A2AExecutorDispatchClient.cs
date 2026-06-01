using InfraGate.Planner.Diagnostics;
using Microsoft.Agents.AI.A2A;

namespace InfraGate.Planner.Handoff;

internal sealed class A2AExecutorDispatchClient(
    A2AAgent agent,
    ILogger<A2AExecutorDispatchClient> logger) : IExecutorDispatchClient
{
    public async Task<ExecutorDispatchResult> DispatchAsync(
        string contextId,
        string planId,
        CancellationToken cancellationToken)
    {
        PlannerLogEvents.LogExecutorDispatchSent(logger, contextId, planId);

        try
        {
            var session = await agent.CreateSessionAsync(contextId).ConfigureAwait(false);
            var response = await agent.RunAsync(
                planId,
                session,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            string json = response.Text
                ?? throw new InvalidOperationException("Executor A2A response did not include an outcome.");

            var result = JsonSerializer.Deserialize<ExecutorDispatchResult>(json)
                ?? throw new InvalidOperationException("Executor A2A response could not be deserialized.");

            PlannerLogEvents.LogExecutorDispatchResult(logger, contextId, planId, result.Status);
            return result;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            PlannerLogEvents.LogExecutorDispatchFailed(logger, contextId, planId, ex);
            throw;
        }
    }
}
