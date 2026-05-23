using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InfraGate.Observer.Cycle;

internal sealed class ObservationCycleLoop : IHostedService, IDisposable
{
    private readonly IOptionsMonitor<ObserverOptions> options;
    private readonly ILogger<ObservationCycleLoop> logger;
    private Timer? timer;

    public ObservationCycleLoop(
        IOptionsMonitor<ObserverOptions> options,
        ILogger<ObservationCycleLoop> logger)
    {
        this.options = options;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        int cadenceSeconds = options.CurrentValue.CycleIntervalSeconds;
        logger.LogInformation("Observer starting with cadence {CycleIntervalSeconds}s", cadenceSeconds);
        timer = new Timer(
            ExecuteCycle,
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(cadenceSeconds));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Observer stopping");
        timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        timer?.Dispose();
    }

    private void ExecuteCycle(object? state)
    {
        var cycleId = Guid.NewGuid();
        logger.LogInformation("observer.cycle.started {CycleId}", cycleId);
        // Cycle body runs here — no work yet in skeleton
        logger.LogInformation("observer.cycle.completed {CycleId}", cycleId);
    }
}
