namespace InfraGate.McpGateway.Auth;

public sealed record GatewayAuditIdentity(string? Subject, string? AuthenticationType);
