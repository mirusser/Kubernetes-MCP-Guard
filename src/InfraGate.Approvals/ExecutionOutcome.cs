namespace InfraGate.Approvals;

public sealed record class ExecutionOutcome(
    string Id,
    string AttemptId,
    string PlanId,
    string Status,
    DateTimeOffset CompletedAtUtc,
    string Message,
    string? ReasonCode,
    string? TargetNamespace);
