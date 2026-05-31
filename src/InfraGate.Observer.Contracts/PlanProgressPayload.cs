namespace InfraGate.Observer.Contracts;

public sealed record class PlanProgressPayload
{
    public required string Stage { get; init; }
    public string? Detail { get; init; }
    public int? ProposalCount { get; init; }
}
