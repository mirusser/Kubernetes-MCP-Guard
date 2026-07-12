namespace InfraGate.McpGateway.Auth;

public sealed record class InfraGateAuthSettings
{
    public string? OAuthAuthority { get; init; }
    public string? OAuthMetadataAddress { get; init; }
    public string? OAuthResource { get; init; }
    public string? OAuthScope { get; init; }
    public bool? OAuthRequireHttpsMetadata { get; init; }
    public string? ApprovalOAuthClientId { get; init; }
    public string? ApprovalOAuthCallbackPath { get; init; }
    public string? ApprovalOAuthAuthorizationEndpoint { get; init; }
    public string? ApprovalOAuthTokenEndpoint { get; init; }
    public bool? RequireDPoP { get; init; }
    public bool? TokenIntrospectionEnabled { get; init; }
    public string? TokenIntrospectionEndpoint { get; init; }
    public string? TokenIntrospectionClientId { get; init; }
    public string? TokenIntrospectionClientSecret { get; init; }
    public int? TokenIntrospectionCacheSeconds { get; init; }
    public int? MaxAcceptedAccessTokenLifetimeSeconds { get; init; }
}
