namespace InfraGate.Planner.Handoff;

internal interface IExecutorDispatchClient
{
    Task<ExecutorDispatchResult> DispatchAsync(
        string contextId,
        string planId,
        CancellationToken cancellationToken);
}
