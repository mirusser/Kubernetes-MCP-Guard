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
        public const string AppSettingJson = "appsettings";
        public const string DotEnv = "env";
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
        public const string Observer = "Observer";
        public const string Planner = "Planner";
        public const string Executor = "Executor";
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
        public const string Postgres = "Postgres";
        public const string PostgresConnectionString = "ConnectionString";
        public const string RunMigrationsOnStartup = "RunMigrationsOnStartup";
        public const string ObserverCycleIntervalSeconds = "CycleIntervalSeconds";
        public const string ObserverCycleWallClockCapSeconds = "CycleWallClockCapSeconds";
        public const string ObserverFileSinkRoot = "FileSinkRoot";
        public const string ObserverGatewayBaseUrl = "GatewayBaseUrl";
        public const string ObserverLlmApiKey = "LlmApiKey";
        public const string ObserverLlmModel = "LlmModel";
        public const string ObserverLlmProvider = "LlmProvider";
        public const string ObserverMaxToolIterations = "MaxToolIterations";
        public const string ObserverPlannerHandoffUrl = "PlannerHandoffUrl";
        public const string ObserverTokenEndpoint = "TokenEndpoint";
        public const string ObserverClientId = "ClientId";
        public const string ObserverClientSecret = "ClientSecret";
        public const string ObserverScope = "Scope";
        public const string PlannerGatewayBaseUrl = "GatewayBaseUrl";
        public const string PlannerExecutorHandoffUrl = "ExecutorHandoffUrl";
        public const string PlannerTokenEndpoint = "TokenEndpoint";
        public const string PlannerClientId = "ClientId";
        public const string PlannerClientSecret = "ClientSecret";
        public const string PlannerLlmProvider = "LlmProvider";
        public const string PlannerLlmModel = "LlmModel";
        public const string PlannerLlmApiKey = "LlmApiKey";
        public const string PlannerAnomalyWallClockCapSeconds = "AnomalyWallClockCapSeconds";
        public const string PlannerBatchWallClockCapSeconds = "BatchWallClockCapSeconds";
        public const string PlannerMaxToolIterations = "MaxToolIterations";
        public const string PlannerFileSinkRoot = "FileSinkRoot";
        public const string ExecutorGatewayBaseUrl = "GatewayBaseUrl";
        public const string ExecutorTokenEndpoint = "TokenEndpoint";
        public const string ExecutorClientId = "ClientId";
        public const string ExecutorClientSecret = "ClientSecret";
        public const string ExecutorConcurrencyCap = "ConcurrencyCap";
        public const string ExecutorWatchTimeoutSeconds = "WatchTimeoutSeconds";
    }

    public static class YamlKeys
    {
        public const string AllowedNamespaces = "allowedNamespaces";
        public const string ApprovalAuthority = "approvalAuthority";
        public const string ApprovalRoot = "approvalRoot";
        public const string PostgresConnectionString = "postgresConnectionString";
        public const string RunMigrationsOnStartup = "runMigrationsOnStartup";
        public const string AspnetcoreUrls = "aspnetcoreUrls";
        public const string Audience = "audience";
        public const string Authority = "authority";
        public const string BaseUrl = "baseUrl";
        public const string BindAddress = "bindAddress";
        public const string BindPort = "bindPort";
        public const string ConfigHostPath = "configHostPath";
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
        public const string Observer = "observer";
        public const string Planner = "planner";
        public const string Executor = "executor";
        public const string ExecutorHandoffUrl = "executorHandoffUrl";
        public const string PlannerHandoffUrl = "plannerHandoffUrl";
        public const string AnomalyWallClockCapSeconds = "anomalyWallClockCapSeconds";
        public const string BatchWallClockCapSeconds = "batchWallClockCapSeconds";
        public const string ConcurrencyCap = "concurrencyCap";
        public const string WatchTimeoutSeconds = "watchTimeoutSeconds";
        public const string PlannerHostPath = "plannerHostPath";
        public const string ExecutorHostPath = "executorHostPath";
        public const string OAuthAuthority = "oauthAuthority";
        public const string Profiles = "profiles";
        public const string RealmImport = "realmImport";
        public const string Required = "required";
        public const string RequireHttpsMetadata = "requireHttpsMetadata";
        public const string Resource = "resource";
        public const string RuntimeMode = "runtimeMode";
        public const string Scope = "scope";
        public const string Type = "type";
        public const string Version = "version";
        public const string ClientId = "clientId";
        public const string ClientSecret = "clientSecret";
        public const string CycleCadenceSeconds = "cycleCadenceSeconds";
        public const string CycleWallClockCapSeconds = "cycleWallClockCapSeconds";
        public const string FileSinkRoot = "fileSinkRoot";
        public const string GatewayBaseUrl = "gatewayBaseUrl";
        public const string LlmApiKey = "llmApiKey";
        public const string LlmModel = "llmModel";
        public const string LlmProvider = "llmProvider";
        public const string MaxToolIterations = "maxToolIterations";
        public const string ObserverHostPath = "observerHostPath";
        public const string TokenEndpoint = "tokenEndpoint";
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
        public const string ObserverAspnetcoreUrls = "INFRA_GATE_OBSERVER_ASPNETCORE_URLS";
        public const string ObserverClientId = "INFRA_GATE_OBSERVER_CLIENT_ID";
        public const string ObserverClientSecret = "INFRA_GATE_OBSERVER_CLIENT_SECRET";
        public const string ObserverCycleIntervalSeconds = "INFRA_GATE_OBSERVER_CYCLE_INTERVAL_SECONDS";
        public const string ObserverCycleWallClockCapSeconds = "INFRA_GATE_OBSERVER_WALL_CLOCK_CAP_SECONDS";
        public const string ObserverFileSinkRoot = "INFRA_GATE_OBSERVER_FILE_SINK_ROOT";
        public const string ObserverGatewayBaseUrl = "INFRA_GATE_OBSERVER_GATEWAY_BASE_URL";
        public const string ObserverHostPath = "INFRA_GATE_OBSERVER_HOST_PATH";
        public const string ObserverLlmApiKey = "INFRA_GATE_OBSERVER_LLM_API_KEY";
        public const string ObserverLlmModel = "INFRA_GATE_OBSERVER_LLM_MODEL";
        public const string ObserverLlmProvider = "INFRA_GATE_OBSERVER_LLM_PROVIDER";
        public const string ObserverMaxToolIterations = "INFRA_GATE_OBSERVER_MAX_TOOL_ITERATIONS";
        public const string ObserverOAuthAuthority = "INFRA_GATE_OBSERVER_OAUTH_AUTHORITY";
        public const string ObserverPlannerHandoffUrl = "INFRA_GATE_OBSERVER_PLANNER_HANDOFF_URL";
        public const string ObserverScope = "INFRA_GATE_OBSERVER_OAUTH_SCOPE";
        public const string ObserverTokenEndpoint = "INFRA_GATE_OBSERVER_TOKEN_ENDPOINT";
        public const string PlannerAspnetcoreUrls = "INFRA_GATE_PLANNER_ASPNETCORE_URLS";
        public const string PlannerGatewayBaseUrl = "INFRA_GATE_PLANNER_GATEWAY_BASE_URL";
        public const string PlannerExecutorHandoffUrl = "INFRA_GATE_PLANNER_EXECUTOR_HANDOFF_URL";
        public const string PlannerTokenEndpoint = "INFRA_GATE_PLANNER_TOKEN_ENDPOINT";
        public const string PlannerClientId = "INFRA_GATE_PLANNER_CLIENT_ID";
        public const string PlannerClientSecret = "INFRA_GATE_PLANNER_CLIENT_SECRET";
        public const string PlannerOAuthAuthority = "INFRA_GATE_PLANNER_OAUTH_AUTHORITY";
        public const string PlannerOAuthScope = "INFRA_GATE_PLANNER_OAUTH_SCOPE";
        public const string PlannerLlmProvider = "INFRA_GATE_PLANNER_LLM_PROVIDER";
        public const string PlannerLlmModel = "INFRA_GATE_PLANNER_LLM_MODEL";
        public const string PlannerLlmApiKey = "INFRA_GATE_PLANNER_LLM_API_KEY";
        public const string PlannerAnomalyWallClockCapSeconds = "INFRA_GATE_PLANNER_ANOMALY_WALL_CLOCK_CAP_SECONDS";
        public const string PlannerBatchWallClockCapSeconds = "INFRA_GATE_PLANNER_BATCH_WALL_CLOCK_CAP_SECONDS";
        public const string PlannerMaxToolIterations = "INFRA_GATE_PLANNER_MAX_TOOL_ITERATIONS";
        public const string PlannerFileSinkRoot = "INFRA_GATE_PLANNER_FILE_SINK_ROOT";
        public const string PlannerHostPath = "INFRA_GATE_PLANNER_HOST_PATH";
        public const string ExecutorAspnetcoreUrls = "INFRA_GATE_EXECUTOR_ASPNETCORE_URLS";
        public const string ExecutorGatewayBaseUrl = "INFRA_GATE_EXECUTOR_GATEWAY_BASE_URL";
        public const string ExecutorTokenEndpoint = "INFRA_GATE_EXECUTOR_TOKEN_ENDPOINT";
        public const string ExecutorClientId = "INFRA_GATE_EXECUTOR_CLIENT_ID";
        public const string ExecutorClientSecret = "INFRA_GATE_EXECUTOR_CLIENT_SECRET";
        public const string ExecutorOAuthAuthority = "INFRA_GATE_EXECUTOR_OAUTH_AUTHORITY";
        public const string ExecutorOAuthScope = "INFRA_GATE_EXECUTOR_OAUTH_SCOPE";
        public const string ExecutorConcurrencyCap = "INFRA_GATE_EXECUTOR_CONCURRENCY_CAP";
        public const string ExecutorWatchTimeoutSeconds = "INFRA_GATE_EXECUTOR_WATCH_TIMEOUT_SECONDS";
        public const string ExecutorHostPath = "INFRA_GATE_EXECUTOR_HOST_PATH";
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
