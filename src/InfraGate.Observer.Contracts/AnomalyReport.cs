namespace InfraGate.Observer.Contracts;

public sealed record class AnomalyReport
{
    public required string AnomalyId { get; init; }
    public required string CycleId { get; init; }
    public required DateTimeOffset DetectedAt { get; init; }
    public required AnomalyKind Kind { get; init; }
    public required ResourceRef Target { get; init; }
    public required Severity Severity { get; init; }
    public required AnomalyStatus Status { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<EvidenceItem> Evidence { get; init; }
    public RemediationHint? Suggested { get; init; }
    public required IReadOnlyDictionary<string, string> Annotations { get; init; }
}
