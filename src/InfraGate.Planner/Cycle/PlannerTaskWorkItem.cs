namespace InfraGate.Planner.Cycle;

internal sealed record class PlannerTaskWorkItem(
    string TaskId,
    string ContextId,
    AnomalyHandoffBatch Batch);
