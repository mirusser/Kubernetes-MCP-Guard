using A2A;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Cycle;
using InfraGate.Planner.Diagnostics;
using InfraGate.Planner.Tasks;

namespace InfraGate.Planner.Handoff;

#pragma warning disable MEAI001 // IAgentHandler is in experimental A2A package
internal sealed class PlannerHandoffAgentHandler(
    AnomalyBatchQueue batchQueue,
    IPlannerTaskStore taskStore,
    ILogger<PlannerHandoffAgentHandler> logger,
    IPlannerAuditOutbox? auditOutbox = null) : IAgentHandler
{
    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        string? json = context.Message?.Parts.FirstOrDefault(p => p.Text is not null)?.Text;

        if (json is not null && JsonSerializer.Deserialize<AnomalyHandoffBatch>(json) is { } batch)
        {
            if (batch.Reports.Count != 1
                || !string.Equals(batch.Reports[0].AnomalyId, context.ContextId, StringComparison.Ordinal))
            {
                throw new A2AException(
                    "Planner handoff requires one anomaly report with contextId matching anomalyId.",
                    A2AErrorCode.InvalidParams);
            }

            var task = new AgentTask
            {
                Id = context.TaskId,
                ContextId = context.ContextId,
                Status = new A2A.TaskStatus
                {
                    State = TaskState.Submitted,
                    Timestamp = DateTimeOffset.UtcNow,
                },
            };

            if (!await taskStore.TryCreateTaskAsync(context.TaskId, task, cancellationToken).ConfigureAwait(false))
            {
                await EnqueueAcceptedMessageAsync(context, eventQueue, cancellationToken).ConfigureAwait(false);
                return;
            }

            await eventQueue.EnqueueTaskAsync(task, cancellationToken).ConfigureAwait(false);

            PlannerLogEvents.LogHandoffBatchReceived(logger, batch.CycleId, batch.Reports.Count);

            if (auditOutbox is not null)
            {
                await auditOutbox.AppendAsync(
                    new PlannerAuditEntry(
                        EventName: PlannerAuditEvents.HandoffReceived,
                        Payload: new
                        {
                            taskId = context.TaskId,
                            contextId = context.ContextId,
                            cycleId = batch.CycleId,
                            anomalyIds = batch.Reports.Select(r => r.AnomalyId).ToArray(),
                            count = batch.Reports.Count,
                        },
                        ActorSubject: PlannerConventions.Audit.ServiceObserverSubject,
                        Outcome: PlannerConventions.Audit.Outcomes.Received),
                    cancellationToken).ConfigureAwait(false);
            }

            if (!batchQueue.TryEnqueue(new PlannerTaskWorkItem(context.TaskId, context.ContextId, batch)))
            {
                PlannerLogEvents.LogHandoffBatchBackpressure(logger, batch.CycleId);
            }

            return;
        }

        await EnqueueAcceptedMessageAsync(context, eventQueue, cancellationToken).ConfigureAwait(false);
    }

    private static ValueTask EnqueueAcceptedMessageAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken) =>
        eventQueue.EnqueueMessageAsync(
            new Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = Role.Agent,
                ContextId = context.ContextId,
                Parts = [new Part { Text = PlannerConventions.A2AHandoff.AcceptedResponse }],
            },
            cancellationToken);

    public Task CancelAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
#pragma warning restore MEAI001
