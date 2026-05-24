namespace InfraGate.Observer.Cycle;

internal sealed record class CycleResult
{
    public required string CycleId { get; init; }
    public required IReadOnlyList<AnomalyReport> Reports { get; init; }
    public bool IsTruncated { get; init; }
    public int ToolCallsUsed { get; init; }
    public int SeverityDisagreements { get; init; }
    public TimeSpan Duration { get; init; }
}
