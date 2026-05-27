namespace InfraGate.McpGateway;

internal sealed record class GatewayApprovalIdentity(string Subject, string DisplayName, string? AuthenticationType);
