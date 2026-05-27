namespace InfraGate.Approvals.Plan;

public sealed record class PlanStatusResult(PlanStatus Status, string? ApprovalUrl = null);

public enum PlanStatus
{
    NotFound,
    ApprovalRequired,
    Approved,
    Applied,
    Expired
}
