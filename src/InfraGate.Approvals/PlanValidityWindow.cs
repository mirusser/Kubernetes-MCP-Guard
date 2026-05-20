namespace InfraGate.Approvals;

public sealed record class PlanValidityWindow(DateTimeOffset ValidFromUtc, DateTimeOffset ValidUntilUtc);
