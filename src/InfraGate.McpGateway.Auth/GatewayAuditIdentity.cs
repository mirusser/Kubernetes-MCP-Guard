namespace InfraGate.McpGateway.Auth;

public sealed record class GatewayAuditIdentity(
    string? Subject,
    string? AuthenticationType,
    string IdentityKind = GatewayAuthConventions.Audit.HumanIdentityKind);
