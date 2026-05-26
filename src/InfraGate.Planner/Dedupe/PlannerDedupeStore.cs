namespace InfraGate.Planner.Dedupe;

internal sealed class PlannerDedupeStore
{
    private const int Capacity = 1000;

    private readonly ConcurrentDictionary<string, ActivePlanState> activePlans = new(StringComparer.Ordinal);

    public bool HasActivePlan(string anomalyId)
    {
        if (!activePlans.TryGetValue(anomalyId, out var state))
        {
            return false;
        }

        if (state.IsExpired)
        {
            activePlans.TryRemove(anomalyId, out _);
            return false;
        }

        state.LastAccessedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public void TrackActivePlan(string anomalyId, string planId, DateTimeOffset proposedAt, DateTimeOffset expiresAt)
    {
        activePlans[anomalyId] = new ActivePlanState(anomalyId, planId, proposedAt, expiresAt);

        if (activePlans.Count >= Capacity)
        {
            var leastRecentlyUsed = activePlans.Values.MinBy(static p => p.LastAccessedAt);
            if (leastRecentlyUsed is not null)
            {
                activePlans.TryRemove(leastRecentlyUsed.AnomalyId, out _);
            }
        }
    }

    public void Remove(string anomalyId)
    {
        activePlans.TryRemove(anomalyId, out _);
    }
}
