namespace InfraGate.McpGateway;

internal static class McpGatewayConventions
{
    public const string DefaultUrl = "http://127.0.0.1:3001";
    public const string McpPath = "/mcp";
    public const int DefaultEventLimit = 50;
    public const int DefaultLogTailLines = 200;
    internal const int RegexTimeoutMilliseconds = 1000;

    public static class ConfigurationKeys
    {
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
    }

    public static class Approvals
    {
        public const string PathPrefix = "/approvals";
        public const string ChallengeRoute = "/approvals/{challengeId}";
        public const string ApproveRoute = "/approvals/{challengeId}/approve";
        public const string DenyRoute = "/approvals/{challengeId}/deny";
        public const string LoginPath = "/approvals/login";
        public const string RequestVerificationToken = "__RequestVerificationToken";
    }

    public static class ApprovalChallengeStatuses
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Denied = "denied";
        public const string Expired = "expired";
    }

    public static class ToolNames
    {
        public const string GetK8sStatus = "get_k8s_status";
        public const string GetK8sEvents = "get_k8s_events";
        public const string GetPodLogs = "get_pod_logs";
        public const string GetK8sResource = "get_k8s_resource";
        public const string GetDeploymentDiagnostics = "get_deployment_diagnostics";
        public const string GetPodDiagnostics = "get_pod_diagnostics";
        public const string GetServiceDiagnostics = "get_service_diagnostics";
        public const string RequestApplyManifest = "request_apply_manifest";
        public const string RequestDeleteManifest = "request_delete_manifest";
        public const string RequestScaleDeployment = "request_scale_deployment";
        public const string RequestRestartDeployment = "request_restart_deployment";
        public const string RequestSetDeploymentImage = "request_set_deployment_image";
        public const string ApplyApprovedPlan = "apply_approved_plan";
        public const string GetAllowedNamespaces = "get_allowed_namespaces";
    }

    public static class ToolArguments
    {
        public const string Namespace = "namespace";
        public const string LabelSelector = "labelSelector";
        public const string FieldSelector = "fieldSelector";
        public const string Manifest = "manifest";
        public const string Kind = "kind";
        public const string Name = "name";
        public const string PodName = "podName";
        public const string Container = "container";
        public const string TailLines = "tailLines";
        public const string Previous = "previous";
        public const string Limit = "limit";
        public const string Replicas = "replicas";
        public const string Image = "image";
        public const string PlanId = "planId";
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
    }

}
