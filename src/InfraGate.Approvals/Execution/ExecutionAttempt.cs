namespace InfraGate.Approvals.Execution;

public sealed record class ExecutionAttempt(
    string Id,
    string PlanId,
    string GrantId,
    DateTimeOffset StartedAtUtc);
