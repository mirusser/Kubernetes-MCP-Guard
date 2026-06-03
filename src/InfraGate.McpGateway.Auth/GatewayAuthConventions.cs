namespace InfraGate.McpGateway.Auth;

public static class GatewayAuthConventions
{
    public const string DefaultOAuthResource = "http://127.0.0.1:3001/mcp";
    public const string DefaultOAuthScope = "mcp:tools";
    public const string DefaultReadOnlyOAuthScope = "mcp:tools.readonly";
    public const string DefaultProposeOAuthScope = "mcp:tools.propose";
    public const string DefaultExecuteOAuthScope = "mcp:tools.execute";
    public const string DefaultApprovalOAuthClientId = "infra-gate-approval-ui";
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
}
