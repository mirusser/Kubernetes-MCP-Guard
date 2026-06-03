using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;
using InfraGate.AgentMcp;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace InfraGate.Observer.Snapshot;

internal sealed class SnapshotFetcher : ISnapshotFetcher
{
    private readonly IAgentMcpToolset mcpClient;
    private readonly ILogger<SnapshotFetcher> logger;
    private readonly Counter<long>? snapshotFetchErrorsCounter;

    public SnapshotFetcher(
        IAgentMcpToolset mcpClient,
        ILogger<SnapshotFetcher> logger,
        Meter? meter = null)
    {
        this.mcpClient = mcpClient;
        this.logger = logger;
        snapshotFetchErrorsCounter = ObserverMetrics.CreateSnapshotFetchErrorsCounter(meter);
    }

    public async Task<SnapshotDocument> FetchAsync(string namespaceName, CancellationToken cancellationToken)
    {
        var availableTools = await mcpClient.GetAgentToolsAsync(cancellationToken).ConfigureAwait(false);
        var availableNames = availableTools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        var tasks = ObserverConventions.ToolNames.NamespaceSnapshotTools
            .Where(availableNames.Contains)
            .ToDictionary(
                name => name,
                name => FetchToolSafeAsync(name, ToolArguments(name, namespaceName), namespaceName, cancellationToken),
                StringComparer.Ordinal);

        await Task.WhenAll(tasks.Values).ConfigureAwait(false);

        var toolResults = tasks.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Result,
            StringComparer.Ordinal);

        return new SnapshotDocument(namespaceName, toolResults, DateTimeOffset.UtcNow);
    }

    private static IReadOnlyDictionary<string, object?> ToolArguments(string toolName, string namespaceName)
    {
        var args = new Dictionary<string, object?>(StringComparer.Ordinal) { ["namespace"] = namespaceName };

        if (string.Equals(toolName, ObserverConventions.ToolNames.GetK8sEvents, StringComparison.OrdinalIgnoreCase))
        {
            args["excludeEventTypes"] = new[] { "Normal" };
        }

        return args;
    }

    private async Task<JsonNode?> FetchToolSafeAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        string namespaceName,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await mcpClient.CallToolAsync(toolName, arguments, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsError == true)
            {
                ObserverLogEvents.LogMcpToolError(logger, toolName);
                return null;
            }

            string text = string.Join(
                Environment.NewLine,
                result.Content.OfType<TextContentBlock>().Select(c => c.Text));
            if (string.IsNullOrEmpty(text))
                return null;

            return JsonNode.Parse(text, nodeOptions: null, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            ObserverLogEvents.LogSnapshotFetchFailed(logger, toolName, namespaceName, ex);

            if (snapshotFetchErrorsCounter is not null)
            {
                snapshotFetchErrorsCounter.Add(1,
                    new KeyValuePair<string, object?>(ObserverMetrics.ToolNameTag, toolName));
            }

            return null;
        }
    }
}
