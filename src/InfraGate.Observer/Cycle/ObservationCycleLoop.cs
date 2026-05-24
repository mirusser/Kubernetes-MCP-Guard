using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InfraGate.Observer.Cycle;

internal sealed class ObservationCycleLoop : IHostedService, IDisposable
{
    private readonly IOptionsMonitor<ObserverOptions> options;
    private readonly IObservationCycleRunner cycleRunner;
    private readonly CycleSerialisation cycleSerialisation;
    private readonly ILogger<ObservationCycleLoop> logger;
    private Timer? timer;
    private CancellationTokenSource? shutdownCts;

    public ObservationCycleLoop(
        IOptionsMonitor<ObserverOptions> options,
        IObservationCycleRunner cycleRunner,
        CycleSerialisation cycleSerialisation,
        ILogger<ObservationCycleLoop> logger)
    {
        this.options = options;
        this.cycleRunner = cycleRunner;
        this.cycleSerialisation = cycleSerialisation;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

#pragma warning disable CA1849 // Timer.Dispose() is lightweight; called synchronously during startup
        timer?.Dispose();
#pragma warning restore CA1849
        int cadenceSeconds = options.CurrentValue.CycleIntervalSeconds;
        ObserverLogEvents.LogObserverStarting(logger, cadenceSeconds);
        timer = new Timer(
            ExecuteCycle,
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(cadenceSeconds));

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ObserverLogEvents.LogObserverStopping(logger);
        timer?.Change(Timeout.Infinite, Timeout.Infinite);

        try
        {
            if (shutdownCts is not null)
            {
                await shutdownCts.CancelAsync().ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        timer?.Dispose();
        shutdownCts?.Dispose();
    }

#pragma warning disable MA0155 // Timer callback signature requires void return
    private async void ExecuteCycle(object? state)
#pragma warning restore MA0155
    {
        var shutdownToken = shutdownCts?.Token ?? CancellationToken.None;
        bool acquired = false;

        try
        {
            if (!await cycleSerialisation.TryAcquireScheduledAsync(shutdownToken).ConfigureAwait(false))
            {
                ObserverLogEvents.LogCycleSkipped(logger);
                return;
            }

            acquired = true;

            var result = await cycleRunner.RunAsync(shutdownToken).ConfigureAwait(false);

            if (result.IsTruncated && !shutdownToken.IsCancellationRequested)
            {
                ObserverLogEvents.LogCycleTruncatedWithDetails(
                    logger,
                    result.CycleId,
                    result.ToolCallsUsed,
                    (long)result.Duration.TotalMilliseconds);
            }
            else if (!result.IsTruncated)
            {
                ObserverLogEvents.LogCycleCompleted(
                    logger,
                    result.CycleId,
                    result.Reports.Count,
                    (long)result.Duration.TotalMilliseconds);
            }
        }
        catch (OperationCanceledException) when (shutdownCts?.IsCancellationRequested == true)
        {
            ObserverLogEvents.LogCycleCancelled(logger);
        }
        catch (Exception ex)
        {
            ObserverLogEvents.LogCycleError(logger, ex);
        }
        finally
        {
            if (acquired)
            {
                cycleSerialisation.Release();
            }
        }
    }
}
