namespace InfraGate.McpGateway;

internal static class McpGatewayConventions
{
    public const string DefaultUrl = "http://127.0.0.1:3001";
    public const string DefaultOAuthResource = "http://127.0.0.1:3001/mcp";
    public const string DefaultOAuthScope = "mcp:tools";
    public const string McpPath = "/mcp";
    public const string AuthorizationScheme = "Bearer";

    public static class ConfigurationKeys
    {
        public const string Urls = "urls";
    }

    public static class EnvironmentVariables
    {
        public const string AspNetCoreUrls = "ASPNETCORE_URLS";
        public const string BearerToken = "INFRA_GATE_GATEWAY_BEARER_TOKEN";
        public const string DownstreamProject = "INFRA_GATE_DOWNSTREAM_PROJECT";
        public const string GuardAuditRoot = "INFRA_GATE_GUARD_AUDIT_ROOT";
        public const string OAuthAuthority = "INFRA_GATE_OAUTH_AUTHORITY";
        public const string OAuthResource = "INFRA_GATE_OAUTH_RESOURCE";
        public const string OAuthScope = "INFRA_GATE_OAUTH_SCOPE";
        public const string OAuthRequireHttpsMetadata = "INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA";
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
        public const string DeclineAction = "decline";
    }

    public static class ToolNames
    {
        public const string GetK8sStatus = "get_k8s_status";
        public const string RequestApplyManifest = "request_apply_manifest";
        public const string RequestDeleteManifest = "request_delete_manifest";
        public const string RequestScaleDeployment = "request_scale_deployment";
        public const string RequestRestartDeployment = "request_restart_deployment";
        public const string ApplyApprovedPlan = "apply_approved_plan";
    }

    public static class ToolArguments
    {
        public const string Namespace = "namespace";
        public const string LabelSelector = "labelSelector";
        public const string Manifest = "manifest";
        public const string Name = "name";
        public const string Replicas = "replicas";
        public const string PlanId = "planId";
    }

    public static class GuardrailAudit
    {
        public const string LocalBearerSubject = "local-bearer-demo";
        public const string StaticBearerAuthenticationType = "static-bearer";
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

    public static class Authentication
    {
        public const string PolicyName = "InfraGateMcpGateway";
        public const string PolicyScheme = "InfraGateGatewayBearer";
        public const string StaticBearerScheme = "InfraGateStaticBearer";
        public const string ProtectedResourceMetadataPath = "/.well-known/oauth-protected-resource";
        public const string ResourceName = "InfraGate MCP Gateway";
        public const string ScopeClaim = "scope";
        public const string ScpClaim = "scp";
        public const string PreferredUsernameClaim = "preferred_username";
        public const string EmailClaim = "email";
        public const string SubjectClaim = "sub";
        public const string ClientIdClaim = "client_id";
    }
}
