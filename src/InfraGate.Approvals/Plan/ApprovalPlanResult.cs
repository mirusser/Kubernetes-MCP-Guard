namespace InfraGate.Approvals.Plan;

public sealed record class ApprovalPlanResult(PlanEnvelope Envelope, string PendingPath, string Hash);
