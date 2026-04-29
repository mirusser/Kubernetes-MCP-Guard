namespace InfraGate.McpGateway;

public sealed record GuardrailAuditEvent(
    string ToolName,
    string Direction,
    string Action,
    string[] Categories,
    string? PlanId,
    string? Subject,
    string? AuthenticationType);
