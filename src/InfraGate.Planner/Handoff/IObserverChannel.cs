namespace InfraGate.Planner.Handoff;

internal interface IObserverChannel
{
    Task<ToolResponsePayload> SendToolRequestAsync(
        string cycleId,
        string toolName,
        string? argumentsJson,
        CancellationToken cancellationToken = default);
}
