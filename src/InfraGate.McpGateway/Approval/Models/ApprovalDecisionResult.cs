namespace InfraGate.McpGateway;

public sealed record class ApprovalDecisionResult(bool Succeeded, string Message, string? ReasonCode = null);
