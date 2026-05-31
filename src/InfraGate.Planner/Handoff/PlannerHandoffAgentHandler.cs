using A2A;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Cycle;
using InfraGate.Planner.Diagnostics;

namespace InfraGate.Planner.Handoff;

#pragma warning disable MEAI001 // IAgentHandler is in experimental A2A package
internal sealed class PlannerHandoffAgentHandler(
    AnomalyBatchQueue batchQueue,
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
            PlannerLogEvents.LogHandoffBatchReceived(logger, batch.CycleId, batch.Reports.Count);

            if (auditOutbox is not null)
            {
                await auditOutbox.AppendAsync(
                    new PlannerAuditEntry(
                        EventName: PlannerAuditEvents.HandoffReceived,
                        Payload: new
                        {
                            cycleId = batch.CycleId,
                            anomalyIds = batch.Reports.Select(r => r.AnomalyId).ToArray(),
                            count = batch.Reports.Count,
                        },
                        ActorSubject: "service:observer",
                        Outcome: "received"),
                    cancellationToken).ConfigureAwait(false);
            }

            if (!batchQueue.TryEnqueue(batch))
            {
                PlannerLogEvents.LogHandoffBatchBackpressure(logger, batch.CycleId);
            }
        }

        await eventQueue.EnqueueMessageAsync(
            new Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = Role.Agent,
                Parts = [new Part { Text = "accepted" }],
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task CancelAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
#pragma warning restore MEAI001
