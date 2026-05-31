namespace InfraGate.Approvals.Audit;

public sealed record class ApprovalAuditEntry(
    string EventName,
    object Payload,
    string? PlanId = null,
    string? ChallengeId = null,
    string? GrantId = null,
    string? ExecutionAttemptId = null,
    string? ActorSubject = null,
    string? ActorClientId = null,
    string? Outcome = null,
    string? Reason = null);
