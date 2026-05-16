namespace InfraGate.Approvals;

public sealed record ApprovalPlanResult(PlanEnvelope Envelope, string PendingPath, string Hash);
