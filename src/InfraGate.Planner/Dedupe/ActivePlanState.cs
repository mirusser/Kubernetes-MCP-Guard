namespace InfraGate.Planner.Dedupe;

internal sealed class ActivePlanState
{
    public ActivePlanState(string anomalyId, string planId, DateTimeOffset proposedAt)
    {
        AnomalyId = anomalyId;
        PlanId = planId;
        ProposedAt = proposedAt;
        LastAccessedAt = proposedAt;
    }

    public string AnomalyId { get; }
    public string PlanId { get; }
    public DateTimeOffset ProposedAt { get; }
    public DateTimeOffset LastAccessedAt { get; set; }
}
