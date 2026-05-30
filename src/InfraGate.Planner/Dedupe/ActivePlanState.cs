namespace InfraGate.Planner.Dedupe;

internal sealed class ActivePlanState
{
    public ActivePlanState(string anomalyId, string planId, DateTimeOffset proposedAt, DateTimeOffset expiresAt)
    {
        AnomalyId = anomalyId;
        PlanId = planId;
        ProposedAt = proposedAt;
        ExpiresAt = expiresAt;
        LastAccessedAt = proposedAt;
    }

    public string AnomalyId { get; }
    public string PlanId { get; }
    public DateTimeOffset ProposedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public DateTimeOffset LastAccessedAt { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
}
