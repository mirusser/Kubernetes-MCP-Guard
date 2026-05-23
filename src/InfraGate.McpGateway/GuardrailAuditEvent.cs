namespace InfraGate.McpGateway;

public sealed record class GuardrailAuditEvent(
    string ToolName,
    string Direction,
    string Action,
    string[] Categories,
    string? PlanId,
    string? Subject,
    string? AuthenticationType,
    string IdentityKind = "Human");
