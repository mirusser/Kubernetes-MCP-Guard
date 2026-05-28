namespace InfraGate.Planner.Audit;

internal sealed record class PlannerAuditEntry(
    string EventName,
    object Payload,
    string? ProposalId = null,
    string? AnomalyId = null,
    string? PlanId = null,
    string? ActorSubject = null,
    string? ActorClientId = null,
    string? Outcome = null,
    string? Reason = null);
