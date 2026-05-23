using InfraGate.Observer.Mcp;
using Microsoft.Extensions.Logging;

namespace InfraGate.Observer.Snapshot;

internal sealed class SnapshotFetcher : ISnapshotFetcher
{
    private readonly IObserverMcpClient mcpClient;
    private readonly ILogger<SnapshotFetcher> logger;

    public SnapshotFetcher(
        IObserverMcpClient mcpClient,
        ILogger<SnapshotFetcher> logger)
    {
        this.mcpClient = mcpClient;
        this.logger = logger;
    }

    public async Task<SnapshotDocument> FetchAsync(string namespaceName, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["namespace"] = namespaceName };

        var statusTask = FetchToolSafeAsync(ObserverConventions.ToolNames.GetK8sStatus, arguments, namespaceName, cancellationToken);
        var eventsTask = FetchToolSafeAsync(ObserverConventions.ToolNames.GetK8sEvents, arguments, namespaceName, cancellationToken);
        var podsTask = FetchToolSafeAsync(ObserverConventions.ToolNames.GetK8sPods, arguments, namespaceName, cancellationToken);
        var deploymentsTask = FetchToolSafeAsync(ObserverConventions.ToolNames.GetK8sDeployments, arguments, namespaceName, cancellationToken);
        var servicesTask = FetchToolSafeAsync(ObserverConventions.ToolNames.GetK8sServices, arguments, namespaceName, cancellationToken);
        var endpointsTask = FetchToolSafeAsync(ObserverConventions.ToolNames.GetK8sEndpoints, arguments, namespaceName, cancellationToken);

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
            return await mcpClient.GetToolResultAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch {ToolName} for namespace {Namespace}", toolName, namespaceName);
            return null;
        }
    }
}
