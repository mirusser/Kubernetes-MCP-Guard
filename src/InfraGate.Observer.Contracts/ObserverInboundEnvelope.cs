namespace InfraGate.Observer.Contracts;

public sealed record class ObserverInboundEnvelope
{
    public required string Intent { get; init; }
    public required string CycleId { get; init; }
    public ToolRequestPayload? ToolRequest { get; init; }
}
