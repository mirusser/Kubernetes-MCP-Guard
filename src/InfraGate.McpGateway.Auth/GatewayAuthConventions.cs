namespace InfraGate.McpGateway.Auth;

public static class GatewayAuthConventions
{
    public const string DefaultOAuthResource = "http://127.0.0.1:3001/mcp";
    public const string DefaultOAuthScope = "mcp:tools";
    public const string AuthorizationScheme = "Bearer";

    public static class EnvironmentVariables
    {
        public const string BearerToken = "INFRA_GATE_GATEWAY_BEARER_TOKEN";
        public const string OAuthAuthority = "INFRA_GATE_OAUTH_AUTHORITY";
        public const string OAuthResource = "INFRA_GATE_OAUTH_RESOURCE";
        public const string OAuthScope = "INFRA_GATE_OAUTH_SCOPE";
        public const string OAuthRequireHttpsMetadata = "INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA";
    }

    public static class Schemes
    {
        public const string PolicyName = "InfraGateMcpGateway";
        public const string PolicyScheme = "InfraGateGatewayBearer";
        public const string StaticBearer = "InfraGateStaticBearer";
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
        public const string LocalBearerSubject = "local-bearer-demo";
        public const string StaticBearerAuthenticationType = "static-bearer";
        public const string OAuthAuthenticationType = "oauth-jwt";
    }
}
