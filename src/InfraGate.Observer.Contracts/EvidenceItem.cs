namespace InfraGate.Observer.Contracts;

public sealed record class EvidenceItem
{
    public required string Source { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
}
