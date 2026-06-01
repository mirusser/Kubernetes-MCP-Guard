namespace InfraGate.Observer;

internal static class ObserverConventions
{
    private const string LoopbackHttpScheme = "http";
    private const string LoopbackHost = "127.0.0.1";
    private const string UriSchemeSeparator = "://";
    private const string DefaultPort = "3003";

    public const string DefaultUrl = LoopbackHttpScheme + UriSchemeSeparator + LoopbackHost + ":" + DefaultPort;

    public const string HealthEndpointPath = "/health";
    public const string ObserveNowEndpointPath = "/observe-now";
    public const int ObserveNowTimeoutSeconds = 30;
    public const int OnDemandSlackWindowSeconds = 2;
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
        public const string DedupeSuppressionWindow = "InfraGate:Observer:DedupeSuppressionWindow";
        public const string DedupeResolutionThreshold = "InfraGate:Observer:DedupeResolutionThreshold";
        public const string FileSinkRoot = "InfraGate:Observer:FileSink:Root";
        public const string PlannerHandoffUrl = "InfraGate:Observer:PlannerHandoffUrl";
        public const string AuditConnectionString = "InfraGate:Observer:AuditConnectionString";
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
        public const string DedupeSuppressionWindow = "INFRA_GATE_OBSERVER_DEDUPE_SUPPRESSION_WINDOW";
        public const string DedupeResolutionThreshold = "INFRA_GATE_OBSERVER_DEDUPE_RESOLUTION_THRESHOLD";
        public const string FileSinkRoot = "INFRA_GATE_OBSERVER_FILE_SINK_ROOT";
        public const string PlannerHandoffUrl = "INFRA_GATE_OBSERVER_PLANNER_HANDOFF_URL";
        public const string AuditConnectionString = "INFRA_GATE_OBSERVER_AUDIT_CONNECTION_STRING";
    }

    public static class LlmProviders
    {
        public const string Anthropic = "ANTHROPIC";
        public const string OpenAI = "OPENAI";
        public const string Google = "GOOGLE";
        public const string Azure = "AZURE";
        public const string Ollama = "OLLAMA";
        public const string OpenRouter = "OPENROUTER";
    }

    // A2A agent name used when the Observer constructs an AIAgent against the Planner's A2A endpoint
    public const string A2AHandoffAgentName = "observer-to-planner";

    // A2A server hosted by the Observer for inbound Planner→Observer messages
    public const string A2AInboundAgentName = "observer-inbound";
    public const string A2AInboundEndpointPath = "/a2a/observer";

    public static class Claims
    {
        public const string AuthorizedParty = "azp";
    }

    public static class ServiceClients
    {
        public const string Planner = "infra-gate-planner";
    }

    // "PlannerSender" means the Planner is the caller — the Observer is the receiver.
    // The Executor also defines a PlannerSender policy with the same semantics (Planner→Executor inbound).
    // Each lives in its own service; there is no cross-service collision.
    public static class Policies
    {
        public const string PlannerSender = "PlannerSender";
    }

    public static class HttpClients
    {
        public const string PlannerHandoff = "PlannerHandoff";
    }

    public static class Audit
    {
        public const string ServiceObserverSubject = "service:observer";

        public static class Outcomes
        {
            public const string Resolved = "resolved";
            public const string Active = "active";
            public const string Suppressed = "suppressed";
        }
    }

    public static class ToolNames
    {
        public const string GetAllowedNamespaces = "get_allowed_namespaces";
        public const string GetK8sStatus = "get_k8s_status";
        public const string GetK8sEvents = "get_k8s_events";
        public const string GetPodLogs = "get_pod_logs";
        public const string GetK8sResource = "get_k8s_resource";
        public const string GetDeploymentDiagnostics = "get_deployment_diagnostics";
        public const string GetPodDiagnostics = "get_pod_diagnostics";
        public const string GetServiceDiagnostics = "get_service_diagnostics";

        // Tools callable with only {namespace} — used by SnapshotFetcher to build the namespace overview.
        // SnapshotFetcher intersects this set with the live MCP tool list so missing tools are skipped
        // rather than generating isError=true responses.
        public static readonly IReadOnlySet<string> NamespaceSnapshotTools = new HashSet<string>(StringComparer.Ordinal)
        {
            GetK8sStatus,
            GetK8sEvents,
        };
    }

    public static class Prompts
    {
        public const string SystemPromptResourceName = "InfraGate.Observer.Prompts.ObserverSystemPrompt.md";
        public const string SystemPromptTemplateName = "observer-system-prompt";
        public const string NamespaceArgumentName = "namespace";
        public const string MaxToolIterationsArgumentName = "maxToolIterations";
    }
}
