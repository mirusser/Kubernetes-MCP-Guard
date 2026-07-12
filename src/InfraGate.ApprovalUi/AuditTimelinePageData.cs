namespace InfraGate.ApprovalUi;

/// <summary>
/// View model for the audit timeline navigator. Exposes the full correlated lifecycle
/// for a single plan_id without credentials or raw secrets.
/// </summary>
public sealed record class AuditTimelinePageData(
    string PlanId,
    string? AnomalyId,
    IReadOnlyList<AuditTimelineEntry> Entries);
