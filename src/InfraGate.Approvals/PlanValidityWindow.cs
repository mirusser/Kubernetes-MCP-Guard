namespace InfraGate.Approvals;

public sealed record PlanValidityWindow(DateTimeOffset ValidFromUtc, DateTimeOffset ValidUntilUtc);
