namespace InfraGate.McpGateway;

internal sealed record GatewayApprovalIdentity(string Subject, string DisplayName, string? AuthenticationType);
