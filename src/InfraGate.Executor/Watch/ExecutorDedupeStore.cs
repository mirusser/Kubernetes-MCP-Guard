namespace InfraGate.Executor.Watch;

internal sealed class ExecutorDedupeStore : IExecutorDedupeStore
{
    private const int Capacity = 1000;

    private readonly ConcurrentDictionary<string, ActiveExecutionState> activePlans = new(StringComparer.Ordinal);

    public bool TryTrack(string planId)
    {
        if (!activePlans.TryAdd(planId, new ActiveExecutionState(planId, DateTimeOffset.UtcNow)))
        {
            return false;
        }

        if (activePlans.Count > Capacity)
        {
            var lru = activePlans.Values.MinBy(static s => s.TrackedAt);
            if (lru is not null && !planId.Equals(lru.PlanId, StringComparison.Ordinal))
            {
                activePlans.TryRemove(lru.PlanId, out _);
            }
        }

        return true;
    }

    public void Remove(string planId) => activePlans.TryRemove(planId, out _);
}
