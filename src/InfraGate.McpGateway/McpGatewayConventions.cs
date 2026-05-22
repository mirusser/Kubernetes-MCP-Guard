using InfraGate.Approvals;
using InfraGate.DownstreamAuth;
using InfraGate.RuntimeSafety;

namespace InfraGate.McpGateway;

internal static class McpGatewayConventions
{
    public static void RegisterInfraGateEnvVarMappings(InfraGateEnvVarMappings mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        mappings.Map(EnvironmentVariables.AspNetCoreUrls, ConfigurationKeys.AspNetCoreUrls);
        mappings.Map(EnvironmentVariables.DownstreamAssembly, ConfigurationKeys.DownstreamAssembly);
        mappings.Map(EnvironmentVariables.DownstreamProject, ConfigurationKeys.DownstreamProject);
        mappings.Map(EnvironmentVariables.GuardAuditRoot, ConfigurationKeys.GuardAuditRoot);
        mappings.Map(EnvironmentVariables.ApprovalBaseUrl, ConfigurationKeys.ApprovalBaseUrl);
        mappings.Map(EnvironmentVariables.ApprovalChallengeTtlSeconds, ConfigurationKeys.ApprovalChallengeTtlSeconds);
        RegisterDownstreamAuthMappings(mappings);
    }

    private static void RegisterDownstreamAuthMappings(InfraGateEnvVarMappings mappings)
    {
        mappings.Map(DownstreamAuthConventions.EnvironmentVariables.Required, DownstreamAuthConventions.ConfigurationKeys.Required);
        mappings.Map(DownstreamAuthConventions.EnvironmentVariables.Authority, DownstreamAuthConventions.ConfigurationKeys.Authority);
        mappings.Map(DownstreamAuthConventions.EnvironmentVariables.MetadataAddress, DownstreamAuthConventions.ConfigurationKeys.MetadataAddress);
        mappings.Map(DownstreamAuthConventions.EnvironmentVariables.RequireHttpsMetadata, DownstreamAuthConventions.ConfigurationKeys.RequireHttpsMetadata);
        mappings.Map(DownstreamAuthConventions.EnvironmentVariables.Audience, DownstreamAuthConventions.ConfigurationKeys.Audience);
        mappings.Map(DownstreamAuthConventions.EnvironmentVariables.Scope, DownstreamAuthConventions.ConfigurationKeys.Scope);
        mappings.Map(DownstreamAuthConventions.EnvironmentVariables.GatewayClientId, DownstreamAuthConventions.ConfigurationKeys.GatewayClientId);
        mappings.Map(DownstreamAuthConventions.EnvironmentVariables.GatewayClientSecret, DownstreamAuthConventions.ConfigurationKeys.GatewayClientSecret);
    }

    private const string LoopbackHttpScheme = "http";
    private const string LoopbackHost = "127.0.0.1";
    private const string UriSchemeSeparator = "://";
    private const string DefaultPort = "3001";

    public const string DefaultUrl = LoopbackHttpScheme + UriSchemeSeparator + LoopbackHost + ":" + DefaultPort;
    public const string McpPath = "/mcp";
    public const int DefaultEventLimit = 50;
    public const int DefaultLogTailLines = 200;
    internal const int RegexTimeoutMilliseconds = 1000;

    public static class ConfigurationKeys
    {
        public const string ApprovalBaseUrl = "InfraGate:Approval:BaseUrl";
        public const string ApprovalChallengeTtlSeconds = "InfraGate:Approval:ChallengeTtlSeconds";
        public const string ApprovalPostgresConnectionString = "InfraGate:Approval:Postgres:ConnectionString";
        public const string ApprovalRoot = "InfraGate:Approval:Root";
        public const string AspNetCoreUrls = "InfraGate:Gateway:AspNetCoreUrls";
        public const string DownstreamAssembly = "InfraGate:Gateway:DownstreamAssembly";
        public const string DownstreamProject = "InfraGate:Gateway:DownstreamProject";
        public const string GuardAuditRoot = "InfraGate:Gateway:GuardAuditRoot";
        public const string Urls = "urls";
    }

    public static class EnvironmentVariables
    {
        public const string AspNetCoreUrls = "ASPNETCORE_URLS";
        public const string DownstreamAssembly = "INFRA_GATE_DOWNSTREAM_ASSEMBLY";
        public const string DownstreamProject = "INFRA_GATE_DOWNSTREAM_PROJECT";
        public const string GuardAuditRoot = "INFRA_GATE_GUARD_AUDIT_ROOT";
        public const string ApprovalBaseUrl = "INFRA_GATE_APPROVAL_BASE_URL";
        public const string ApprovalChallengeTtlSeconds = "INFRA_GATE_APPROVAL_CHALLENGE_TTL_SECONDS";
    }

    public static class Paths
    {
        public const string SourceDirectory = "src";
        public const string DefaultDownstreamProjectDirectory = "InfraGate.McpServer";
        public const string DefaultDownstreamProjectFileName = "InfraGate.McpServer.csproj";
        public const string DefaultGuardAuditRootDirectory = ".mcp-guardrails";
        public const string AuditFileName = "audit.jsonl";
    }

    public static class DownstreamProcess
    {
        public const string Name = "infra-gate-downstream";
        public const string Command = "dotnet";
        public const string RunArgument = "run";
        public const string ProjectArgument = "--project";

        /// <summary>
        /// Explicit allowlist of environment variable names that are safe to pass to the downstream server subprocess.
        /// An allowlist is used (rather than a denylist) so that new secrets added to the gateway in the future
        /// are excluded by default rather than leaking automatically.
        /// </summary>
        public static readonly IReadOnlySet<string> AllowedEnvironmentVariables =
            new HashSet<string>(StringComparer.Ordinal)
            {
                // .NET / OS runtime — required for dotnet to run
                "PATH",
                "HOME",
                "DOTNET_ROOT",
                "DOTNET_MULTILEVEL_LOOKUP",
                "TMPDIR",
                "TMP",
                "TEMP",

                // Runtime environment signals — server reads these to determine its mode
                RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment,
                RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment,
                RuntimeSafetyConventions.EnvironmentVariables.AspNetCoreEnvironment,
                RuntimeSafetyConventions.EnvironmentVariables.ConfigPath,

                // Kubernetes access — server needs these to connect to the cluster
                "KUBECONFIG",
                "K8S_MCP_USE_IN_CLUSTER",

                // Server domain config — approval root, namespaces, logging
                ApprovalConventions.EnvironmentVariables.ApprovalRoot,
                "K8S_MCP_ALLOWED_NAMESPACES",
                "K8S_MCP_LOG_PATH",

                // Downstream auth (server-side validation config only — no gateway credentials)
                DownstreamAuthConventions.EnvironmentVariables.Required,
                DownstreamAuthConventions.EnvironmentVariables.Authority,
                DownstreamAuthConventions.EnvironmentVariables.MetadataAddress,
                DownstreamAuthConventions.EnvironmentVariables.RequireHttpsMetadata,
                DownstreamAuthConventions.EnvironmentVariables.Audience,
                DownstreamAuthConventions.EnvironmentVariables.Scope,
                // GatewayClientId and GatewayClientSecret are intentionally excluded:
                // they are gateway-only credentials and must never reach the server subprocess.
            };
    }

    public static class Approvals
    {
        public const string PathPrefix = "/approvals";
        public const string ChallengeRoute = "/approvals/{challengeId}";
        public const string ApproveRoute = "/approvals/{challengeId}/approve";
        public const string DenyRoute = "/approvals/{challengeId}/deny";
        public const string CancelRoute = "/approvals/{challengeId}/cancel";
        public const string LoginPath = "/approvals/login";
        public const string RequestVerificationToken = "__RequestVerificationToken";
    }

    public static class ApprovalReasonCodes
    {
        public const string AdapterDecodeFailed = "gateway.approval.adapter_decode_failed";
        public const string ApprovalRequired = "gateway.approval.required";
        public const string AuthenticatedSubjectRequired = "gateway.approval.authenticated_subject_required";
        public const string PlanExpired = "gateway.approval.plan_expired";
        public const string PlanNotStarted = "gateway.approval.plan_not_started";
        public const string SameSubjectRequired = "gateway.approval.same_subject_required";
    }

    public static class ToolNames
    {
        public const string RequestToolPrefix = "request_";
        public const string ApplyApprovedPlan = "execute_approved_plan";
        public const string GetPlanStatus = "get_plan_status";
        public const string WaitForPlanApproval = "wait_for_plan_approval";
    }

    public static class ToolArguments
    {
        public const string PlanId = "planId";
        public const string TimeoutSeconds = "timeoutSeconds";
    }

    public static class ToolResponseFields
    {
        public const string Status = "status";
        public const string TimedOut = "timedOut";
    }

    public static class GuardrailAudit
    {
        public const string OAuthAuthenticationType = "oauth-jwt";
        public const string RequestDirection = "request";
        public const string ResponseDirection = "response";
        public const string WarnAction = "warn";
        public const string WarnRedactAction = "warn_redact";
        public const string RedactManifestAction = "redact_manifest";
    }

    public static class GuardrailCategories
    {
        public const string IgnoreInstructions = "ignore-instructions";
        public const string RevealPrompts = "reveal-prompts";
        public const string ToolUse = "tool-use";
        public const string SecretExfiltration = "secret-exfiltration";
        public const string AuthorityOverride = "authority-override";
        public const string ManifestEchoCategory = "manifest-echo";
    }

    public static class GuardrailLocations
    {
        public const string Response = "response";
        public const string ResponseManifest = Response + ".manifest";
        public const string ResponseLine = Response + ".line";
    }

    public static class RegexGroups
    {
        public const string Id = "id";
        public const string Manifest = "manifest";
        public const string Prefix = "prefix";
    }

    public static class Redactions
    {
        public const string PromptInjectionRisk = "[redacted: prompt-injection-risk]";
        public const string InspectPendingPlan = "[redacted: inspect the pending plan file before approval]";
        public const string SensitivePlanMetadata = "[redacted: sensitive plan metadata]";
    }

}
