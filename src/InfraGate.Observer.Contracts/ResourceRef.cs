namespace InfraGate.Observer.Contracts;

public sealed record class ResourceRef(
    string ApiVersion,
    string Kind,
    string Namespace,
    string Name);
