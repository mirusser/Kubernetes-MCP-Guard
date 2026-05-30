namespace InfraGate.Planner;

internal static class PlannerConventions
{
    private const string LoopbackHttpScheme = "http";
    private const string LoopbackHost = "localhost";
    private const string UriSchemeSeparator = "://";
    private const string DefaultPort = "3004";

    public const string DefaultClientId = "infra-gate-planner";
    public const string DefaultOAuthScope = "mcp:tools.propose mcp:tools.readonly";
    public const string DefaultUrl = LoopbackHttpScheme + UriSchemeSeparator + LoopbackHost + ":" + DefaultPort;
    public const string DefaultLlmModel = "claude-sonnet-4-6";
    public const string DefaultOpenRouterLlmModel = "deepseek/deepseek-v4-flash:free";
    public const string HealthEndpointPath = "/health";
    public const string HandoffAnomaliesEndpointPath = "/handoff/anomalies";

    public const int DefaultAnomalyWallClockCapSeconds = 30;
    public const int MinAnomalyWallClockCapSeconds = 5;
    public const int MaxAnomalyWallClockCapSeconds = 120;

    public const int DefaultBatchWallClockCapSeconds = 300;
    public const int MinBatchWallClockCapSeconds = 30;
    public const int MaxBatchWallClockCapSeconds = 900;

    public const int DefaultMaxToolIterations = 4;
    public const int MinMaxToolIterations = 1;
    public const int MaxMaxToolIterations = 10;

    public static class EnvironmentVariables
    {
        public const string AspNetCoreUrls = "ASPNETCORE_URLS";
        public const string GatewayBaseUrl = "INFRA_GATE_PLANNER_GATEWAY_BASE_URL";
        public const string ExecutorHandoffUrl = "INFRA_GATE_PLANNER_EXECUTOR_HANDOFF_URL";
        public const string AnomalyWallClockCapSeconds = "INFRA_GATE_PLANNER_ANOMALY_WALL_CLOCK_CAP_SECONDS";
        public const string BatchWallClockCapSeconds = "INFRA_GATE_PLANNER_BATCH_WALL_CLOCK_CAP_SECONDS";
        public const string MaxToolIterations = "INFRA_GATE_PLANNER_MAX_TOOL_ITERATIONS";
        public const string LlmProvider = "INFRA_GATE_PLANNER_LLM_PROVIDER";
        public const string LlmModel = "INFRA_GATE_PLANNER_LLM_MODEL";
        public const string LlmApiKey = "INFRA_GATE_PLANNER_LLM_API_KEY";
        public const string ClientId = "INFRA_GATE_PLANNER_CLIENT_ID";
        public const string ClientSecret = "INFRA_GATE_PLANNER_CLIENT_SECRET";
        public const string OAuthAuthority = "INFRA_GATE_PLANNER_OAUTH_AUTHORITY";
        public const string OAuthScope = "INFRA_GATE_PLANNER_OAUTH_SCOPE";
        public const string FileSinkRoot = "INFRA_GATE_PLANNER_FILE_SINK_ROOT";
        public const string AuditConnectionString = "INFRA_GATE_PLANNER_AUDIT_CONNECTION_STRING";
    }

    public static class ConfigurationKeys
    {
        public const string Planner = "InfraGate:Planner";
        public const string GatewayBaseUrl = "InfraGate:Planner:GatewayBaseUrl";
        public const string ExecutorHandoffUrl = "InfraGate:Planner:ExecutorHandoffUrl";
        public const string AnomalyWallClockCapSeconds = "InfraGate:Planner:AnomalyWallClockCapSeconds";
        public const string BatchWallClockCapSeconds = "InfraGate:Planner:BatchWallClockCapSeconds";
        public const string MaxToolIterations = "InfraGate:Planner:MaxToolIterations";
        public const string LlmProvider = "InfraGate:Planner:LlmProvider";
        public const string LlmModel = "InfraGate:Planner:LlmModel";
        public const string LlmApiKey = "InfraGate:Planner:LlmApiKey";
        public const string FileSinkRoot = "InfraGate:Planner:FileSink:Root";
        public const string AuditConnectionString = "InfraGate:Planner:AuditConnectionString";
    }

    public static class Prompts
    {
        public const string SystemPromptResourceName = "InfraGate.Planner.Prompts.PlannerSystemPrompt.md";
        public const string SystemPromptTemplateName = "planner-system-prompt";
    }

    public static class Llm
    {
        public const string ToolCallPrefix = "TOOL_CALL:";
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

    public static class OperationTypes
    {
        public const string RestartDeployment = "restart_deployment";
        public const string ScaleDeployment = "scale_deployment";
        public const string SetDeploymentImage = "set_deployment_image";

        public static readonly IReadOnlySet<string> AllowedOperationTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            RestartDeployment,
            ScaleDeployment,
            SetDeploymentImage,
        };
    }

    public static class ToolArguments
    {
        public const string OperationType = "operationType";
        public const string OperationArguments = "arguments";
        public const string Name = "name";
        public const string Namespace = "namespace";
        public const string Replicas = "replicas";
        public const string Container = "container";
        public const string Image = "image";
    }

    public static class ProposePlanResponseFields
    {
        public const string PlanId = "planId";
        public const string ContentLower = "content";
        public const string ContentUpper = "Content";
        public const string TextLower = "text";
        public const string TextUpper = "Text";
    }

    public static class ToolNames
    {
        public const string ProposePlan = "propose_plan";
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

        public static readonly IReadOnlySet<string> AllowedToolNames = new HashSet<string>(StringComparer.Ordinal)
        {
            ProposePlan,
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

    public static class Claims
    {
        public const string AuthorizedParty = "azp";
    }

    public static class ServiceClients
    {
        public const string Observer = "infra-gate-observer";
    }

    public static class Policies
    {
        public const string ObserverSender = "ObserverSender";
    }

    public static class FilterDropReasons
    {
        public const string Resolved = "resolved";
        public const string UnsupportedKind = "unsupported_kind";
        public const string DedupeActivePlan = "dedupe:active_plan";
        public const string DedupeOperationInBatch = "dedupe:operation_in_batch";
    }

    public static class Dedupe
    {
        /// <summary>TTL for a successfully proposed plan — matches plan validity window.</summary>
        public static readonly TimeSpan ActivePlanTtl = TimeSpan.FromHours(1);

        /// <summary>Backoff TTL when propose_plan fails — prevents hammering the gateway.</summary>
        public static readonly TimeSpan FailedProposalBackoff = TimeSpan.FromMinutes(5);
    }

    public static class HttpClients
    {
        public const string ExecutorHandoff = "ExecutorHandoff";
    }
}
