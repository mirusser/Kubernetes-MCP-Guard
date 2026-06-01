using InfraGate.Planner.Audit;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using Microsoft.Agents.AI.Workflows;

namespace InfraGate.Planner.Cycle.Workflow;

[SendsMessage(typeof(AnomalyReport))]
internal sealed class FilterExecutor(
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
        var filterReason = GetFilterReason(message);
        if (filterReason is not null)
        {
            if (!string.Equals(filterReason, PlannerConventions.FilterDropReasons.Resolved, StringComparison.Ordinal)
                && auditOutbox is not null)
            {
                await auditOutbox.AppendAsync(
                    new PlannerAuditEntry(
                        EventName: PlannerAuditEvents.ProposalSkipped,
                        Payload: new { reasonCode = filterReason },
                        AnomalyId: message.AnomalyId,
                        ActorSubject: PlannerConventions.Audit.ServicePlannerSubject,
                        Outcome: PlannerConventions.Audit.Outcomes.Skipped,
                        Reason: filterReason),
                    cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        await context.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private string? GetFilterReason(AnomalyReport message)
    {
        if (message.Status == AnomalyStatus.Resolved)
        {
            dedupeStore.Remove(message.AnomalyId);
            PlannerLogEvents.LogFilterDropped(logger, message.AnomalyId, PlannerConventions.FilterDropReasons.Resolved);
            return PlannerConventions.FilterDropReasons.Resolved;
        }

        bool isAllowedKind = message.Kind is AnomalyKind.PodUnhealthy
            or AnomalyKind.DeploymentUnavailable
            or AnomalyKind.ServiceNoEndpoints
            or AnomalyKind.WarningEvent;

        if (!isAllowedKind)
        {
            PlannerLogEvents.LogFilterDropped(logger, message.AnomalyId, PlannerConventions.FilterDropReasons.UnsupportedKind);
            return PlannerConventions.FilterDropReasons.UnsupportedKind;
        }

        return null;
    }
}
