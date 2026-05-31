namespace InfraGate.Observer.Contracts;

public sealed record class ToolRequestPayload
{
    public required string ToolName { get; init; }
    public string? ArgumentsJson { get; init; }
}
