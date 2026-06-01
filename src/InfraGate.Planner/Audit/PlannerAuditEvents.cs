namespace InfraGate.Planner.Audit;

internal static class PlannerAuditEvents
{
    public const string HandoffReceived = "handoff.received";
    public const string ProposalSkipped = "proposal.skipped";
    public const string ProposePlanSucceeded = "propose_plan.succeeded";
    public const string ProposePlanFailed = "propose_plan.failed";
    public const string DecisionNoOutput = "decision.no_output";
    public const string DecisionTimedOut = "decision.timed_out";
    public const string DecisionInvalidOperation = "decision.invalid_operation";
    public const string DecisionInvalidArguments = "decision.invalid_arguments";
}
