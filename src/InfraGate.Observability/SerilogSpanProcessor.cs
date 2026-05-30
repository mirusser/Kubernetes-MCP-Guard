using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

namespace InfraGate.Observability;

internal sealed class SerilogSpanProcessor(ILogger<SerilogSpanProcessor> logger) : BaseProcessor<Activity>
{
    private static readonly HashSet<string> relevantSources = new(StringComparer.Ordinal)
    {
        TelemetryConventions.AgentsAiSourceName,
        TelemetryConventions.ExtensionsAiSourceName,
        TelemetryConventions.WorkflowsSourceName,
    };

    public override void OnEnd(Activity data)
    {
        if (!relevantSources.Contains(data.Source.Name)) return;

        ObservabilityLogEvents.LogSpanCompleted(
            logger,
            data.DisplayName,
            data.GetTagItem("gen_ai.operation.name") as string,
            (data.GetTagItem("gen_ai.request.model") ?? data.GetTagItem("gen_ai.response.model")) as string,
            data.GetTagItem("gen_ai.agent.name") as string,
            TagAsString(data.GetTagItem("gen_ai.usage.input_tokens")),
            TagAsString(data.GetTagItem("gen_ai.usage.output_tokens")),
            data.Duration.TotalMilliseconds,
            data.Status,
            data.TraceId.ToString(),
            data.SpanId.ToString());
    }

    private static string? TagAsString(object? value) =>
        value switch
        {
            null => null,
            string s => s,
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
}
