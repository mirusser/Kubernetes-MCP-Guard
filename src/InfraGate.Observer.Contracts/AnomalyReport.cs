namespace InfraGate.Observer.Contracts;

public sealed record class AnomalyReport(
    string AnomalyId,
    Guid CycleId,
    DateTimeOffset DetectedAt,
    AnomalyKind Kind,
    ResourceRef Target,
    Severity Severity,
    AnomalyStatus Status,
    string Summary,
    IReadOnlyList<EvidenceItem> Evidence,
    RemediationHint? Suggested,
    IReadOnlyDictionary<string, string> Annotations);
