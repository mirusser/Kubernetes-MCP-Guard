using InfraGate.Approvals.AuditPayloads;

namespace InfraGate.Approvals;

public sealed record PlanAudit(string EventName, IPlanAuditPayload Payload);
