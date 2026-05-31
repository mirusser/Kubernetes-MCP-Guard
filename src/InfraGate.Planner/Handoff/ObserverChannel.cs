using InfraGate.Planner.Diagnostics;
using Microsoft.Agents.AI;

namespace InfraGate.Planner.Handoff;

internal sealed class ObserverChannel(
    AIAgent agent,
    ILogger<ObserverChannel> logger) : IObserverChannel
{
    public async Task SendProgressAsync(
        string cycleId,
        string stage,
        string? detail,
        int? proposalCount,
        CancellationToken cancellationToken = default)
    {
        var envelope = new ObserverInboundEnvelope
        {
            Intent = ObserverInboundIntents.Progress,
            CycleId = cycleId,
            Progress = new PlanProgressPayload
            {
                Stage = stage,
                Detail = detail,
                ProposalCount = proposalCount,
            },
        };

        string json = JsonSerializer.Serialize(envelope);

        try
        {
            await agent.RunAsync(json, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PlannerLogEvents.LogProgressSendFailed(logger, cycleId, stage, ex);
        }
    }

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

        try
        {
            var response = await agent.RunAsync(json, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            string responseText = response.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(responseText))
                return new ToolResponsePayload { IsError = true, ResultJson = string.Empty };

            return JsonSerializer.Deserialize<ToolResponsePayload>(responseText)
                ?? new ToolResponsePayload { IsError = true, ResultJson = string.Empty };
        }
        catch (Exception ex)
        {
            PlannerLogEvents.LogToolRequestFailed(logger, cycleId, toolName, ex);
            return new ToolResponsePayload { IsError = true, ResultJson = string.Empty };
        }
    }
}
