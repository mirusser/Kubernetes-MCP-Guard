namespace InfraGate.McpGateway.Auth;

public static class GatewayAuthConventions
{
    public const string DefaultOAuthResource = "http://127.0.0.1:3001/mcp";
    public const string DefaultOAuthScope = "mcp:tools";
    public const string DefaultApprovalOAuthClientId = "infra-gate-approval-ui";
    public const string AuthorizationScheme = "Bearer";

    public static class EnvironmentVariables
    {
        public const string OAuthAuthority = "INFRA_GATE_OAUTH_AUTHORITY";
        public const string OAuthMetadataAddress = "INFRA_GATE_OAUTH_METADATA_ADDRESS";
        public const string OAuthResource = "INFRA_GATE_OAUTH_RESOURCE";
        public const string OAuthScope = "INFRA_GATE_OAUTH_SCOPE";
        public const string OAuthRequireHttpsMetadata = "INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA";
        public const string ApprovalOAuthClientId = "INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID";
        public const string ApprovalOAuthAuthorizationEndpoint = "INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT";
        public const string ApprovalOAuthTokenEndpoint = "INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT";
        public const string ApprovalOAuthCallbackPath = "INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH";
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
    }

    public static class Audit
    {
        public const string OAuthAuthenticationType = "oauth-jwt";
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
