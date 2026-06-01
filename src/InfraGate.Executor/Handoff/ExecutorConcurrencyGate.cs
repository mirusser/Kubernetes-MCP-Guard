namespace InfraGate.Executor.Handoff;

internal sealed class ExecutorConcurrencyGate : IDisposable
{
    private readonly SemaphoreSlim slots;

    public ExecutorConcurrencyGate(IOptions<ExecutorOptions> options)
    {
        int concurrencyCap = options.Value.ConcurrencyCap;
        slots = new SemaphoreSlim(concurrencyCap, concurrencyCap);
    }

    public bool TryAcquire() => slots.Wait(0);

    public void Release() => slots.Release();

    public void Dispose() => slots.Dispose();
}
