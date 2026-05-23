namespace InfraGate.Observer.Contracts;

public sealed record class AnomalyHandoffBatch(
    Guid CycleId,
    IReadOnlyList<AnomalyReport> Reports);
