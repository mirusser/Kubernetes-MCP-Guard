using A2A;
using InfraGate.Executor.Watch;

namespace InfraGate.Executor.Handoff;

#pragma warning disable MEAI001 // IAgentHandler is in experimental A2A package
internal sealed class ExecutorAgentHandler(
    ExecutorConcurrencyGate concurrencyGate,
    PlanWatcher planWatcher) : IAgentHandler
{
    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        string? planId = context.Message?.Parts.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Text))?.Text;
        if (string.IsNullOrWhiteSpace(planId))
        {
            throw new A2AException("Executor handoff requires a planId.", A2AErrorCode.InvalidParams);
        }

        if (!concurrencyGate.TryAcquire())
        {
            await EnqueueResultAsync(
                context,
                eventQueue,
                ExecutorDispatchResult.Failed("Executor capacity is exhausted."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            ExecutorDispatchResult result = await planWatcher.WatchPlanAsync(
                new RemediationProposal
                {
                    PlanId = planId,
                    AnomalyId = context.ContextId,
                    ProposedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);
            await EnqueueResultAsync(context, eventQueue, result, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            concurrencyGate.Release();
        }
    }

    private static ValueTask EnqueueResultAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        ExecutorDispatchResult result,
        CancellationToken cancellationToken) =>
        eventQueue.EnqueueMessageAsync(
            new Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = Role.Agent,
                ContextId = context.ContextId,
                Parts = [new Part { Text = JsonSerializer.Serialize(result) }],
            },
            cancellationToken);

    public Task CancelAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
#pragma warning restore MEAI001
