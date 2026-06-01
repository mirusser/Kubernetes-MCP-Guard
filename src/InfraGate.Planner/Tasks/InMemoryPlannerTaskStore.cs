using A2A;

namespace InfraGate.Planner.Tasks;

internal sealed class InMemoryPlannerTaskStore : IPlannerTaskStore
{
    private readonly InMemoryTaskStore taskStore = new();
    private readonly ConcurrentDictionary<string, string> taskIdsByContext = new(StringComparer.Ordinal);

    public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
        taskStore.GetTaskAsync(taskId, cancellationToken);

    public async Task SaveTaskAsync(
        string taskId,
        AgentTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(task);

        if (taskIdsByContext.TryGetValue(task.ContextId, out string? existingTaskId)
            && !string.Equals(existingTaskId, taskId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Context '{task.ContextId}' is already claimed by task '{existingTaskId}'.");
        }

        bool claimAdded = taskIdsByContext.TryAdd(task.ContextId, taskId);
        try
        {
            await taskStore.SaveTaskAsync(taskId, task, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (claimAdded)
            {
                RemoveClaimIfOwned(task.ContextId, taskId);
            }

            throw;
        }
    }

    public async Task<bool> TryCreateTaskAsync(
        string taskId,
        AgentTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(task);

        if (!taskIdsByContext.TryAdd(task.ContextId, taskId))
        {
            return false;
        }

        try
        {
            await taskStore.SaveTaskAsync(taskId, task, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            RemoveClaimIfOwned(task.ContextId, taskId);
            throw;
        }
    }

    public async Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var task = await taskStore.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        await taskStore.DeleteTaskAsync(taskId, cancellationToken).ConfigureAwait(false);

        if (task is not null)
        {
            RemoveClaimIfOwned(task.ContextId, taskId);
        }
    }

    public Task<ListTasksResponse> ListTasksAsync(
        ListTasksRequest request,
        CancellationToken cancellationToken = default) =>
        taskStore.ListTasksAsync(request, cancellationToken);

    private void RemoveClaimIfOwned(string contextId, string taskId)
    {
        if (taskIdsByContext.TryGetValue(contextId, out string? existingTaskId)
            && string.Equals(existingTaskId, taskId, StringComparison.Ordinal))
        {
            taskIdsByContext.TryRemove(contextId, out _);
        }
    }
}
