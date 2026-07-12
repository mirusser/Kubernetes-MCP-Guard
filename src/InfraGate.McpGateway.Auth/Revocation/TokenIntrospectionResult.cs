namespace InfraGate.McpGateway.Auth;

internal sealed record class TokenIntrospectionResult(bool IsActive, DateTimeOffset? ExpiresAt)
{
    public static TokenIntrospectionResult Inactive { get; } = new(false, ExpiresAt: null);

    public static TokenIntrospectionResult Active(DateTimeOffset? expiresAt = null) => new(true, expiresAt);
}
