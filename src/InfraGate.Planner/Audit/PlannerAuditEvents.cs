namespace InfraGate.Planner.Audit;

internal static class PlannerAuditEvents
{
    public const string HandoffReceived = "handoff.received";
    public const string ProposalSkipped = "proposal.skipped";
    public const string ProposePlanSucceeded = "propose_plan.succeeded";
    public const string ProposePlanFailed = "propose_plan.failed";
}
