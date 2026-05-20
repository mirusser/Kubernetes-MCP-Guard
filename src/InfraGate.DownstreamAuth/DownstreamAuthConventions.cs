namespace InfraGate.DownstreamAuth;

public static class DownstreamAuthConventions
{
    public const string MetaKey = "io.infragate.downstream.authorization";
    public const string BootstrapLineKey = "io.infragate.downstream.authorization";
    public const string BearerScheme = "Bearer ";

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
}
