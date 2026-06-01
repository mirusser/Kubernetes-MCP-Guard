using InfraGate.Planner.Diagnostics;
using Microsoft.Agents.AI;

namespace InfraGate.Planner.Handoff;

internal sealed class ObserverChannel(
    AIAgent agent,
    ILogger<ObserverChannel> logger) : IObserverChannel
{
    public async Task<ToolResponsePayload> SendToolRequestAsync(
        string cycleId,
        string toolName,
        string? argumentsJson,
        CancellationToken cancellationToken = default)
    {
        var envelope = new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.ToolRequest,
            CycleId = cycleId,
            ToolRequest = new ToolRequestPayload { ToolName = toolName, ArgumentsJson = argumentsJson },
        };

        string json = JsonSerializer.Serialize(envelope);

        PlannerLogEvents.LogToolRequestSent(logger, cycleId, toolName);

        try
        {
            var response = await agent.RunAsync(json, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            string responseText = response.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(responseText))
            {
                PlannerLogEvents.LogToolResponseReceived(logger, cycleId, toolName, isError: true);
                return new ToolResponsePayload { IsError = true, ResultJson = string.Empty };
            }

            var payload = JsonSerializer.Deserialize<ToolResponsePayload>(responseText)
                ?? new ToolResponsePayload { IsError = true, ResultJson = string.Empty };
            PlannerLogEvents.LogToolResponseReceived(logger, cycleId, toolName, payload.IsError);
            return payload;
        }
        catch (Exception ex)
        {
            PlannerLogEvents.LogToolRequestFailed(logger, cycleId, toolName, ex);
            return new ToolResponsePayload { IsError = true, ResultJson = string.Empty };
        }
    }
}
