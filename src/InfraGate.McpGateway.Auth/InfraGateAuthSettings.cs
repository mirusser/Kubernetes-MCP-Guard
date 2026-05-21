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
}
