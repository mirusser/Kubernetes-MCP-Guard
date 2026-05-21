namespace InfraGate.DownstreamAuth;

public static class DownstreamAuthConventions
{
    public const string MetaKey = "io.infragate.downstream.authorization";
    public const string BootstrapLineKey = MetaKey;
    public const string BearerPrefix = "Bearer ";

    public static class ErrorCodes
    {
        public const string DownstreamAuthRequired = "downstream_auth_required";
    }

    public static class Defaults
    {
        public const string Scope = "mcp:downstream";
        public const string Audience = "urn:infra-gate:mcp-server";
        public static readonly TimeSpan GatewayRefreshSkew = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan ServerClockSkew = TimeSpan.FromSeconds(30);
    }

    public static class EnvironmentVariables
    {
        public const string Required = "INFRA_GATE_DOWNSTREAM_AUTH_REQUIRED";
        public const string Authority = "INFRA_GATE_DOWNSTREAM_AUTH_AUTHORITY";
        public const string MetadataAddress = "INFRA_GATE_DOWNSTREAM_AUTH_METADATA_ADDRESS";
        public const string RequireHttpsMetadata = "INFRA_GATE_DOWNSTREAM_AUTH_REQUIRE_HTTPS_METADATA";
        public const string Audience = "INFRA_GATE_DOWNSTREAM_AUTH_AUDIENCE";
        public const string Scope = "INFRA_GATE_DOWNSTREAM_AUTH_SCOPE";
        public const string GatewayClientId = "INFRA_GATE_DOWNSTREAM_AUTH_GATEWAY_CLIENT_ID";
        public const string GatewayClientSecret = "INFRA_GATE_DOWNSTREAM_AUTH_GATEWAY_CLIENT_SECRET";
    }

    public static class ConfigurationKeys
    {
        public const string Required = "InfraGate:DownstreamAuth:Required";
        public const string Authority = "InfraGate:DownstreamAuth:Authority";
        public const string MetadataAddress = "InfraGate:DownstreamAuth:MetadataAddress";
        public const string RequireHttpsMetadata = "InfraGate:DownstreamAuth:RequireHttpsMetadata";
        public const string Audience = "InfraGate:DownstreamAuth:Audience";
        public const string Scope = "InfraGate:DownstreamAuth:Scope";
        public const string GatewayClientId = "InfraGate:DownstreamAuth:GatewayClientId";
        public const string GatewayClientSecret = "InfraGate:DownstreamAuth:GatewayClientSecret";
    }
}
