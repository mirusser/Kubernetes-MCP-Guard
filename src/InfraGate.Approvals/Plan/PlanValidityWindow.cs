namespace InfraGate.Approvals.Plan;

public sealed record class PlanValidityWindow(DateTimeOffset ValidFromUtc, DateTimeOffset ValidUntilUtc);
