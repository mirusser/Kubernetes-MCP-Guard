namespace InfraGate.Observer.Contracts;

public sealed record class ResourceRef
{
    public required string ApiVersion { get; init; }
    public required string Kind { get; init; }
    public required string Namespace { get; init; }
    public required string Name { get; init; }
}
