namespace InfraGate.Observer.Contracts;

public sealed record class AnomalyEvidence
{
    public required AnomalyKind Kind { get; init; }
    public required ResourceRef Target { get; init; }
    public string? PodCondition { get; init; }
    public int? RestartCount { get; init; }
    public int? RestartCountSinceLastCycle { get; init; }
    public bool IsPending { get; init; }
    public TimeSpan? PendingDuration { get; init; }
    public int? SpecReplicas { get; init; }
    public int? AvailableReplicas { get; init; }
    public bool IsAllPodsAffected { get; init; }
    public bool HasHealthySiblings { get; init; }
    public int? EndpointCount { get; init; }
    public string? EventType { get; init; }
    public int WarningCount { get; init; }
    public bool IsSustained { get; init; }
}
