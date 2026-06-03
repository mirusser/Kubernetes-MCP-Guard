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
    public const string AspNetCoreUrlsKey = "InfraGate:Planner:AspNetCoreUrls";

    // A2A endpoint replacing the legacy /handoff/anomalies HTTP POST
    public const string A2AHandoffEndpointPath = "/a2a/planner";
    public const string A2AHandoffAgentName = "planner-agent";

    // A2A agent name used when the Planner constructs an AIAgent against the Observer's inbound A2A endpoint
    public const string A2AObserverAgentName = "planner-to-observer";
    public const string A2AExecutorAgentName = "planner-to-executor";
    public static readonly TimeSpan ExecutorDispatchTimeout = TimeSpan.FromMinutes(61);

    public const int DefaultAnomalyWallClockCapSeconds = 90;
    public const int MinAnomalyWallClockCapSeconds = 5;
    public const int MaxAnomalyWallClockCapSeconds = 300;

    public const int DefaultBatchWallClockCapSeconds = 300;
    public const int MinBatchWallClockCapSeconds = 30;
    public const int MaxBatchWallClockCapSeconds = 900;

    public const int DefaultMaxToolIterations = 6;
    public const int MinMaxToolIterations = 1;
    public const int MaxMaxToolIterations = 15;

    public static class EnvironmentVariables
    {
        public const string AspNetCoreUrls = "InfraGate__Planner__AspNetCoreUrls";
        public const string GatewayBaseUrl = "InfraGate__Planner__GatewayBaseUrl";
        public const string ExecutorHandoffUrl = "InfraGate__Planner__ExecutorHandoffUrl";
        public const string ObserverBaseUrl = "InfraGate__Planner__ObserverBaseUrl";
        public const string AnomalyWallClockCapSeconds = "InfraGate__Planner__AnomalyWallClockCapSeconds";
        public const string BatchWallClockCapSeconds = "InfraGate__Planner__BatchWallClockCapSeconds";
        public const string MaxToolIterations = "InfraGate__Planner__MaxToolIterations";
        public const string LlmProvider = "InfraGate__Planner__LlmProvider";
        public const string LlmModel = "InfraGate__Planner__LlmModel";
        public const string ClientId = "InfraGate__Planner__ClientCredentials__ClientId";
        public const string ClientSecret = "InfraGate__Planner__ClientCredentials__ClientSecret";
        public const string OAuthAuthority = "InfraGate__Planner__ClientCredentials__Authority";
        public const string OAuthScope = "InfraGate__Planner__ClientCredentials__Scope";
        public const string FileSinkRoot = "InfraGate__Planner__FileSinkRoot";
        public const string AuditConnectionString = "InfraGate__Planner__AuditConnectionString";
    }

    public static class ConfigurationKeys
    {
        public const string Planner = "InfraGate:Planner";
        public const string GatewayBaseUrl = "InfraGate:Planner:GatewayBaseUrl";
        public const string ExecutorHandoffUrl = "InfraGate:Planner:ExecutorHandoffUrl";
        public const string ObserverBaseUrl = "InfraGate:Planner:ObserverBaseUrl";
        public const string AnomalyWallClockCapSeconds = "InfraGate:Planner:AnomalyWallClockCapSeconds";
        public const string BatchWallClockCapSeconds = "InfraGate:Planner:BatchWallClockCapSeconds";
        public const string MaxToolIterations = "InfraGate:Planner:MaxToolIterations";
        public const string LlmProvider = "InfraGate:Planner:LlmProvider";
        public const string LlmModel = "InfraGate:Planner:LlmModel";
        public const string FileSinkRoot = "InfraGate:Planner:FileSinkRoot";
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
        public const string PlanId = "planId";
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
        public const string Status = "status";
        public const string ContentLower = "content";
        public const string ContentUpper = "Content";
        public const string TextLower = "text";
        public const string TextUpper = "Text";
    }

    public static class ToolNames
    {
        public const string GetPlanStatus = "get_plan_status";
        public const string ProposePlan = "propose_plan";
        public const string GetAllowedNamespaces = "get_allowed_namespaces";
        public const string GetK8sStatus = "get_k8s_status";
        public const string GetK8sEvents = "get_k8s_events";
        public const string GetK8sPods = "get_k8s_pods";
        public const string DescribeK8sResource = "describe_k8s_resource";
        public const string GetK8sDeployments = "get_k8s_deployments";
        public const string GetK8sServices = "get_k8s_services";
        public const string GetK8sEndpoints = "get_k8s_endpoints";

    }

    public static class PlanStatusValues
    {
        public const string NotFound = "NotFound";
        public const string ApprovalRequired = "ApprovalRequired";
        public const string Approved = "Approved";
        public const string Applied = "Applied";
        public const string Expired = "Expired";
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

    public static class Audit
    {
        public const string ServicePlannerSubject = "service:planner";
        public const string ServiceObserverSubject = "service:observer";

        public static class Outcomes
        {
            public const string Skipped = "skipped";
            public const string Succeeded = "succeeded";
            public const string Failed = "failed";
            public const string Received = "received";
        }

        public static class Reasons
        {
            public const string MissingPlanId = "missing_plan_id";
            public const string GatewayError = "gateway_error";
        }
    }

    public static class A2AHandoff
    {
        public const string AcceptedResponse = "accepted";
        public const string AgentIdPrefix = "planner-";
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
        public const string ObserverRequest = "ObserverRequest";
    }
}
