namespace InfraGate.McpGateway;

public sealed record ApprovalDecisionResult(bool Succeeded, string Message, string? ReasonCode = null);
