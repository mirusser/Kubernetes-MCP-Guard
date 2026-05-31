namespace InfraGate.Observer.Cycle;

// Ensures that only one cycle is running at a time,
// preventing overlapping runs if the LLM takes longer than the interval.
internal sealed class CycleSerialisation : IDisposable
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async Task<bool> TryAcquireScheduledAsync(CancellationToken cancellationToken)
    {
        return await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
    }

    public async Task AcquireForOnDemandAsync(CancellationToken cancellationToken)
    {
        if (await semaphore.WaitAsync(TimeSpan.FromSeconds(ObserverConventions.OnDemandSlackWindowSeconds), cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Release()
    {
        semaphore.Release();
    }

    public void Dispose()
    {
        semaphore.Dispose();
    }
}
