namespace InfraGate.RunProfiles;

internal static class RunProfileConventions
{
    public const string DefaultConfigPath = "deploy/run-profiles.yaml";

    public static class Commands
    {
        public const string Generate = "generate";
        public const string List = "list";
        public const string Validate = "validate";
    }

    public static class Options
    {
        public const string Config = "--config";
        public const string Force = "--force";
        public const string Output = "--output";
        public const string Set = "--set";
    }

    public static class GeneratedFile
    {
        public const string HeaderLinePrefix = "# Generated from ";
        public const string ProfileMarker = " profile: ";
        public const string DoNotEditLinePrefix = "# Do not edit. Run: dotnet run --project src/InfraGate.RunProfiles -- generate ";
    }

    public static class YamlKeys
    {
        public const string AllowedNamespaces = "allowedNamespaces";
        public const string ApprovalAuthority = "approvalAuthority";
        public const string ApprovalRoot = "approvalRoot";
        public const string AspnetcoreUrls = "aspnetcoreUrls";
        public const string Audience = "audience";
        public const string Authority = "authority";
        public const string BaseUrl = "baseUrl";
        public const string BindAddress = "bindAddress";
        public const string BindPort = "bindPort";
        public const string DataProtectionHostPath = "dataProtectionHostPath";
        public const string Defaults = "defaults";
        public const string DomainAdapters = "domainAdapters";
        public const string DownstreamAssembly = "downstreamAssembly";
        public const string DownstreamAuth = "downstreamAuth";
        public const string Gateway = "gateway";
        public const string GatewayClientId = "gatewayClientId";
        public const string GatewayClientSecret = "gatewayClientSecret";
        public const string GatewayImage = "gatewayImage";
        public const string GenericApprovalCore = "genericApprovalCore";
        public const string GuardAuditHostPath = "guardAuditHostPath";
        public const string GuardAuditRoot = "guardAuditRoot";
        public const string Host = "host";
        public const string IdentityProvider = "identityProvider";
        public const string ApprovalHostPath = "approvalHostPath";
        public const string KubeconfigHostPath = "kubeconfigHostPath";
        public const string Kind = "kind";
        public const string KubeConfig = "kubeconfig";
        public const string Kubernetes = "kubernetes";
        public const string MetadataAddress = "metadataAddress";
        public const string Name = "name";
        public const string OauthAuthorizationEndpoint = "oauthAuthorizationEndpoint";
        public const string OauthCallbackPath = "oauthCallbackPath";
        public const string OauthClientId = "oauthClientId";
        public const string OauthTokenEndpoint = "oauthTokenEndpoint";
        public const string Profiles = "profiles";
        public const string RealmImport = "realmImport";
        public const string Required = "required";
        public const string RequireHttpsMetadata = "requireHttpsMetadata";
        public const string Resource = "resource";
        public const string RuntimeMode = "runtimeMode";
        public const string Scope = "scope";
        public const string Type = "type";
        public const string Version = "version";
    }

    public static class Env
    {
        public const string ApprovalBaseUrl = "INFRA_GATE_APPROVAL_BASE_URL";
        public const string ApprovalHostPath = "INFRA_GATE_APPROVAL_HOST_PATH";
        public const string ApprovalOauthAuthorizationEndpoint = "INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT";
        public const string ApprovalOauthCallbackPath = "INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH";
        public const string ApprovalOauthClientId = "INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID";
        public const string ApprovalOauthTokenEndpoint = "INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT";
        public const string ApprovalRoot = "K8S_MCP_APPROVAL_ROOT";
        public const string AllowedNamespaces = "K8S_MCP_ALLOWED_NAMESPACES";
        public const string AspnetcoreUrls = "ASPNETCORE_URLS";
        public const string BindAddress = "INFRA_GATE_BIND_ADDRESS";
        public const string BindPort = "INFRA_GATE_BIND_PORT";
        public const string DataProtectionHostPath = "INFRA_GATE_DATA_PROTECTION_HOST_PATH";
        public const string DownstreamAssembly = "INFRA_GATE_DOWNSTREAM_ASSEMBLY";
        public const string DownstreamAuthAudience = "INFRA_GATE_DOWNSTREAM_AUTH_AUDIENCE";
        public const string DownstreamAuthAuthority = "INFRA_GATE_DOWNSTREAM_AUTH_AUTHORITY";
        public const string DownstreamAuthGatewayClientId = "INFRA_GATE_DOWNSTREAM_AUTH_GATEWAY_CLIENT_ID";
        public const string DownstreamAuthGatewayClientSecret = "INFRA_GATE_DOWNSTREAM_AUTH_GATEWAY_CLIENT_SECRET";
        public const string DownstreamAuthMetadataAddress = "INFRA_GATE_DOWNSTREAM_AUTH_METADATA_ADDRESS";
        public const string DownstreamAuthRequired = "INFRA_GATE_DOWNSTREAM_AUTH_REQUIRED";
        public const string DownstreamAuthRequireHttpsMetadata = "INFRA_GATE_DOWNSTREAM_AUTH_REQUIRE_HTTPS_METADATA";
        public const string DownstreamAuthScope = "INFRA_GATE_DOWNSTREAM_AUTH_SCOPE";
        public const string GatewayImage = "INFRA_GATE_GATEWAY_IMAGE";
        public const string GuardAuditHostPath = "INFRA_GATE_GUARD_AUDIT_HOST_PATH";
        public const string GuardAuditRoot = "INFRA_GATE_GUARD_AUDIT_ROOT";
        public const string InfraGateEnvironment = "INFRA_GATE_ENVIRONMENT";
        public const string KubeconfigHostPath = "INFRA_GATE_KUBECONFIG_HOST_PATH";
        public const string KubeConfig = "KUBECONFIG";
        public const string OauthAuthority = "INFRA_GATE_OAUTH_AUTHORITY";
        public const string OauthMetadataAddress = "INFRA_GATE_OAUTH_METADATA_ADDRESS";
        public const string OauthRequireHttpsMetadata = "INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA";
        public const string OauthResource = "INFRA_GATE_OAUTH_RESOURCE";
        public const string OauthScope = "INFRA_GATE_OAUTH_SCOPE";
    }

    public static class DomainAdapterTypes
    {
        public const string Kubernetes = "kubernetes";
    }
}
