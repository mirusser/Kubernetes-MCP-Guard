namespace InfraGate.Observer.Contracts;

public sealed record class ToolResponsePayload
{
    public required bool IsError { get; init; }
    public required string ResultJson { get; init; }
}
