using A2A;

namespace InfraGate.Planner.Tasks;

internal interface IPlannerTaskStore : ITaskStore
{
    Task<bool> TryCreateTaskAsync(
        string taskId,
        AgentTask task,
        CancellationToken cancellationToken = default);
}
