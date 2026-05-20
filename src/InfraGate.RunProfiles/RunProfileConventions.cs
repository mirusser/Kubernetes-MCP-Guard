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
        public const string Format = "--format";
        public const string Force = "--force";
        public const string Output = "--output";
        public const string Set = "--set";
    }

    public static class Formats
    {
        public const string AppSettings = "appsettings";
        public const string Env = "env";
    }

    public static class GeneratedFile
    {
        public const string HeaderLinePrefix = "# Generated from ";
        public const string ProfileMarker = " profile: ";
        public const string DoNotEditLinePrefix = "# Do not edit. Run: dotnet run --project src/InfraGate.RunProfiles -- generate ";
        public const string MetadataSection = "_generated";
        public const string MetadataProfile = "profile";
        public const string MetadataSource = "source";
    }

    public static class AppSettings
    {
        public const string Root = "InfraGate";
        public const string Runtime = "Runtime";
        public const string Gateway = "Gateway";
        public const string Auth = "Auth";
        public const string Approval = "Approval";
        public const string Kubernetes = "Kubernetes";
        public const string AllowedNamespaces = "AllowedNamespaces";
        public const string ApprovalOAuthAuthorizationEndpoint = "ApprovalOAuthAuthorizationEndpoint";
        public const string ApprovalOAuthCallbackPath = "ApprovalOAuthCallbackPath";
        public const string ApprovalOAuthClientId = "ApprovalOAuthClientId";
        public const string ApprovalOAuthTokenEndpoint = "ApprovalOAuthTokenEndpoint";
        public const string AspNetCoreUrls = "AspNetCoreUrls";
        public const string BaseUrl = "BaseUrl";
        public const string DownstreamAssembly = "DownstreamAssembly";
        public const string Environment = "Environment";
        public const string GuardAuditRoot = "GuardAuditRoot";
        public const string KubeConfig = "KubeConfig";
        public const string OAuthAuthority = "OAuthAuthority";
        public const string OAuthMetadataAddress = "OAuthMetadataAddress";
        public const string OAuthRequireHttpsMetadata = "OAuthRequireHttpsMetadata";
        public const string OAuthResource = "OAuthResource";
        public const string OAuthScope = "OAuthScope";
        public const string RootPath = "Root";
    }

    public static class YamlKeys
    {
        public const string AllowedNamespaces = "allowedNamespaces";
        public const string ApprovalAuthority = "approvalAuthority";
        public const string ApprovalRoot = "approvalRoot";
        public const string AspnetcoreUrls = "aspnetcoreUrls";
        public const string Authority = "authority";
        public const string BaseUrl = "baseUrl";
        public const string BindAddress = "bindAddress";
        public const string BindPort = "bindPort";
        public const string ConfigHostPath = "configHostPath";
        public const string DataProtectionHostPath = "dataProtectionHostPath";
        public const string Defaults = "defaults";
        public const string DomainAdapters = "domainAdapters";
        public const string DownstreamAssembly = "downstreamAssembly";
        public const string Gateway = "gateway";
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
        public const string ConfigHostPath = "INFRA_GATE_CONFIG_HOST_PATH";
        public const string ConfigPath = "INFRA_GATE_CONFIG_PATH";
        public const string DataProtectionHostPath = "INFRA_GATE_DATA_PROTECTION_HOST_PATH";
        public const string DownstreamAssembly = "INFRA_GATE_DOWNSTREAM_ASSEMBLY";
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

    public static class RuntimeConfig
    {
        public const string ContainerPath = "/app/config/appsettings.InfraGate.json";
    }
}
