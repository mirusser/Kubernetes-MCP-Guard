namespace InfraGate.Observer.Cycle;

internal interface IObservationCycleRunner
{
    Task<CycleResult> RunAsync(CancellationToken shutdownToken);
}
