namespace InfraGate.Executor;

internal static class ExecutorConventions
{
    private const string LoopbackHttpScheme = "http";
    private const string LoopbackHost = "localhost";
    private const string UriSchemeSeparator = "://";
    private const string DefaultPort = "3005";

    public const string DefaultClientId = "infra-gate-executor";
    public const string DefaultUrl = LoopbackHttpScheme + UriSchemeSeparator + LoopbackHost + ":" + DefaultPort;
    public const string HealthEndpointPath = "/health";
    public const string A2AHandoffEndpointPath = "/a2a/executor";
    public const string A2AHandoffAgentName = "executor-agent";

    public const int DefaultConcurrencyCap = 64;
    public const int MinConcurrencyCap = 1;
    public const int MaxConcurrencyCap = 256;

    public const int DefaultWatchTimeoutSeconds = 3600;
    public const int MinWatchTimeoutSeconds = 60;
    public const int MaxWatchTimeoutSeconds = 3600;

    public const int WaitForPlanApprovalPerCallTimeoutSeconds = 55;

    /// <summary>Configuration section bound to <see cref="ExecutorOptions"/> (recursive auto-binding).</summary>
    public const string SectionName = "InfraGate:Executor";

    public const string AspNetCoreUrlsKey = "InfraGate:Executor:AspNetCoreUrls";

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

    // "PlannerSender" means the Planner is the caller — the Executor is the receiver.
    // The Observer also defines a PlannerSender policy with the same semantics (Planner→Observer inbound).
    public static class Policies
    {
        public const string PlannerSender = "PlannerSender";
    }
}
