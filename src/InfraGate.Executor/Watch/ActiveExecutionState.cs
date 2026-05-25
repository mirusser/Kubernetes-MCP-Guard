namespace InfraGate.Executor.Watch;

internal sealed record class ActiveExecutionState(string PlanId, DateTimeOffset TrackedAt);
