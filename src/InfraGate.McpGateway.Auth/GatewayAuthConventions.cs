namespace InfraGate.McpGateway.Auth;

public static class GatewayAuthConventions
{
    public const string DefaultOAuthResource = "http://127.0.0.1:3001/mcp";
    public const string DefaultOAuthScope = "mcp:tools";
    public const string DefaultReadOnlyOAuthScope = "mcp:tools.readonly";
    public const string DefaultProposeOAuthScope = "mcp:tools.propose";
    public const string DefaultExecuteOAuthScope = "mcp:tools.execute";
    public const string DefaultReadToolsOAuthScope = "mcp:tools.read";
    public const string DefaultWriteToolsOAuthScope = "mcp:tools.write";
    public const string DefaultApprovalOAuthClientId = "infra-gate-approval-ui";
    public const int DefaultTokenIntrospectionCacheSeconds = 30;
    public const int DefaultMaxAcceptedAccessTokenLifetimeSeconds = 300;
    public static readonly TimeSpan DefaultJwksAutomaticRefreshInterval = TimeSpan.FromSeconds(300);
    public static readonly TimeSpan DefaultJwksMinimumRefreshInterval = TimeSpan.FromSeconds(60);
    public const string AuthorizationScheme = "Bearer";

    public static class EnvironmentVariables
    {
        public const string OAuthAuthority = "InfraGate__Auth__OAuthAuthority";
        public const string OAuthMetadataAddress = "InfraGate__Auth__OAuthMetadataAddress";
        public const string OAuthResource = "InfraGate__Auth__OAuthResource";
        public const string OAuthScope = "InfraGate__Auth__OAuthScope";
        public const string OAuthRequireHttpsMetadata = "InfraGate__Auth__OAuthRequireHttpsMetadata";
        public const string ApprovalOAuthClientId = "InfraGate__Auth__ApprovalOAuthClientId";
        public const string ApprovalOAuthAuthorizationEndpoint = "InfraGate__Auth__ApprovalOAuthAuthorizationEndpoint";
        public const string ApprovalOAuthTokenEndpoint = "InfraGate__Auth__ApprovalOAuthTokenEndpoint";
        public const string ApprovalOAuthCallbackPath = "InfraGate__Auth__ApprovalOAuthCallbackPath";
        public const string RequireDPoP = "InfraGate__Auth__RequireDPoP";
        public const string TokenIntrospectionEnabled = "InfraGate__Auth__TokenIntrospectionEnabled";
        public const string TokenIntrospectionEndpoint = "InfraGate__Auth__TokenIntrospectionEndpoint";
        public const string TokenIntrospectionClientId = "InfraGate__Auth__TokenIntrospectionClientId";
        public const string TokenIntrospectionClientSecret = "InfraGate__Auth__TokenIntrospectionClientSecret";
        public const string TokenIntrospectionCacheSeconds = "InfraGate__Auth__TokenIntrospectionCacheSeconds";
        public const string MaxAcceptedAccessTokenLifetimeSeconds = "InfraGate__Auth__MaxAcceptedAccessTokenLifetimeSeconds";
    }

    public static class ConfigurationKeys
    {
        public const string OAuthAuthority = "InfraGate:Auth:OAuthAuthority";
        public const string OAuthMetadataAddress = "InfraGate:Auth:OAuthMetadataAddress";
        public const string OAuthResource = "InfraGate:Auth:OAuthResource";
        public const string OAuthScope = "InfraGate:Auth:OAuthScope";
        public const string OAuthRequireHttpsMetadata = "InfraGate:Auth:OAuthRequireHttpsMetadata";
        public const string ApprovalOAuthClientId = "InfraGate:Auth:ApprovalOAuthClientId";
        public const string ApprovalOAuthAuthorizationEndpoint = "InfraGate:Auth:ApprovalOAuthAuthorizationEndpoint";
        public const string ApprovalOAuthTokenEndpoint = "InfraGate:Auth:ApprovalOAuthTokenEndpoint";
        public const string ApprovalOAuthCallbackPath = "InfraGate:Auth:ApprovalOAuthCallbackPath";
        public const string RequireDPoP = "InfraGate:Auth:RequireDPoP";
        public const string TokenIntrospectionEnabled = "InfraGate:Auth:TokenIntrospectionEnabled";
        public const string TokenIntrospectionEndpoint = "InfraGate:Auth:TokenIntrospectionEndpoint";
        public const string TokenIntrospectionClientId = "InfraGate:Auth:TokenIntrospectionClientId";
        public const string TokenIntrospectionClientSecret = "InfraGate:Auth:TokenIntrospectionClientSecret";
        public const string TokenIntrospectionCacheSeconds = "InfraGate:Auth:TokenIntrospectionCacheSeconds";
        public const string MaxAcceptedAccessTokenLifetimeSeconds = "InfraGate:Auth:MaxAcceptedAccessTokenLifetimeSeconds";
    }

    public static class Schemes
    {
        public const string PolicyName = "InfraGateMcpGateway";
        public const string ApprovalPolicyName = "InfraGateApprovalUi";
        public const string PolicyScheme = "InfraGateGatewayBearer";
        public const string ApprovalCookie = "InfraGateApprovalCookie";
        public const string ApprovalOAuth = "InfraGateApprovalOAuth";
    }

    public static class Metadata
    {
        public const string ProtectedResourcePath = "/.well-known/oauth-protected-resource";
        public const string ResourceName = "InfraGate MCP Gateway";
    }

    public static class OAuthErrors
    {
        public const string InsufficientScope = "insufficient_scope";
    }

    public static class ChallengeParameters
    {
        public const string Error = "error";
        public const string Scope = "scope";
        public const string ResourceMetadata = "resource_metadata";
    }

    public static class Claims
    {
        public const string Scope = "scope";
        public const string Scp = "scp";
        public const string PreferredUsername = "preferred_username";
        public const string Email = "email";
        public const string Subject = "sub";
        public const string ClientId = "client_id";
        public const string AuthorizedParty = "azp";
        public const string Groups = "groups";
        public const string Expiration = "exp";
        public const string IssuedAt = "iat";
        public const string NotBefore = "nbf";
    }

    public static class Audit
    {
        public const string OAuthAuthenticationType = "oauth-jwt";
        public const string HumanIdentityKind = "Human";
        public const string ServiceIdentityKind = "Service";
    }

    public static class ServiceClients
    {
        public const string ObserverClientId = "infra-gate-observer";
        public const string PlannerClientId = "infra-gate-planner";
        public const string ExecutorClientId = "infra-gate-executor";
    }

    public static class Approvals
    {
        public const string DefaultCallbackPath = "/approvals/oauth/callback";
        public const string LoginPath = "/approvals/login";
        public const string CookieName = "InfraGate.Approval";
        public const string PublicClientSecretPlaceholder = "public-client";
        public const string AuthorizePath = "/authorize";
        public const string TokenPath = "/token";
    }

    public static class Parameters
    {
        public const string Resource = "resource";
    }

    public static class HttpClients
    {
        public const string TokenIntrospectionClient = "InfraGate.TokenIntrospection";
    }

    public static class TokenIntrospection
    {
        public const string TokenFormFieldName = "token";
        public const string ActiveResponsePropertyName = "active";
        public const string OpenIdConnectDiscoveryPath = "/.well-known/openid-configuration";
        public const string IntrospectionEndpointMetadataName = "introspection_endpoint";
        public const string BasicAuthenticationScheme = "Basic";
    }

    public static class DPoP
    {
        public const string ProofHeaderName = "DPoP";
        public const string Scheme = "DPoP";
        public const string ProofTyp = "dpop+jwt";
        public const string CnfClaim = "cnf";
        public const string JktClaim = "jkt";
    }
}
