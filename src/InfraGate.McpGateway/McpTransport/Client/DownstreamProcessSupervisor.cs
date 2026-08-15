using System.Diagnostics.Metrics;
using System.Security.Cryptography;

namespace InfraGate.McpGateway;

/// <summary>
/// Wraps a <see cref="DownstreamMcpClient"/> for the optional secondary downstream, detecting
/// transport faults (unexpected process exit or a broken handshake) and recovering with a
/// single-flight, capped-exponential-backoff-with-jitter restart loop. On success it invalidates
/// and republishes the source's catalog entries via
/// <see cref="IGatewayToolDispatcher.RegenerateSourceAsync"/>; when restart attempts are exhausted
/// it records the source as degraded via <see cref="DownstreamToolCatalog.RecordSourceDegraded"/>
/// rather than throwing or taking down the primary downstream or the Gateway. Never wrap the
/// mandatory primary downstream with this type — only the optional secondary may be degraded.
/// </summary>
internal sealed class DownstreamProcessSupervisor(
    DownstreamMcpClient inner,
    string sourceId,
    DownstreamProcessSupervisorOptions options,
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<DownstreamProcessSupervisor> logger,
    CancellationToken shutdownToken) : IDownstreamMcpClient, ISupervisedDownstreamStatus, IAsyncDisposable
{
    private static readonly Meter Meter = new(
        McpGatewayConventions.Telemetry.MeterName,
        McpGatewayConventions.Telemetry.MeterVersion);

    private static readonly Counter<long> RestartCounter =
        Meter.CreateCounter<long>(McpGatewayConventions.Telemetry.DownstreamRestartCounterName);

    private readonly Lock restartGate = new();
    private readonly CancellationTokenSource disposalCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
    private Task? restartTask;
    private long processGeneration = 1;

    /// <summary>
    /// Count of successful process (re)creations, starting at 1 for the original process. Bumped
    /// once per successful restart, independent of the catalog's own per-publish generation.
    /// </summary>
    public long ProcessGeneration => Interlocked.Read(ref processGeneration);

    /// <inheritdoc/>
    public bool IsRestarting
    {
        get
        {
            lock (restartGate)
            {
                return restartTask is { IsCompleted: false };
            }
        }
    }

    public async Task<DownstreamCallResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        DownstreamCallResult result = await inner.CallToolAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);
        if (result.IsTransportFault)
        {
            logger.LogWarning(
                "Downstream '{SourceId}' call to '{ToolName}' hit a transport fault; triggering a supervised restart.",
                sourceId,
                toolName);
            TriggerRestart();
        }

        return result;
    }

    public async Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await inner.ListToolsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransportFault(ex))
        {
            logger.LogWarning(
                ex,
                "Downstream '{SourceId}' ListToolsAsync hit a transport fault; triggering a supervised restart.",
                sourceId);
            TriggerRestart();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await disposalCts.CancelAsync().ConfigureAwait(false);

        Task? pendingRestart;
        lock (restartGate)
        {
            pendingRestart = restartTask;
        }

        if (pendingRestart is not null)
        {
            await pendingRestart.ConfigureAwait(false);
        }

        disposalCts.Dispose();
        await inner.DisposeAsync().ConfigureAwait(false);
    }

    private void TriggerRestart()
    {
        lock (restartGate)
        {
            if (restartTask is { IsCompleted: false })
            {
                return;
            }

            restartTask = RunRestartLoopAsync(disposalCts.Token);
        }
    }

    private async Task RunRestartLoopAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= options.MaxAttempts; attempt++)
        {
            try
            {
                await Task.Delay(ComputeBackoff(attempt), timeProvider, cancellationToken).ConfigureAwait(false);

                await inner.ResetAsync(cancellationToken).ConfigureAwait(false);
                await inner.ListToolsAsync(cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref processGeneration);

                IGatewayToolDispatcher dispatcher = serviceProvider.GetRequiredService<IGatewayToolDispatcher>();
                await dispatcher.RegenerateSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);

                logger.LogInformation(
                    "Downstream '{SourceId}' restarted successfully on attempt {Attempt}; process generation {Generation}.",
                    sourceId,
                    attempt,
                    ProcessGeneration);
                RecordRestartOutcome(McpGatewayConventions.Telemetry.Outcomes.RestartSucceeded);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Downstream '{SourceId}' restart attempt {Attempt}/{MaxAttempts} failed.",
                    sourceId,
                    attempt,
                    options.MaxAttempts);
                RecordRestartOutcome(McpGatewayConventions.Telemetry.Outcomes.RestartAttemptFailed);
            }
        }

        logger.LogError(
            "Downstream '{SourceId}' exhausted {MaxAttempts} restart attempts; leaving the secondary downstream degraded.",
            sourceId,
            options.MaxAttempts);

        RecordRestartOutcome(McpGatewayConventions.Telemetry.Outcomes.RestartExhausted);
        serviceProvider.GetRequiredService<DownstreamToolCatalog>()
            .RecordSourceDegraded(sourceId, McpGatewayMessages.ToolCatalog.RestartAttemptsExhausted);
    }

    private void RecordRestartOutcome(string outcome)
    {
        RestartCounter.Add(1,
            new KeyValuePair<string, object?>(McpGatewayConventions.Telemetry.Tags.Source, sourceId),
            new KeyValuePair<string, object?>(McpGatewayConventions.Telemetry.Tags.Outcome, outcome));
    }

    internal TimeSpan ComputeBackoff(int attempt)
    {
        double exponent = Math.Min(attempt - 1, 20);
        double cappedMs = Math.Min(
            options.MinBackoff.TotalMilliseconds * Math.Pow(2, exponent),
            options.MaxBackoff.TotalMilliseconds);
        int minMs = (int)options.MinBackoff.TotalMilliseconds;
        int rangeMs = (int)Math.Max(cappedMs - minMs, 0);
        int jitterMs = rangeMs > 0 ? RandomNumberGenerator.GetInt32(rangeMs + 1) : 0;
        return TimeSpan.FromMilliseconds(minMs + jitterMs);
    }

    // ClientTransportClosedException (handshake-time failure) derives from IOException (post-handshake
    // process death surfaces as this base type directly), so one pattern covers both.
    private static bool IsTransportFault(Exception ex) =>
        ex is IOException or ObjectDisposedException;
}
