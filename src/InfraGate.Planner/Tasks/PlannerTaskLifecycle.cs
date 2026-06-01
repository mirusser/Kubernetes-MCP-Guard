using A2A;
using InfraGate.Remediation.Contracts;

namespace InfraGate.Planner.Tasks;

internal sealed class PlannerTaskLifecycle(
    IPlannerTaskStore taskStore,
    ChannelEventNotifier notifier)
{
    public Task StartWorkAsync(
        string taskId,
        string contextId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            taskId,
            contextId,
            static (updater, ct) => updater.StartWorkAsync(
                CreateStatusMessage(PlannerTaskStoreConventions.DomainStates.Planning),
                cancellationToken: ct),
            cancellationToken);

    public Task AddPlanArtifactAsync(
        string taskId,
        string contextId,
        string planId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            taskId,
            contextId,
            (updater, ct) => updater.AddArtifactAsync(
                [new Part { Text = planId }],
                artifactId: PlannerTaskStoreConventions.Artifacts.PlanReferenceId,
                name: PlannerTaskStoreConventions.Artifacts.PlanReferenceName,
                cancellationToken: ct),
            cancellationToken);

    public Task CompleteNoActionAsync(
        string taskId,
        string contextId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            taskId,
            contextId,
            static (updater, ct) => updater.CompleteAsync(
                CreateStatusMessage(PlannerTaskStoreConventions.DomainStates.Unremediable),
                cancellationToken: ct),
            cancellationToken);

    public Task RequireApprovalAsync(
        string taskId,
        string contextId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            taskId,
            contextId,
            static (updater, ct) => updater.RequireAuthAsync(
                CreateStatusMessage(PlannerTaskStoreConventions.DomainStates.Waiting),
                cancellationToken: ct),
            cancellationToken);

    public Task FailAsync(
        string taskId,
        string contextId,
        string reason,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            taskId,
            contextId,
            (updater, ct) => updater.FailAsync(
                CreateStatusMessage(reason),
                cancellationToken: ct),
            cancellationToken);

    public Task CompleteAsync(
        string taskId,
        string contextId,
        string outcome,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            taskId,
            contextId,
            (updater, ct) => updater.CompleteAsync(
                CreateStatusMessage(outcome),
                cancellationToken: ct),
            cancellationToken);

    public Task RejectAsync(
        string taskId,
        string contextId,
        string reason,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            taskId,
            contextId,
            (updater, ct) => updater.RejectAsync(
                CreateStatusMessage(reason),
                cancellationToken: ct),
            cancellationToken);

    public Task ApplyExecutorOutcomeAsync(
        string taskId,
        string contextId,
        ExecutorDispatchResult outcome,
        CancellationToken cancellationToken = default) =>
        outcome.Status switch
        {
            ExecutorDispatchStatuses.Applied => CompleteAsync(
                taskId, contextId, outcome.Detail, cancellationToken),
            ExecutorDispatchStatuses.Rejected => RejectAsync(
                taskId, contextId, outcome.Detail, cancellationToken),
            ExecutorDispatchStatuses.Failed => FailAsync(
                taskId, contextId, outcome.Detail, cancellationToken),
            _ => FailAsync(
                taskId,
                contextId,
                $"Executor returned unsupported status '{outcome.Status}'.",
                cancellationToken),
        };

    private async Task ApplyAsync(
        string taskId,
        string contextId,
        Func<TaskUpdater, CancellationToken, ValueTask> emitAsync,
        CancellationToken cancellationToken)
    {
        var eventQueue = new AgentEventQueue();
        var updater = new TaskUpdater(eventQueue, taskId, contextId);

        await emitAsync(updater, cancellationToken).ConfigureAwait(false);
        eventQueue.Complete();

        await foreach (var streamEvent in eventQueue.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            using (await notifier.AcquireTaskLockAsync(taskId, cancellationToken).ConfigureAwait(false))
            {
                var current = await taskStore.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"A2A task '{taskId}' was not found.");
                var updated = TaskProjection.Apply(current, streamEvent)
                    ?? throw new InvalidOperationException($"A2A task '{taskId}' could not apply its lifecycle event.");

                await taskStore.SaveTaskAsync(taskId, updated, cancellationToken).ConfigureAwait(false);
                notifier.Notify(taskId, streamEvent);
            }
        }
    }

    private static Message CreateStatusMessage(string domainState) =>
        new()
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = Role.Agent,
            Parts = [new Part { Text = domainState }],
        };
}
