namespace InfraGate.DownstreamAuth;

public static class DownstreamAuthConventions
{
    public const string MetaKey = "io.infragate.downstream.authorization";
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
        public static readonly TimeSpan JwksAutomaticRefreshInterval = TimeSpan.FromSeconds(300);
        public static readonly TimeSpan JwksMinimumRefreshInterval = TimeSpan.FromSeconds(60);
    }

    public static class EnvironmentVariables
    {
        public const string Required = "InfraGate__DownstreamAuth__Required";
        public const string Authority = "InfraGate__DownstreamAuth__Authority";
        public const string MetadataAddress = "InfraGate__DownstreamAuth__MetadataAddress";
        public const string RequireHttpsMetadata = "InfraGate__DownstreamAuth__RequireHttpsMetadata";
        public const string Audience = "InfraGate__DownstreamAuth__Audience";
        public const string Scope = "InfraGate__DownstreamAuth__Scope";
        public const string GatewayClientId = "InfraGate__DownstreamAuth__GatewayClientId";
        public const string GatewayClientSecret = "InfraGate__DownstreamAuth__GatewayClientSecret";
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
