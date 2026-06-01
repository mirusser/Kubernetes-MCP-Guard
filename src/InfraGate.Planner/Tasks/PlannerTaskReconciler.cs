using A2A;
using InfraGate.AgentMcp;
using InfraGate.Planner.Diagnostics;
using InfraGate.Planner.Handoff;
using InfraGate.Remediation.Contracts;
using ModelContextProtocol.Protocol;

namespace InfraGate.Planner.Tasks;

internal sealed class PlannerTaskReconciler(
    IPlannerTaskStore taskStore,
    PlannerTaskLifecycle lifecycle,
    IAgentMcpToolset mcpClient,
    ILogger<PlannerTaskReconciler> logger,
    IExecutorDispatchClient? executorDispatchClient = null) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        ReconcileAsync(stoppingToken);

    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        if (executorDispatchClient is null)
        {
            return;
        }

        while (true)
        {
            var response = await taskStore.ListTasksAsync(
                new ListTasksRequest
                {
                    Status = TaskState.AuthRequired,
                    IncludeArtifacts = true,
                },
                cancellationToken).ConfigureAwait(false);

            if (response.Tasks.Count == 0)
            {
                return;
            }

            foreach (var task in response.Tasks)
            {
                await ReconcileTaskSafelyAsync(task, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ReconcileTaskSafelyAsync(AgentTask task, CancellationToken cancellationToken)
    {
        try
        {
            await ReconcileTaskAsync(task, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            PlannerLogEvents.LogTaskReconciliationFailed(logger, task.Id, task.ContextId, ex);
            await lifecycle.FailAsync(task.Id, task.ContextId, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileTaskAsync(AgentTask task, CancellationToken cancellationToken)
    {
        string planId = ExtractPlanId(task)
            ?? throw new InvalidOperationException($"A2A task '{task.Id}' does not contain a plan reference artifact.");
        string planStatus = await GetPlanStatusAsync(planId, cancellationToken).ConfigureAwait(false);

        if (string.Equals(planStatus, PlannerConventions.PlanStatusValues.Applied, StringComparison.Ordinal))
        {
            await lifecycle.CompleteAsync(
                task.Id,
                task.ContextId,
                $"Plan '{planId}' was already applied.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(planStatus, PlannerConventions.PlanStatusValues.Expired, StringComparison.Ordinal) ||
            string.Equals(planStatus, PlannerConventions.PlanStatusValues.NotFound, StringComparison.Ordinal))
        {
            await lifecycle.FailAsync(
                task.Id,
                task.ContextId,
                $"Plan '{planId}' status is '{planStatus}'.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(planStatus, PlannerConventions.PlanStatusValues.ApprovalRequired, StringComparison.Ordinal) &&
            !string.Equals(planStatus, PlannerConventions.PlanStatusValues.Approved, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Plan '{planId}' returned unsupported status '{planStatus}'.");
        }

        var outcome = await executorDispatchClient!.DispatchAsync(
            task.ContextId,
            planId,
            cancellationToken).ConfigureAwait(false);
        await lifecycle.ApplyExecutorOutcomeAsync(
            task.Id,
            task.ContextId,
            outcome,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetPlanStatusAsync(string planId, CancellationToken cancellationToken)
    {
        var result = await mcpClient.CallToolAsync(
            PlannerConventions.ToolNames.GetPlanStatus,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlannerConventions.ToolArguments.PlanId] = planId,
            },
            cancellationToken).ConfigureAwait(false);

        if (result.IsError == true)
        {
            throw new InvalidOperationException($"Could not read status for plan '{planId}'.");
        }

        foreach (var content in result.Content.OfType<TextContentBlock>())
        {
            using var document = JsonDocument.Parse(content.Text);
            if (document.RootElement.TryGetProperty(
                    PlannerConventions.ProposePlanResponseFields.Status,
                    out var statusElement) &&
                statusElement.ValueKind == JsonValueKind.String)
            {
                return statusElement.GetString()!;
            }
        }

        throw new InvalidOperationException($"Status response for plan '{planId}' did not include a status.");
    }

    private static string? ExtractPlanId(AgentTask task) =>
        task.Artifacts?
            .FirstOrDefault(artifact => string.Equals(
                artifact.ArtifactId,
                PlannerTaskStoreConventions.Artifacts.PlanReferenceId,
                StringComparison.Ordinal))?
            .Parts
            .Select(part => part.Text)
            .FirstOrDefault(planId => !string.IsNullOrWhiteSpace(planId));
}
