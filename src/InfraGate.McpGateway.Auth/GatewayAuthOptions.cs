using Microsoft.Extensions.Configuration;

namespace InfraGate.McpGateway.Auth;

public sealed record class GatewayAuthOptions(
    string OAuthAuthority,
    string OAuthResource = GatewayAuthConventions.DefaultOAuthResource,
    string OAuthScope = GatewayAuthConventions.DefaultOAuthScope,
    bool OAuthRequireHttpsMetadata = true,
    string? OAuthMetadataAddress = null,
    string ApprovalOAuthClientId = GatewayAuthConventions.DefaultApprovalOAuthClientId,
    string? ApprovalOAuthAuthorizationEndpoint = null,
    string? ApprovalOAuthTokenEndpoint = null,
    string ApprovalOAuthCallbackPath = GatewayAuthConventions.Approvals.DefaultCallbackPath,
    bool RequireDPoP = false,
    bool TokenIntrospectionEnabled = false,
    string? TokenIntrospectionEndpoint = null,
    string? TokenIntrospectionClientId = null,
    string? TokenIntrospectionClientSecret = null,
    int TokenIntrospectionCacheSeconds = GatewayAuthConventions.DefaultTokenIntrospectionCacheSeconds,
    int MaxAcceptedAccessTokenLifetimeSeconds = GatewayAuthConventions.DefaultMaxAcceptedAccessTokenLifetimeSeconds)
{
    public string ApprovalAuthorizationEndpoint =>
        string.IsNullOrWhiteSpace(ApprovalOAuthAuthorizationEndpoint)
            ? TrimTrailingSlash(OAuthAuthority) + GatewayAuthConventions.Approvals.AuthorizePath
            : ApprovalOAuthAuthorizationEndpoint;

    public string ApprovalTokenEndpoint =>
        string.IsNullOrWhiteSpace(ApprovalOAuthTokenEndpoint)
            ? TrimTrailingSlash(OAuthAuthority) + GatewayAuthConventions.Approvals.TokenPath
            : ApprovalOAuthTokenEndpoint;

    public static GatewayAuthOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var authSettings = configuration
            .GetSection("InfraGate:Auth")
            .Get<InfraGateAuthSettings>();

        string oauthAuthority = authSettings?.OAuthAuthority ??
            throw new InvalidOperationException(
                $"{GatewayAuthConventions.EnvironmentVariables.OAuthAuthority} is required.");
        string oauthResource = authSettings?.OAuthResource ??
            GatewayAuthConventions.DefaultOAuthResource;
        string oauthScope = authSettings?.OAuthScope ??
            GatewayAuthConventions.DefaultOAuthScope;
        bool requireHttpsMetadata = authSettings?.OAuthRequireHttpsMetadata ?? true;
        string? oauthMetadataAddress = authSettings?.OAuthMetadataAddress;
        string approvalClientId = authSettings?.ApprovalOAuthClientId ??
            GatewayAuthConventions.DefaultApprovalOAuthClientId;
        string? approvalAuthorizationEndpoint = authSettings?.ApprovalOAuthAuthorizationEndpoint;
        string? approvalTokenEndpoint = authSettings?.ApprovalOAuthTokenEndpoint;
        string approvalCallbackPath = authSettings?.ApprovalOAuthCallbackPath ??
            GatewayAuthConventions.Approvals.DefaultCallbackPath;
        bool requireDPoP = authSettings?.RequireDPoP ?? false;
        bool tokenIntrospectionEnabled = authSettings?.TokenIntrospectionEnabled ?? false;
        string? tokenIntrospectionEndpoint = authSettings?.TokenIntrospectionEndpoint;
        string? tokenIntrospectionClientId = authSettings?.TokenIntrospectionClientId;
        string? tokenIntrospectionClientSecret = authSettings?.TokenIntrospectionClientSecret;
        int tokenIntrospectionCacheSeconds = authSettings?.TokenIntrospectionCacheSeconds ??
            GatewayAuthConventions.DefaultTokenIntrospectionCacheSeconds;
        int maxAcceptedAccessTokenLifetimeSeconds = authSettings?.MaxAcceptedAccessTokenLifetimeSeconds ??
            GatewayAuthConventions.DefaultMaxAcceptedAccessTokenLifetimeSeconds;

        return new GatewayAuthOptions(
            oauthAuthority,
            oauthResource,
            oauthScope,
            requireHttpsMetadata,
            oauthMetadataAddress,
            approvalClientId,
            approvalAuthorizationEndpoint,
            approvalTokenEndpoint,
            approvalCallbackPath,
            RequireDPoP: requireDPoP,
            TokenIntrospectionEnabled: tokenIntrospectionEnabled,
            TokenIntrospectionEndpoint: tokenIntrospectionEndpoint,
            TokenIntrospectionClientId: tokenIntrospectionClientId,
            TokenIntrospectionClientSecret: tokenIntrospectionClientSecret,
            TokenIntrospectionCacheSeconds: tokenIntrospectionCacheSeconds,
            MaxAcceptedAccessTokenLifetimeSeconds: maxAcceptedAccessTokenLifetimeSeconds);
    }

    private static string TrimTrailingSlash(string value) => value.TrimEnd('/');
}
