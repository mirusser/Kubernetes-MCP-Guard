using System.Diagnostics.Metrics;
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
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["namespace"] = namespaceName };

        Task<string?> statusTask = FetchToolSafeAsync(ObserverConventions.ToolNames.GetK8sStatus, arguments, namespaceName, cancellationToken);
        Task<string?> eventsTask = FetchToolSafeAsync(ObserverConventions.ToolNames.GetK8sEvents, arguments, namespaceName, cancellationToken);
        Task<string?> podsTask = FetchToolSafeAsync(ObserverConventions.ToolNames.GetK8sPods, arguments, namespaceName, cancellationToken);
        Task<string?> deploymentsTask = FetchToolSafeAsync(ObserverConventions.ToolNames.GetK8sDeployments, arguments, namespaceName, cancellationToken);
        Task<string?> servicesTask = FetchToolSafeAsync(ObserverConventions.ToolNames.GetK8sServices, arguments, namespaceName, cancellationToken);
        Task<string?> endpointsTask = FetchToolSafeAsync(ObserverConventions.ToolNames.GetK8sEndpoints, arguments, namespaceName, cancellationToken);

        await Task.WhenAll(statusTask, eventsTask, podsTask, deploymentsTask, servicesTask, endpointsTask).ConfigureAwait(false);

        return new SnapshotDocument(
            namespaceName,
            await statusTask.ConfigureAwait(false),
            await eventsTask.ConfigureAwait(false),
            await podsTask.ConfigureAwait(false),
            await deploymentsTask.ConfigureAwait(false),
            await servicesTask.ConfigureAwait(false),
            await endpointsTask.ConfigureAwait(false),
            DateTimeOffset.UtcNow);
    }

    private async Task<string?> FetchToolSafeAsync(
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
            return string.IsNullOrEmpty(text) ? null : text;
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
