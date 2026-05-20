namespace InfraGate.Approvals;

public sealed record class ApprovalPlanResult(PlanEnvelope Envelope, string PendingPath, string Hash);
