namespace InfraGate.Planner.Handoff;

internal interface IObserverChannel
{
    Task SendProgressAsync(
        string cycleId,
        string stage,
        string? detail,
        int? proposalCount,
        CancellationToken cancellationToken = default);

    Task<ToolResponsePayload> SendToolRequestAsync(
        string cycleId,
        string toolName,
        string? argumentsJson,
        CancellationToken cancellationToken = default);
}
