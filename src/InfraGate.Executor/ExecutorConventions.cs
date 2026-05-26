namespace InfraGate.Executor;

internal static class ExecutorConventions
{
    private const string LoopbackHttpScheme = "http";
    private const string LoopbackHost = "localhost";
    private const string UriSchemeSeparator = "://";
    private const string DefaultPort = "3005";

    public const string DefaultClientId = "infra-gate-executor";
    public const string DefaultOAuthScope = "mcp:tools.execute";
    public const string DefaultUrl = LoopbackHttpScheme + UriSchemeSeparator + LoopbackHost + ":" + DefaultPort;
    public const string HealthEndpointPath = "/health";
    public const string HandoffProposalsEndpointPath = "/handoff/proposals";

    public const int DefaultConcurrencyCap = 64;
    public const int MinConcurrencyCap = 1;
    public const int MaxConcurrencyCap = 256;

    public const int DefaultWatchTimeoutSeconds = 900;
    public const int MinWatchTimeoutSeconds = 60;
    public const int MaxWatchTimeoutSeconds = 3600;

    public const int WaitForPlanApprovalPerCallTimeoutSeconds = 55;

    public static class EnvironmentVariables
    {
        public const string AspNetCoreUrls = "ASPNETCORE_URLS";
        public const string GatewayBaseUrl = "INFRA_GATE_EXECUTOR_GATEWAY_BASE_URL";
        public const string ConcurrencyCap = "INFRA_GATE_EXECUTOR_CONCURRENCY_CAP";
        public const string WatchTimeoutSeconds = "INFRA_GATE_EXECUTOR_WATCH_TIMEOUT_SECONDS";
        public const string ClientId = "INFRA_GATE_EXECUTOR_CLIENT_ID";
        public const string ClientSecret = "INFRA_GATE_EXECUTOR_CLIENT_SECRET";
        public const string OAuthAuthority = "INFRA_GATE_EXECUTOR_OAUTH_AUTHORITY";
        public const string OAuthScope = "INFRA_GATE_EXECUTOR_OAUTH_SCOPE";
    }

    public static class ConfigurationKeys
    {
        public const string Executor = "InfraGate:Executor";
        public const string GatewayBaseUrl = "InfraGate:Executor:GatewayBaseUrl";
        public const string ConcurrencyCap = "InfraGate:Executor:ConcurrencyCap";
        public const string WatchTimeoutSeconds = "InfraGate:Executor:WatchTimeoutSeconds";
    }

    public static class ToolNames
    {
        public const string WaitForPlanApproval = "wait_for_plan_approval";
        public const string ExecuteApprovedPlan = "execute_approved_plan";

        public static readonly IReadOnlySet<string> AllowedToolNames = new HashSet<string>(StringComparer.Ordinal)
        {
            WaitForPlanApproval,
            ExecuteApprovedPlan,
        };
    }

    public static class ToolArguments
    {
        public const string PlanId = "planId";
        public const string TimeoutSeconds = "timeoutSeconds";
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
        public const string Planner = "infra-gate-planner";
    }

    public static class Policies
    {
        public const string PlannerSender = "PlannerSender";
    }
}
