using InfraGate.Planner.Audit;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using Microsoft.Agents.AI.Workflows;

namespace InfraGate.Planner.Cycle.Workflow;

[SendsMessage(typeof(AnomalyReport))]
internal sealed class DedupeGateExecutor(
    string id,
    PlannerDedupeStore dedupeStore,
    IPlannerAuditOutbox? auditOutbox,
    ILogger logger) : Executor<AnomalyReport>(id)
{
    public override async ValueTask HandleAsync(
        AnomalyReport message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (dedupeStore.HasActivePlan(message.AnomalyId))
        {
            PlannerLogEvents.LogFilterDropped(logger, message.AnomalyId, PlannerConventions.FilterDropReasons.DedupeActivePlan);
            if (auditOutbox is not null)
            {
                await auditOutbox.AppendAsync(
                    new PlannerAuditEntry(
                        EventName: PlannerAuditEvents.ProposalSkipped,
                        Payload: new { reasonCode = PlannerConventions.FilterDropReasons.DedupeActivePlan },
                        AnomalyId: message.AnomalyId,
                        ActorSubject: PlannerConventions.Audit.ServicePlannerSubject,
                        Outcome: PlannerConventions.Audit.Outcomes.Skipped,
                        Reason: PlannerConventions.FilterDropReasons.DedupeActivePlan),
                    cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        await context.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
