using System.ComponentModel;
using InfraGate.Planner.Handoff;
using Microsoft.Extensions.AI;

namespace InfraGate.Planner.Llm;

internal static class AskObserverTool
{
    internal const string FunctionName = "ask_observer_to_inspect";

    internal static AIFunction Create(IObserverChannel channel, string cycleId)
    {
        async Task<string> InspectAsync(
            [Description("Name of the read-only Kubernetes tool to invoke (e.g. get_k8s_events, get_k8s_pods)")] string toolName,
            [Description("JSON-encoded tool arguments object, or null if the tool requires none")] string? argumentsJson,
            CancellationToken ct)
        {
            var result = await channel.SendToolRequestAsync(cycleId, toolName, argumentsJson, ct)
                .ConfigureAwait(false);
            return result.IsError ? $"error: {result.ResultJson}" : result.ResultJson;
        }

        return AIFunctionFactory.Create(
            InspectAsync,
            name: FunctionName,
            description: "Ask the Observer to run a read-only Kubernetes inspection tool and return the result. " +
                         "Use this when the anomaly report lacks current cluster state needed to make a confident decision.");
    }
}
