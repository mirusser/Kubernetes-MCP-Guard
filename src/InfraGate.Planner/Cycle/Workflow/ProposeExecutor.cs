using System.Diagnostics.Metrics;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Decision;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using InfraGate.Planner.Mcp;
using Microsoft.Agents.AI.Workflows;

namespace InfraGate.Planner.Cycle.Workflow;

[YieldsOutput(typeof(RemediationProposal))]
internal sealed class ProposeExecutor(
    string id,
    IPlannerMcpClient mcpClient,
    PlannerDedupeStore dedupeStore,
    IPlannerAuditOutbox? auditOutbox,
    Counter<long>? proposeFailedCounter,
    ILogger logger) : Executor<DecisionContext>(id)
{
    private const string ServicePlannerSubject = "service:planner";

    public override async ValueTask HandleAsync(
        DecisionContext ctx,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var decision = ctx.Decision;
        var report = ctx.Report;
        var proposedAt = DateTimeOffset.UtcNow;
        var planId = await ProposePlanAsync(report, decision, cancellationToken).ConfigureAwait(false);

        if (planId is null)
        {
            dedupeStore.TrackActivePlan(report.AnomalyId, string.Empty, proposedAt,
                proposedAt + PlannerConventions.Dedupe.FailedProposalBackoff);
            return;
        }

        PlannerLogEvents.LogProposePlanSucceeded(logger, report.AnomalyId, planId);
        dedupeStore.TrackActivePlan(report.AnomalyId, planId, proposedAt,
            proposedAt + PlannerConventions.Dedupe.ActivePlanTtl);

        var proposal = new RemediationProposal
        {
            PlanId = planId,
            AnomalyId = report.AnomalyId,
            ProposedAt = proposedAt,
        };
        await context.YieldOutputAsync(proposal, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ProposePlanAsync(AnomalyReport report, RemediationDecision decision, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [PlannerConventions.ToolArguments.OperationType] = decision.OperationType,
                    [PlannerConventions.ToolArguments.OperationArguments] = decision.Arguments,
                },
                cancellationToken).ConfigureAwait(false);

            if (TryExtractPlanId(result, out var planId))
            {
                if (auditOutbox is not null)
                {
                    await auditOutbox.AppendAsync(
                        new PlannerAuditEntry(
                            EventName: PlannerAuditEvents.ProposePlanSucceeded,
                            Payload: new { operationType = decision.OperationType, arguments = decision.Arguments },
                            AnomalyId: report.AnomalyId,
                            PlanId: planId,
                            ActorSubject: ServicePlannerSubject,
                            Outcome: "succeeded"),
                        cancellationToken).ConfigureAwait(false);
                }
                return planId;
            }

            proposeFailedCounter?.Add(1);
            PlannerLogEvents.LogProposePlanMissingPlanId(logger, report.AnomalyId);
            if (auditOutbox is not null)
            {
                await auditOutbox.AppendAsync(
                    new PlannerAuditEntry(
                        EventName: PlannerAuditEvents.ProposePlanFailed,
                        Payload: new { reasonCode = "missing_plan_id" },
                        AnomalyId: report.AnomalyId,
                        ActorSubject: ServicePlannerSubject,
                        Outcome: "failed",
                        Reason: "missing_plan_id"),
                    cancellationToken).ConfigureAwait(false);
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            proposeFailedCounter?.Add(1);
            PlannerLogEvents.LogProposePlanFailed(logger, report.AnomalyId, ex);
            if (auditOutbox is not null)
            {
                var statusCode = ex is HttpRequestException httpEx ? (int?)httpEx.StatusCode : null;
                await auditOutbox.AppendAsync(
                    new PlannerAuditEntry(
                        EventName: PlannerAuditEvents.ProposePlanFailed,
                        Payload: new { reasonCode = "gateway_error", errorClass = ex.GetType().Name, statusCode },
                        AnomalyId: report.AnomalyId,
                        ActorSubject: ServicePlannerSubject,
                        Outcome: "failed",
                        Reason: ex.GetType().Name),
                    cancellationToken).ConfigureAwait(false);
            }
            return null;
        }
    }

    private static bool TryExtractPlanId(string response, out string planId)
    {
        planId = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(response);
            return TryExtractPlanId(doc.RootElement, out planId);
        }
        catch (JsonException) { return false; }
    }

    private static bool TryExtractPlanId(JsonElement element, out string planId)
    {
        planId = string.Empty;
        return TryExtractPlanIdProperty(element, out planId)
            || TryExtractPlanIdFromText(element, PlannerConventions.ProposePlanResponseFields.TextLower, out planId)
            || TryExtractPlanIdFromText(element, PlannerConventions.ProposePlanResponseFields.TextUpper, out planId)
            || TryExtractPlanIdFromContent(element, out planId)
            || TryExtractPlanIdFromArray(element, out planId);
    }

    private static bool TryExtractPlanIdProperty(JsonElement element, out string planId)
    {
        planId = string.Empty;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(PlannerConventions.ProposePlanResponseFields.PlanId, out var planIdElement)
            || planIdElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(planIdElement.GetString()))
            return false;
        planId = planIdElement.GetString()!;
        return true;
    }

    private static bool TryExtractPlanIdFromContent(JsonElement element, out string planId)
    {
        planId = string.Empty;
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var prop in element.EnumerateObject())
        {
            if ((prop.NameEquals(PlannerConventions.ProposePlanResponseFields.ContentLower)
                || prop.NameEquals(PlannerConventions.ProposePlanResponseFields.ContentUpper))
                && TryExtractPlanId(prop.Value, out planId))
                return true;
        }
        return false;
    }

    private static bool TryExtractPlanIdFromArray(JsonElement element, out string planId)
    {
        planId = string.Empty;
        if (element.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in element.EnumerateArray())
        {
            if (TryExtractPlanId(item, out planId)) return true;
        }
        return false;
    }

    private static bool TryExtractPlanIdFromText(JsonElement element, string propertyName, out string planId)
    {
        planId = string.Empty;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var textElement)
            || textElement.ValueKind != JsonValueKind.String)
            return false;
        var text = textElement.GetString();
        if (string.IsNullOrWhiteSpace(text)) return false;
        return TryExtractPlanId(text, out planId);
    }
}
