using InfraGate.Observer.Diagnostics;
using InfraGate.Observer.Snapshot;
using Microsoft.Agents.AI.Workflows;

namespace InfraGate.Observer.Cycle.Workflow;

[SendsMessage(typeof(ChatMessage))]
[SendsMessage(typeof(TurnToken))]
internal sealed class SnapshotExecutor(
    string id,
    string namespaceName,
    ISnapshotFetcher snapshotFetcher,
    IModelVisibleContentGuard contentGuard,
    ILogger logger) : Executor<CycleWorkflowInput>(id)
{
    public override async ValueTask HandleAsync(
        CycleWorkflowInput message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        string snapshotJson;
        try
        {
            var snapshot = await snapshotFetcher.FetchAsync(namespaceName, cancellationToken).ConfigureAwait(false);
            snapshotJson = JsonSerializer.Serialize(snapshot, SnapshotSerializerOptions.Instance);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            ObserverLogEvents.LogSnapshotFetchFailed(logger, "FetchAsync", namespaceName, ex);
            snapshotJson = "{}";
        }

        var guardContent = new ModelVisibleContent(
            snapshotJson,
            ModelVisibleContentSource.ObserverSnapshot,
            $"observer-{namespaceName}");

        var decision = await contentGuard.EvaluateAsync(guardContent, cancellationToken).ConfigureAwait(false);

        if (decision.Action == ModelVisibleContentAction.BlockModelIngestion)
        {
            await context.SendMessageAsync(
                new ChatMessage(ChatRole.Assistant, AgentGuardrailConventions.DefaultBlockedPlaceholder),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        await context.SendMessageAsync(new ChatMessage(ChatRole.User, decision.Text), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await context.SendMessageAsync(new TurnToken(emitEvents: false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
