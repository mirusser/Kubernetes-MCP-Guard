using InfraGate.AgentGuardrails;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Decision;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using Microsoft.Agents.AI.Workflows;

namespace InfraGate.Planner.Cycle.Workflow;

[SendsMessage(typeof(DecisionContext))]
internal sealed class ValidateExecutor(
    string id,
    ConcurrentDictionary<string, byte> batchOperationKeys,
    PlannerDedupeStore dedupeStore,
    AgentGuardrailMetrics? guardrailMetrics,
    ILogger logger,
    IPlannerAuditOutbox? auditOutbox = null) : Executor<DecisionContext>(id)
{
    public override async ValueTask HandleAsync(
        DecisionContext ctx,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var decision = ctx.Decision;
        var report = ctx.Report;

        if (!PlannerConventions.OperationTypes.AllowedOperationTypes.Contains(decision.OperationType))
        {
            guardrailMetrics?.RecordDecision(GuardrailDecisionOutcome.Rejected, AgentGuardrailConventions.Reasons.InvalidOperation);
            PlannerLogEvents.LogDecisionInvalidOperation(logger, report.AnomalyId, decision.OperationType);
            var now = DateTimeOffset.UtcNow;
            dedupeStore.TrackActivePlan(report.AnomalyId, string.Empty, now,
                now + PlannerConventions.Dedupe.FailedProposalBackoff);
            if (auditOutbox is not null)
                await auditOutbox.AppendAsync(
                    new PlannerAuditEntry(
                        EventName: PlannerAuditEvents.DecisionInvalidOperation,
                        Payload: new { operationType = decision.OperationType },
                        AnomalyId: report.AnomalyId,
                        ActorSubject: PlannerConventions.Audit.ServicePlannerSubject,
                        Outcome: PlannerConventions.Audit.Outcomes.Failed,
                        Reason: AgentGuardrailConventions.Reasons.InvalidOperation),
                    cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!OperationArgumentValidator.TryNormalize(decision, out var normalizedArguments))
        {
            logger.LogDebug(
                "TryNormalize failed for anomaly {AnomalyId}: operationType={OperationType} arguments={Arguments}",
                report.AnomalyId,
                decision.OperationType,
                string.Join(", ", decision.Arguments.Select(kv => $"{kv.Key}={kv.Value}")));
            guardrailMetrics?.RecordDecision(GuardrailDecisionOutcome.Rejected, AgentGuardrailConventions.Reasons.InvalidArguments);
            PlannerLogEvents.LogDecisionInvalidArguments(logger, report.AnomalyId, decision.OperationType);
            var now = DateTimeOffset.UtcNow;
            dedupeStore.TrackActivePlan(report.AnomalyId, string.Empty, now,
                now + PlannerConventions.Dedupe.FailedProposalBackoff);
            if (auditOutbox is not null)
                await auditOutbox.AppendAsync(
                    new PlannerAuditEntry(
                        EventName: PlannerAuditEvents.DecisionInvalidArguments,
                        Payload: new { operationType = decision.OperationType, arguments = decision.Arguments },
                        AnomalyId: report.AnomalyId,
                        ActorSubject: PlannerConventions.Audit.ServicePlannerSubject,
                        Outcome: PlannerConventions.Audit.Outcomes.Failed,
                        Reason: AgentGuardrailConventions.Reasons.InvalidArguments),
                    cancellationToken).ConfigureAwait(false);
            return;
        }

        var normalized = decision with { Arguments = normalizedArguments };
        var operationKey = BuildOperationKey(normalized);

        if (!batchOperationKeys.TryAdd(operationKey, 0))
        {
            guardrailMetrics?.RecordDecision(GuardrailDecisionOutcome.Rejected, AgentGuardrailConventions.Reasons.DedupeInBatch);
            PlannerLogEvents.LogFilterDropped(logger, report.AnomalyId, PlannerConventions.FilterDropReasons.DedupeOperationInBatch);
            dedupeStore.TrackActivePlan(report.AnomalyId, string.Empty, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow + PlannerConventions.Dedupe.ActivePlanTtl);
            return;
        }

        guardrailMetrics?.RecordDecision(GuardrailDecisionOutcome.Accepted, AgentGuardrailConventions.Reasons.None);
        PlannerLogEvents.LogDecisionCompleted(logger, report.AnomalyId, normalized.OperationType);
        await context.SendMessageAsync(ctx with { Decision = normalized }, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildOperationKey(RemediationDecision decision)
    {
        var ns = decision.Arguments.TryGetValue(PlannerConventions.ToolArguments.Namespace, out var nsVal)
            ? nsVal as string ?? string.Empty
            : string.Empty;
        var name = decision.Arguments.TryGetValue(PlannerConventions.ToolArguments.Name, out var nameVal)
            ? nameVal as string ?? string.Empty
            : string.Empty;
        return $"{decision.OperationType}:{ns}/{name}";
    }
}
