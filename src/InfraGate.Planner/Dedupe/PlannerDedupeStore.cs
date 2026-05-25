namespace InfraGate.Planner.Dedupe;

internal sealed class PlannerDedupeStore
{
    private const int Capacity = 1000;

    private readonly ConcurrentDictionary<string, ActivePlanState> activePlans = new(StringComparer.Ordinal);

    public bool HasActivePlan(string anomalyId)
    {
        if (activePlans.TryGetValue(anomalyId, out var state))
        {
            state.LastAccessedAt = DateTimeOffset.UtcNow;
            return true;
        }

        return false;
    }

    public void TrackActivePlan(string anomalyId, string planId, DateTimeOffset proposedAt)
    {
        activePlans[anomalyId] = new ActivePlanState(anomalyId, planId, proposedAt);

        if (activePlans.Count < Capacity)
        {
            return;
        }

        var leastRecentlyUsed = activePlans.Values.MinBy(static plan => plan.LastAccessedAt);
        if (leastRecentlyUsed is not null)
        {
            activePlans.TryRemove(leastRecentlyUsed.AnomalyId, out _);
        }
    }

    public void Remove(string anomalyId)
    {
        activePlans.TryRemove(anomalyId, out _);
    }
}
