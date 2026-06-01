using System.Diagnostics.Metrics;
using InfraGate.Executor.Diagnostics;
using InfraGate.Executor.Mcp;
using Serilog.Context;

namespace InfraGate.Executor.Watch;

internal sealed class PlanWatcher
{
    private readonly IExecutorDedupeStore dedupeStore;
    private readonly IExecutorMcpClient mcpClient;
    private readonly IOptionsMonitor<ExecutorOptions> optionsMonitor;
    private readonly ILogger<PlanWatcher> logger;
    private readonly Counter<long>? watchTimeoutCounter;
    private readonly Counter<long>? watchFailedCounter;
    private readonly Counter<long>? executeSucceededCounter;
    private readonly Counter<long>? executeFailedCounter;
    private readonly Counter<long>? executeBlockedCounter;

    public PlanWatcher(
        IExecutorDedupeStore dedupeStore,
        IExecutorMcpClient mcpClient,
        IOptionsMonitor<ExecutorOptions> optionsMonitor,
        ILogger<PlanWatcher> logger,
        Meter? meter = null)
    {
        this.dedupeStore = dedupeStore;
        this.mcpClient = mcpClient;
        this.optionsMonitor = optionsMonitor;
        this.logger = logger;
        watchTimeoutCounter = ExecutorMetrics.CreateWatchTimeoutCounter(meter);
        watchFailedCounter = ExecutorMetrics.CreateWatchFailedCounter(meter);
        executeSucceededCounter = ExecutorMetrics.CreateExecuteSucceededCounter(meter);
        executeFailedCounter = ExecutorMetrics.CreateExecuteFailedCounter(meter);
        executeBlockedCounter = ExecutorMetrics.CreateExecuteBlockedCounter(meter);
    }

    internal async Task<ExecutorDispatchResult> WatchPlanAsync(
        RemediationProposal proposal,
        CancellationToken stoppingToken)
    {
        var planId = proposal.PlanId;
        using var planScope = LogContext.PushProperty("PlanId", planId);

        if (!dedupeStore.TryTrack(planId))
        {
            return ExecutorDispatchResult.Failed($"Plan '{planId}' is already being executed.");
        }

        try
        {
            ExecutorLogEvents.LogWatchStarted(logger, planId);
            var opts = optionsMonitor.CurrentValue;
            string status = await WaitForApprovalAsync(planId, opts, stoppingToken).ConfigureAwait(false);

            if (string.Equals(status, ExecutorConventions.PlanStatusValues.Approved, StringComparison.Ordinal))
            {
                ExecutorLogEvents.LogWatchApproved(logger, planId);
                return await ExecutePlanAsync(planId, stoppingToken).ConfigureAwait(false);
            }

            if (string.Equals(status, ExecutorConventions.PlanStatusValues.Applied, StringComparison.Ordinal))
            {
                return ExecutorDispatchResult.Applied($"Plan '{planId}' was already applied.");
            }

            watchTimeoutCounter?.Add(1);
            ExecutorLogEvents.LogWatchTimeout(logger, planId);
            return ExecutorDispatchResult.Failed(
                $"Plan '{planId}' did not reach approval. Last status: {status}.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            watchFailedCounter?.Add(1);
            ExecutorLogEvents.LogWatchFailed(logger, planId, ex);
            return ExecutorDispatchResult.Failed(ex.Message);
        }
        finally
        {
            dedupeStore.Remove(planId);
        }
    }

    private async Task<string> WaitForApprovalAsync(string planId, ExecutorOptions opts, CancellationToken stoppingToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(opts.WatchTimeoutSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            int remaining = (int)(deadline - DateTimeOffset.UtcNow).TotalSeconds;
            if (remaining <= 0)
            {
                return ExecutorConventions.PlanStatusValues.ApprovalRequired;
            }

            int callTimeout = Math.Min(remaining, ExecutorConventions.WaitForPlanApprovalPerCallTimeoutSeconds);

            var response = await mcpClient.CallToolAsync(
                ExecutorConventions.ToolNames.WaitForPlanApproval,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ExecutorConventions.ToolArguments.PlanId] = planId,
                    [ExecutorConventions.ToolArguments.TimeoutSeconds] = callTimeout,
                },
                stoppingToken).ConfigureAwait(false);

            if (!TryParseWaitResult(response, out string status, out bool timedOut))
            {
                throw new InvalidOperationException(
                    $"Could not parse wait_for_plan_approval response for plan '{planId}'.");
            }

            if (string.Equals(status, ExecutorConventions.PlanStatusValues.Approved, StringComparison.Ordinal))
            {
                return status;
            }

            if (string.Equals(status, ExecutorConventions.PlanStatusValues.NotFound, StringComparison.Ordinal) ||
                string.Equals(status, ExecutorConventions.PlanStatusValues.Expired, StringComparison.Ordinal) ||
                string.Equals(status, ExecutorConventions.PlanStatusValues.Applied, StringComparison.Ordinal))
            {
                return status;
            }

            if (!timedOut)
            {
                return status;
            }

            // timedOut=true, status=ApprovalRequired: loop until wall-clock deadline
        }

        stoppingToken.ThrowIfCancellationRequested();
        return ExecutorConventions.PlanStatusValues.ApprovalRequired;
    }

    private async Task<ExecutorDispatchResult> ExecutePlanAsync(string planId, CancellationToken stoppingToken)
    {
        string response;
        try
        {
            response = await mcpClient.CallToolAsync(
                ExecutorConventions.ToolNames.ExecuteApprovedPlan,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ExecutorConventions.ToolArguments.PlanId] = planId,
                },
                stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            executeFailedCounter?.Add(1);
            ExecutorLogEvents.LogExecuteFailed(logger, planId, ex);
            return ExecutorDispatchResult.Failed(ex.Message);
        }

        if (IsErrorResponse(response))
        {
            executeBlockedCounter?.Add(1);
            ExecutorLogEvents.LogExecuteBlocked(logger, planId);
            return ExecutorDispatchResult.Failed(response);
        }

        executeSucceededCounter?.Add(1);
        ExecutorLogEvents.LogExecuteSucceeded(logger, planId);
        return ExecutorDispatchResult.Applied(response);
    }

    internal static bool TryParseWaitResult(string response, out string status, out bool timedOut)
    {
        status = string.Empty;
        timedOut = false;

        try
        {
            using var doc = JsonDocument.Parse(response);
            return TryFindWaitResult(doc.RootElement, ref status, ref timedOut);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryFindWaitResult(JsonElement element, ref string status, ref bool timedOut)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => TryFindWaitResultInObject(element, ref status, ref timedOut),
            JsonValueKind.Array => TryFindWaitResultInArray(element, ref status, ref timedOut),
            _ => false
        };
    }

    internal static bool TryFindWaitResultInObject(JsonElement element, ref string status, ref bool timedOut)
    {
        if (TryReadWaitStatus(element, ref status, ref timedOut))
        {
            return true;
        }

        foreach (var value in element.EnumerateObject().Select(static prop => prop.Value))
        {
            if (TryFindWaitResultInValue(value, ref status, ref timedOut))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryReadWaitStatus(JsonElement element, ref string status, ref bool timedOut)
    {
        if (!element.TryGetProperty("status", out var statusEl) ||
            statusEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        status = statusEl.GetString()!;
        timedOut = element.TryGetProperty("timedOut", out var timedOutEl) &&
            timedOutEl.ValueKind == JsonValueKind.True;
        return true;
    }

    internal static bool TryFindWaitResultInValue(JsonElement value, ref string status, ref bool timedOut)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return TryFindWaitResultInJsonString(value.GetString(), ref status, ref timedOut);
        }

        return value.ValueKind is JsonValueKind.Object or JsonValueKind.Array &&
            TryFindWaitResult(value, ref status, ref timedOut);
    }

    internal static bool TryFindWaitResultInJsonString(string? text, ref string status, ref bool timedOut)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains('"', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var inner = JsonDocument.Parse(text);
            return TryFindWaitResult(inner.RootElement, ref status, ref timedOut);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryFindWaitResultInArray(JsonElement element, ref string status, ref bool timedOut)
    {
        foreach (var item in element.EnumerateArray())
        {
            if (TryFindWaitResult(item, ref status, ref timedOut))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsErrorResponse(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("isError", out var isErrorEl) &&
                   isErrorEl.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
