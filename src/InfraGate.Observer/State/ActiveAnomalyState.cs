namespace InfraGate.Observer.State;

internal sealed class ActiveAnomalyState
{
    public required long FirstSeenCycle { get; set; }
    public required long LastSeenCycle { get; set; }
    public required string AnomalyId { get; set; }
    public required Severity LastSeverity { get; set; }
}
