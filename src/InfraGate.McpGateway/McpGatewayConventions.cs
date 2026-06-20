using InfraGate;
using InfraGate.RuntimeSafety;

namespace InfraGate.McpGateway;

internal static class McpGatewayConventions
{
    private const string LoopbackHttpScheme = "http";
    private const string LoopbackHost = "127.0.0.1";
    private const string UriSchemeSeparator = "://";
    private const string DefaultPort = "3001";

    public const string DefaultUrl = LoopbackHttpScheme + UriSchemeSeparator + LoopbackHost + ":" + DefaultPort;
    public const string DefaultOperatorGroup = "kubernetes-operators";
    public const string McpPath = "/mcp";
    public const int DefaultEventLimit = 50;
    public const int DefaultLogTailLines = 200;
    internal const int RegexTimeoutMilliseconds = 1000;

    public static class Telemetry
    {
        public const string MeterName = "InfraGate.McpGateway";
        public const string MeterVersion = "1.0";
        public const string GuardrailAuditWriteFailedCounterName = "infragate.gateway.guardrail.audit_write.failed";
        public const string EmailFailedCounterName = "infragate.gateway.email.failed";

        public static class Tags
        {
            public const string ToolName = "tool.name";
            public const string GuardrailDirection = "guardrail.direction";
            public const string GuardrailAction = "guardrail.action";
        }
    }

    public static class ConfigurationKeys
    {
        public const string ApprovalBaseUrl = "InfraGate:Approval:BaseUrl";
        public const string ApprovalChallengeTtlSeconds = "InfraGate:Approval:ChallengeTtlSeconds";
        public const string ApprovalPostgresConnectionString = "InfraGate:Approval:Postgres:ConnectionString";

        public const string ApprovalPostgresRunMigrationsOnStartup =
            "InfraGate:Approval:Postgres:RunMigrationsOnStartup";

        public const string ApprovalRoot = "InfraGate:Approval:Root";
        public const string AspNetCoreUrls = "InfraGate:Gateway:AspNetCoreUrls";
        public const string DownstreamAssembly = "InfraGate:Gateway:DownstreamAssembly";
        public const string DownstreamProject = "InfraGate:Gateway:DownstreamProject";
        public const string GuardAuditRoot = "InfraGate:Gateway:GuardAuditRoot";
        public const string OperatorEmail = "InfraGate:Approval:OperatorEmail";
        public const string OperatorGroup = "InfraGate:Approval:OperatorGroup";
        public const string SmtpHost = "InfraGate:Approval:Smtp:Host";
        public const string SmtpPort = "InfraGate:Approval:Smtp:Port";
        public const string SmtpFrom = "InfraGate:Approval:Smtp:From";
        public const string SmtpUser = "InfraGate:Approval:Smtp:User";
        public const string SmtpPassword = "InfraGate:Approval:Smtp:Password";
        public const string SmtpEnableSsl = "InfraGate:Approval:Smtp:EnableSsl";
        public const string Urls = "urls";
    }

    public static class EnvironmentVariables
    {
        public const string AspNetCoreUrls = "InfraGate__Gateway__AspNetCoreUrls";
        public const string DownstreamAssembly = "InfraGate__Gateway__DownstreamAssembly";
        public const string DownstreamProject = "InfraGate__Gateway__DownstreamProject";
        public const string GuardAuditRoot = "InfraGate__Gateway__GuardAuditRoot";
        public const string ApprovalBaseUrl = "InfraGate__Approval__BaseUrl";
        public const string ApprovalChallengeTtlSeconds = "InfraGate__Approval__ChallengeTtlSeconds";
        public const string OperatorEmail = "InfraGate__Approval__OperatorEmail";
        public const string OperatorGroup = "InfraGate__Approval__OperatorGroup";
        public const string SmtpHost = "InfraGate__Approval__Smtp__Host";
        public const string SmtpPort = "InfraGate__Approval__Smtp__Port";
        public const string SmtpFrom = "InfraGate__Approval__Smtp__From";
        public const string SmtpUser = "InfraGate__Approval__Smtp__User";
        public const string SmtpPassword = "InfraGate__Approval__Smtp__Password";
        public const string SmtpEnableSsl = "InfraGate__Approval__Smtp__EnableSsl";
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
        /// Uses InfraGate__ framework-convention names; old K8S_MCP_* names are hard-cut (Task 2 stdio migration).
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
                "InfraGate__Runtime__Environment",
                RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment,
                RuntimeSafetyConventions.EnvironmentVariables.AspNetCoreEnvironment,
                RuntimeSafetyConventions.EnvironmentVariables.ConfigPath,

                // Server domain config — InfraGate:Kubernetes section (__ convention)
                "InfraGate__Kubernetes__KubeConfig",
                "InfraGate__Kubernetes__UseInClusterConfig",
                "InfraGate__Kubernetes__LogPath",
                "InfraGate__Kubernetes__AllowedNamespaces__0",
                "InfraGate__Kubernetes__AllowedNamespaces__1",
                "InfraGate__Kubernetes__AllowedNamespaces__2",
                "InfraGate__Kubernetes__AllowedNamespaces__3",
                "InfraGate__Kubernetes__AllowedNamespaces__4",
                "InfraGate__Kubernetes__AllowedNamespaces__5",
                "InfraGate__Kubernetes__AllowedNamespaces__6",
                "InfraGate__Kubernetes__AllowedNamespaces__7",
                "InfraGate__Kubernetes__AllowedNamespaces__8",
                "InfraGate__Kubernetes__AllowedNamespaces__9",

                // Downstream auth — InfraGate:DownstreamAuth section (server-side validation only)
                // GatewayClientId and GatewayClientSecret are intentionally excluded:
                // they are gateway-only credentials and must never reach the server subprocess.
                "InfraGate__DownstreamAuth__Required",
                "InfraGate__DownstreamAuth__Authority",
                "InfraGate__DownstreamAuth__MetadataAddress",
                "InfraGate__DownstreamAuth__RequireHttpsMetadata",
                "InfraGate__DownstreamAuth__Audience",
                "InfraGate__DownstreamAuth__Scope",
            };
    }

    public static class Approvals
    {
        public const string PathPrefix = "/approvals";
        public const string ChallengeRoute = "/approvals/{challengeId}";
        public const string CodeRoute = "/approvals/code";
        public const string ApproveRoute = "/approvals/{challengeId}/approve";
        public const string DenyRoute = "/approvals/{challengeId}/deny";
        public const string CancelRoute = "/approvals/{challengeId}/cancel";
        public const string LoginPath = "/approvals/login";
        public const string LogoutPath = "/approvals/logout";
        public const string CodeFormField = "code";
        public const string RequestVerificationToken = "__RequestVerificationToken";
        public const string ReturnUrlQueryKey = "ReturnUrl";
    }

    public static class ApprovalReasonCodes
    {
        public const string AdapterDecodeFailed = "gateway.approval.adapter_decode_failed";
        public const string ApprovalRequired = "gateway.approval.required";
        public const string AuthenticatedSubjectRequired = "gateway.approval.authenticated_subject_required";
        public const string OperatorGroupRequired = "gateway.approval.operator_group_required";
        public const string PlanExpired = "gateway.approval.plan_expired";
        public const string PlanNotStarted = "gateway.approval.plan_not_started";
        public const string SameSubjectRequired = "gateway.approval.same_subject_required";
    }

    public static class ToolNames
    {
        public const string RequestToolPrefix = "request_";
        public const string ApplyApprovedPlan = "execute_approved_plan";
        public const string GetPlanStatus = "get_plan_status";
        public const string ProposePlan = "propose_plan";
        public const string WaitForPlanApproval = "wait_for_plan_approval";
    }

    public static class ToolScopeRequirements
    {
        public const string MutationScope = "mcp:tools";
        public const string ReadOnlyScope = "mcp:tools.readonly";
        public const string ProposeScope = "mcp:tools.propose";
        public const string ExecuteScope = "mcp:tools.execute";
        public const string ReadScope = "mcp:tools.read";
        public const string WriteScope = "mcp:tools.write";
    }

    public static class ToolArguments
    {
        public const string PlanId = "planId";
        public const string OperationType = "operationType";
        public const string OperationArguments = "arguments";
        public const string TimeoutSeconds = "timeoutSeconds";
    }

    public static class ToolResponseFields
    {
        public const string Status = "status";
        public const string TimedOut = "timedOut";
    }

    public static class ModelVisibleToolResult
    {
        public const string SchemaVersion = ModelVisibleToolResultConventions.SchemaVersion;
        public const string Kind = ModelVisibleToolResultConventions.Kind;
        public const string ToolName = ModelVisibleToolResultConventions.ToolName;
        public const string Source = ModelVisibleToolResultConventions.Source;
        public const string GeneratedAtUtc = ModelVisibleToolResultConventions.GeneratedAtUtc;
        public const string Status = ModelVisibleToolResultConventions.Status;
        public const string Guardrail = ModelVisibleToolResultConventions.Guardrail;
        public const string GuardrailAction = ModelVisibleToolResultConventions.GuardrailAction;
        public const string GuardrailCategories = ModelVisibleToolResultConventions.GuardrailCategories;
        public const string Untrusted = ModelVisibleToolResultConventions.Untrusted;
        public const string UntrustedPayload = ModelVisibleToolResultConventions.UntrustedPayload;

        public const string KindValue = ModelVisibleToolResultConventions.KindValue;
        public const string SourceReadOnlyToolValue = ModelVisibleToolResultConventions.SourceReadOnlyToolValue;
        public const string StatusSuccess = ModelVisibleToolResultConventions.StatusSuccess;
        public const string StatusError = ModelVisibleToolResultConventions.StatusError;
        public const string GuardrailActionAllow = ModelVisibleToolResultConventions.GuardrailActionAllow;
    }

    public static class GuardrailAudit
    {
        public const string OAuthAuthenticationType = "oauth-jwt";
        public const string RequestDirection = "request";
        public const string ResponseDirection = "response";
        public const string WarnAction = "warn";
        public const string WarnRedactAction = "warn_redact";
        public const string RedactManifestAction = "redact_manifest";
        public const string DenyAction = "scope.denied";

        public static class EntryFields
        {
            public const string Timestamp = "timestamp";
            public const string ToolName = "toolName";
            public const string Direction = "direction";
            public const string Action = "action";
            public const string Categories = "categories";
            public const string PlanId = "planId";
            public const string Subject = "subject";
            public const string AuthenticationType = "authenticationType";
            public const string IdentityKind = "identityKind";
        }
    }

    public static class GuardrailCategories
    {
        public const string IgnoreInstructions = "ignore-instructions";
        public const string RevealPrompts = "reveal-prompts";
        public const string ToolUse = "tool-use";
        public const string SecretExfiltration = "secret-exfiltration";
        public const string AuthorityOverride = "authority-override";
        public const string ManifestEchoCategory = "manifest-echo";
        public const string ScopeDenied = "scope";
    }

    public static class GuardrailLocations
    {
        public const string CombinedInput = "request.combined";
        public const string Response = "response";
        public const string ResponseCombined = Response + ".combined";
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
