using InfraGate.Approvals.AuditPayloads;

namespace InfraGate.Approvals;

public sealed record class PlanAudit(string EventName, IPlanAuditPayload Payload);
