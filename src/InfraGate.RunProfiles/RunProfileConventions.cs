namespace InfraGate.RunProfiles;

internal static class RunProfileConventions
{
    public const string DefaultConfigPath = "deploy/run-profiles.yaml";

    public static class Commands
    {
        public const string Generate = "generate";
        public const string GenerateToml = "generate-toml";
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

    public static class GeneratedFile
    {
        public const string HeaderLinePrefix = "# Generated from ";
        public const string ProfileMarker = " profile: ";
        public const string DoNotEditLinePrefix = "# Do not edit. Run: dotnet run --project src/InfraGate.RunProfiles -- generate ";
        public const string TomlDoNotEditLinePrefix = "# Do not edit. Run: dotnet run --project src/InfraGate.RunProfiles -- generate-toml ";
    }

    public static class YamlKeys
    {
        public const string AllowedNamespaces = "allowedNamespaces";
        public const string ApprovalAuthority = "approvalAuthority";
        public const string ApprovalRoot = "approvalRoot";
        public const string ApiKey = "apiKey";
        public const string PostgresConnectionString = "postgresConnectionString";
        public const string RunMigrationsOnStartup = "runMigrationsOnStartup";
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
        public const string DownstreamAssemblyHash = "downstreamAssemblyHash";
        public const string DownstreamAuth = "downstreamAuth";
        public const string Gateway = "gateway";
        public const string AgentGuardrails = "agentGuardrails";
        public const string ModelVisibleContent = "modelVisibleContent";
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
        public const string OpenRouter = "openRouter";
        public const string OtlpEndpoint = "otlpEndpoint";
        public const string DashboardToken = "dashboardToken";
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
        public const string Telemetry = "telemetry";
        public const string TokenIntrospectionCacheSeconds = "tokenIntrospectionCacheSeconds";
        public const string TokenIntrospectionClientId = "tokenIntrospectionClientId";
        public const string TokenIntrospectionClientSecret = "tokenIntrospectionClientSecret";
        public const string TokenIntrospectionEnabled = "tokenIntrospectionEnabled";
        public const string TokenIntrospectionEndpoint = "tokenIntrospectionEndpoint";
        public const string MaxAcceptedAccessTokenLifetimeSeconds = "maxAcceptedAccessTokenLifetimeSeconds";
        public const string UseDPoP = "useDPoP";
        public const string Type = "type";
        public const string Version = "version";
        public const string ClientId = "clientId";
        public const string ClientSecret = "clientSecret";
        public const string CycleCadenceSeconds = "cycleCadenceSeconds";
        public const string CycleWallClockCapSeconds = "cycleWallClockCapSeconds";
        public const string FileSinkRoot = "fileSinkRoot";
        public const string GatewayBaseUrl = "gatewayBaseUrl";
        public const string LlmModel = "llmModel";
        public const string LlmProvider = "llmProvider";
        public const string MaxToolIterations = "maxToolIterations";
        public const string ObserverAuditConnectionString = "auditConnectionString";
        public const string ObserverHostPath = "observerHostPath";
        public const string SkipCycleWhenNoWarningEvents = "skipCycleWhenNoWarningEvents";
        public const string Enabled = "enabled";
        public const string SemanticClassifierEnabled = "semanticClassifierEnabled";
        public const string LocalClassifierBaseUrl = "localClassifierBaseUrl";
        public const string RequestTimeoutMilliseconds = "requestTimeoutMilliseconds";
        public const string MaximumInputCharacters = "maximumInputCharacters";
        public const string UnavailableBehavior = "unavailableBehavior";
    }

    public static class Env
    {
        public const string ApprovalBaseUrl = "InfraGate__Approval__BaseUrl";
        public const string ApprovalHostPath = "INFRA_GATE_APPROVAL_HOST_PATH";
        public const string ApprovalOauthAuthorizationEndpoint = "InfraGate__Auth__ApprovalOAuthAuthorizationEndpoint";
        public const string ApprovalOauthCallbackPath = "InfraGate__Auth__ApprovalOAuthCallbackPath";
        public const string ApprovalOauthClientId = "InfraGate__Auth__ApprovalOAuthClientId";
        public const string ApprovalOauthTokenEndpoint = "InfraGate__Auth__ApprovalOAuthTokenEndpoint";
        public const string ApprovalRoot = "InfraGate__Approval__Root";
        public const string ApprovalPostgresConnectionString = "InfraGate__Approval__Postgres__ConnectionString";
        public const string ApprovalPostgresRunMigrationsOnStartup = "InfraGate__Approval__Postgres__RunMigrationsOnStartup";
        public const string AllowedNamespaces = "InfraGate__Kubernetes__AllowedNamespaces";
        public const string AspnetcoreUrls = "InfraGate__Gateway__AspNetCoreUrls";
        public const string BindAddress = "INFRA_GATE_BIND_ADDRESS";
        public const string BindPort = "INFRA_GATE_BIND_PORT";
        public const string DataProtectionHostPath = "INFRA_GATE_DATA_PROTECTION_HOST_PATH";
        public const string DownstreamAssembly = "InfraGate__Gateway__DownstreamAssembly";
        public const string DownstreamAssemblyHash = "InfraGate__Gateway__DownstreamAssemblyHash";
        public const string DownstreamAuthAudience = "InfraGate__DownstreamAuth__Audience";
        public const string DownstreamAuthAuthority = "InfraGate__DownstreamAuth__Authority";
        public const string DownstreamAuthGatewayClientId = "InfraGate__DownstreamAuth__GatewayClientId";
        public const string DownstreamAuthGatewayClientSecret = "InfraGate__DownstreamAuth__GatewayClientSecret";
        public const string DownstreamAuthMetadataAddress = "InfraGate__DownstreamAuth__MetadataAddress";
        public const string DownstreamAuthRequired = "InfraGate__DownstreamAuth__Required";
        public const string DownstreamAuthRequireHttpsMetadata = "InfraGate__DownstreamAuth__RequireHttpsMetadata";
        public const string DownstreamAuthScope = "InfraGate__DownstreamAuth__Scope";
        public const string GatewayImage = "INFRA_GATE_GATEWAY_IMAGE";
        public const string GuardAuditHostPath = "INFRA_GATE_GUARD_AUDIT_HOST_PATH";
        public const string GuardAuditRoot = "InfraGate__Gateway__GuardAuditRoot";
        public const string InfraGateEnvironment = "InfraGate__Runtime__Environment";
        public const string KubeconfigHostPath = "INFRA_GATE_KUBECONFIG_HOST_PATH";
        public const string KubeConfig = "InfraGate__Kubernetes__KubeConfig";
        public const string OauthAuthority = "InfraGate__Auth__OAuthAuthority";
        public const string OauthMetadataAddress = "InfraGate__Auth__OAuthMetadataAddress";
        public const string OauthRequireHttpsMetadata = "InfraGate__Auth__OAuthRequireHttpsMetadata";
        public const string OauthResource = "InfraGate__Auth__OAuthResource";
        public const string OauthScope = "InfraGate__Auth__OAuthScope";
        public const string TokenIntrospectionEnabled = "InfraGate__Auth__TokenIntrospectionEnabled";
        public const string TokenIntrospectionEndpoint = "InfraGate__Auth__TokenIntrospectionEndpoint";
        public const string TokenIntrospectionClientId = "InfraGate__Auth__TokenIntrospectionClientId";
        public const string TokenIntrospectionClientSecret = "InfraGate__Auth__TokenIntrospectionClientSecret";
        public const string TokenIntrospectionCacheSeconds = "InfraGate__Auth__TokenIntrospectionCacheSeconds";
        public const string MaxAcceptedAccessTokenLifetimeSeconds = "InfraGate__Auth__MaxAcceptedAccessTokenLifetimeSeconds";
        public const string OpenRouterApiKey = "InfraGate__OpenRouter__ApiKey";
        public const string OtelExporterOtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";
        public const string AspireDashboardToken = "ASPIRE_DASHBOARD_TOKEN";
        public const string ObserverAspnetcoreUrls = "InfraGate__Observer__AspNetCoreUrls";
        public const string ObserverClientId = "InfraGate__Observer__ClientCredentials__ClientId";
        public const string ObserverClientSecret = "InfraGate__Observer__ClientCredentials__ClientSecret";
        public const string ObserverUseDPoP = "InfraGate__Observer__ClientCredentials__UseDPoP";
        public const string ObserverCycleIntervalSeconds = "InfraGate__Observer__CycleIntervalSeconds";
        public const string ObserverCycleWallClockCapSeconds = "InfraGate__Observer__WallClockCapSeconds";
        public const string ObserverFileSinkRoot = "InfraGate__Observer__FileSinkRoot";
        public const string ObserverGatewayBaseUrl = "InfraGate__Observer__GatewayBaseUrl";
        public const string ObserverHostPath = "INFRA_GATE_OBSERVER_HOST_PATH";
        public const string ObserverLlmModel = "InfraGate__Observer__LlmModel";
        public const string ObserverLlmProvider = "InfraGate__Observer__LlmProvider";
        public const string ObserverMaxToolIterations = "InfraGate__Observer__MaxToolIterations";
        public const string ObserverOAuthAuthority = "InfraGate__Observer__ClientCredentials__Authority";
        public const string ObserverPlannerHandoffUrl = "InfraGate__Observer__PlannerHandoffUrl";
        public const string ObserverScope = "InfraGate__Observer__ClientCredentials__Scope";
        public const string ObserverAllowedNamespaces = "InfraGate__Observer__AllowedNamespaces";
        public const string ObserverAuditConnectionString = "InfraGate__Observer__AuditConnectionString";
        public const string ObserverSkipCycleWhenNoWarningEvents = "InfraGate__Observer__SkipCycleWhenNoWarningEvents";
        public const string PlannerAspnetcoreUrls = "InfraGate__Planner__AspNetCoreUrls";
        public const string PlannerGatewayBaseUrl = "InfraGate__Planner__GatewayBaseUrl";
        public const string PlannerExecutorHandoffUrl = "InfraGate__Planner__ExecutorHandoffUrl";
        public const string PlannerClientId = "InfraGate__Planner__ClientCredentials__ClientId";
        public const string PlannerClientSecret = "InfraGate__Planner__ClientCredentials__ClientSecret";
        public const string PlannerUseDPoP = "InfraGate__Planner__ClientCredentials__UseDPoP";
        public const string PlannerOAuthAuthority = "InfraGate__Planner__ClientCredentials__Authority";
        public const string PlannerOAuthScope = "InfraGate__Planner__ClientCredentials__Scope";
        public const string PlannerLlmProvider = "InfraGate__Planner__LlmProvider";
        public const string PlannerLlmModel = "InfraGate__Planner__LlmModel";
        public const string PlannerAnomalyWallClockCapSeconds = "InfraGate__Planner__AnomalyWallClockCapSeconds";
        public const string PlannerBatchWallClockCapSeconds = "InfraGate__Planner__BatchWallClockCapSeconds";
        public const string PlannerMaxToolIterations = "InfraGate__Planner__MaxToolIterations";
        public const string PlannerFileSinkRoot = "InfraGate__Planner__FileSinkRoot";
        public const string PlannerHostPath = "INFRA_GATE_PLANNER_HOST_PATH";
        public const string ExecutorAspnetcoreUrls = "InfraGate__Executor__AspNetCoreUrls";
        public const string ExecutorGatewayBaseUrl = "InfraGate__Executor__GatewayBaseUrl";
        public const string ExecutorClientId = "InfraGate__Executor__ClientCredentials__ClientId";
        public const string ExecutorClientSecret = "InfraGate__Executor__ClientCredentials__ClientSecret";
        public const string ExecutorUseDPoP = "InfraGate__Executor__ClientCredentials__UseDPoP";
        public const string ExecutorOAuthAuthority = "InfraGate__Executor__ClientCredentials__Authority";
        public const string ExecutorOAuthScope = "InfraGate__Executor__ClientCredentials__Scope";
        public const string ExecutorConcurrencyCap = "InfraGate__Executor__ConcurrencyCap";
        public const string ExecutorWatchTimeoutSeconds = "InfraGate__Executor__WatchTimeoutSeconds";
        public const string ExecutorHostPath = "INFRA_GATE_EXECUTOR_HOST_PATH";
        public const string ModelVisibleContentEnabled = "InfraGate__AgentGuardrails__ModelVisibleContent__Enabled";
        public const string ModelVisibleContentSemanticClassifierEnabled = "InfraGate__AgentGuardrails__ModelVisibleContent__SemanticClassifierEnabled";
        public const string ModelVisibleContentRequestTimeoutMilliseconds = "InfraGate__AgentGuardrails__ModelVisibleContent__RequestTimeoutMilliseconds";
        public const string ModelVisibleContentMaximumInputCharacters = "InfraGate__AgentGuardrails__ModelVisibleContent__MaximumInputCharacters";
        public const string ModelVisibleContentUnavailableBehavior = "InfraGate__AgentGuardrails__ModelVisibleContent__UnavailableBehavior";
    }

    public static class DomainAdapterTypes
    {
        public const string Kubernetes = "kubernetes";
    }

    public static class Toml
    {
        public const string KubeConfig = "kubeconfig";
        public const string ReadOnly = "read_only";
        public const string EnabledTools = "enabled_tools";
    }
}
