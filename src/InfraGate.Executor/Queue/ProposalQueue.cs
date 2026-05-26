namespace InfraGate.Executor.Queue;

internal sealed class ProposalQueue : IDisposable
{
    private readonly Channel<RemediationProposal> channel = Channel.CreateUnbounded<RemediationProposal>();
    private readonly SemaphoreSlim concurrencySlots;

    public ProposalQueue(IOptions<ExecutorOptions> options)
    {
        int cap = options.Value.ConcurrencyCap;
        concurrencySlots = new SemaphoreSlim(cap, cap);
    }

    public ChannelReader<RemediationProposal> Reader => channel.Reader;

    public int AvailableSlots => concurrencySlots.CurrentCount;

    public bool TryEnqueueAll(IReadOnlyList<RemediationProposal> proposals)
    {
        if (proposals.Count == 0)
        {
            return true;
        }

        if (concurrencySlots.CurrentCount < proposals.Count)
        {
            return false;
        }

        int acquired = 0;
        for (int i = 0; i < proposals.Count; i++)
        {
            if (!concurrencySlots.Wait(0))
            {
                if (acquired > 0)
                {
                    concurrencySlots.Release(acquired);
                }
                return false;
            }
            acquired++;
        }

        foreach (var proposal in proposals)
        {
            channel.Writer.TryWrite(proposal);
        }

        return true;
    }

    public void ReleaseSlot() => concurrencySlots.Release();

    public void Dispose() => concurrencySlots.Dispose();
}
