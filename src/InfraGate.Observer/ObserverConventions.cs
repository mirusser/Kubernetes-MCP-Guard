namespace InfraGate.Observer;

internal static class ObserverConventions
{
    private const string LoopbackHttpScheme = "http";
    private const string LoopbackHost = "127.0.0.1";
    private const string UriSchemeSeparator = "://";
    private const string DefaultPort = "3003";

    public const string DefaultUrl = LoopbackHttpScheme + UriSchemeSeparator + LoopbackHost + ":" + DefaultPort;

    public const string HealthEndpointPath = "/health";
    public const string DefaultClientId = "infra-gate-observer";
    public const string DefaultOAuthScope = "mcp:tools.readonly";

    public static class ConfigurationKeys
    {
        public const string Observer = "InfraGate:Observer";
        public const string CycleIntervalSeconds = "InfraGate:Observer:CycleIntervalSeconds";
        public const string WallClockCapSeconds = "InfraGate:Observer:WallClockCapSeconds";
        public const string MaxToolIterations = "InfraGate:Observer:MaxToolIterations";
        public const string GatewayBaseUrl = "InfraGate:Observer:GatewayBaseUrl";
        public const string AllowedNamespaces = "InfraGate:Observer:AllowedNamespaces";
        public const string LlmProvider = "InfraGate:Observer:LlmProvider";
        public const string LlmModel = "InfraGate:Observer:LlmModel";
        public const string LlmApiKey = "InfraGate:Observer:LlmApiKey";
    }

    public static class EnvironmentVariables
    {
        public const string AspNetCoreUrls = "ASPNETCORE_URLS";
        public const string CycleIntervalSeconds = "INFRA_GATE_OBSERVER_CYCLE_INTERVAL_SECONDS";
        public const string WallClockCapSeconds = "INFRA_GATE_OBSERVER_WALL_CLOCK_CAP_SECONDS";
        public const string MaxToolIterations = "INFRA_GATE_OBSERVER_MAX_TOOL_ITERATIONS";
        public const string GatewayBaseUrl = "INFRA_GATE_OBSERVER_GATEWAY_BASE_URL";
        public const string AllowedNamespaces = "INFRA_GATE_OBSERVER_ALLOWED_NAMESPACES";
        public const string LlmProvider = "INFRA_GATE_OBSERVER_LLM_PROVIDER";
        public const string LlmModel = "INFRA_GATE_OBSERVER_LLM_MODEL";
        public const string LlmApiKey = "INFRA_GATE_OBSERVER_LLM_API_KEY";
        public const string ClientId = "INFRA_GATE_OBSERVER_CLIENT_ID";
        public const string ClientSecret = "INFRA_GATE_OBSERVER_CLIENT_SECRET";
        public const string OAuthAuthority = "INFRA_GATE_OBSERVER_OAUTH_AUTHORITY";
        public const string OAuthScope = "INFRA_GATE_OBSERVER_OAUTH_SCOPE";
    }

    public static class ToolNames
    {
        public const string GetAllowedNamespaces = "get_allowed_namespaces";
        public const string GetK8sStatus = "get_k8s_status";
        public const string GetK8sEvents = "get_k8s_events";
        public const string GetK8sPods = "get_k8s_pods";
        public const string DescribeK8sResource = "describe_k8s_resource";
        public const string GetK8sDeployments = "get_k8s_deployments";
        public const string GetK8sServices = "get_k8s_services";
        public const string GetK8sEndpoints = "get_k8s_endpoints";

        public static readonly IReadOnlySet<string> ReadOnlyToolNames = new HashSet<string>(StringComparer.Ordinal)
        {
            GetAllowedNamespaces,
            GetK8sStatus,
            GetK8sEvents,
            GetK8sPods,
            DescribeK8sResource,
            GetK8sDeployments,
            GetK8sServices,
            GetK8sEndpoints,
        };
    }
}
