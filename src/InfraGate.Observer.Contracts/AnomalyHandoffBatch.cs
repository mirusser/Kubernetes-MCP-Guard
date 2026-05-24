namespace InfraGate.Observer.Contracts;

public sealed record class AnomalyHandoffBatch
{
    public required string CycleId { get; init; }
    public required DateTimeOffset EmittedAt { get; init; }
    public required IReadOnlyList<AnomalyReport> Reports { get; init; }
}
