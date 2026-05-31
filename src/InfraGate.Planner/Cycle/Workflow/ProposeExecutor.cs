using System.Diagnostics.Metrics;
using System.Text.Json;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Decision;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using InfraGate.AgentMcp;
using Microsoft.Agents.AI.Workflows;
using ModelContextProtocol.Protocol;

namespace InfraGate.Planner.Cycle.Workflow;

[YieldsOutput(typeof(RemediationProposal))]
internal sealed class ProposeExecutor(
    string id,
    IAgentMcpToolset mcpClient,
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
            var callResult = await mcpClient.CallToolAsync(
                PlannerConventions.ToolNames.ProposePlan,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [PlannerConventions.ToolArguments.OperationType] = decision.OperationType,
                    [PlannerConventions.ToolArguments.OperationArguments] = decision.Arguments,
                },
                cancellationToken).ConfigureAwait(false);

            var planId = ExtractPlanIdFromResponse(callResult);
            if (planId is null)
            {
                PlannerLogEvents.LogProposePlanMissingPlanId(logger, report.AnomalyId);
                return await RecordProposeFailureAsync(report, "missing_plan_id", null, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            PlannerLogEvents.LogProposePlanSucceeded(logger, report.AnomalyId, planId);
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
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            PlannerLogEvents.LogProposePlanFailed(logger, report.AnomalyId, ex);
            var statusCode = ex is HttpRequestException httpEx ? (int?)httpEx.StatusCode : null;
            return await RecordProposeFailureAsync(report, "gateway_error", ex.GetType().Name, statusCode, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string? ExtractPlanIdFromResponse(CallToolResult callResult)
    {
        if (callResult.Content is null)
            return null;

        foreach (var block in callResult.Content.OfType<TextContentBlock>())
        {
            if (TryExtractPlanId(block.Text, out var extracted))
                return extracted;
        }

        return null;
    }

    private async Task<string?> RecordProposeFailureAsync(
        AnomalyReport report,
        string reasonCode,
        string? errorClass,
        int? statusCode,
        CancellationToken cancellationToken)
    {
        proposeFailedCounter?.Add(1);
        if (auditOutbox is not null)
        {
            await auditOutbox.AppendAsync(
                new PlannerAuditEntry(
                    EventName: PlannerAuditEvents.ProposePlanFailed,
                    Payload: new { reasonCode, errorClass, statusCode },
                    AnomalyId: report.AnomalyId,
                    ActorSubject: ServicePlannerSubject,
                    Outcome: "failed",
                    Reason: errorClass ?? reasonCode),
                cancellationToken).ConfigureAwait(false);
        }
        return null;
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
