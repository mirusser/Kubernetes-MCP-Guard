namespace InfraGate.Observer.Contracts;

public sealed record class RemediationHint
{
    public string? Action { get; init; }
    public string? Explanation { get; init; }
}
