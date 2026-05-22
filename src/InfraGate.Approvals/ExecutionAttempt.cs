namespace InfraGate.Approvals;

public sealed record class ExecutionAttempt(
    string Id,
    string PlanId,
    string GrantId,
    DateTimeOffset StartedAtUtc);
