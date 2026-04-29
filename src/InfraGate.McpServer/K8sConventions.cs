namespace InfraGate.McpServer;

internal static class K8sConventions
{
    public const string ServiceName = "infra-gate-mcp";
    public const string DefaultNamespace = "mcp-nginx-demo";
    public const int MaxReplicas = 5;

    public static class EnvironmentVariables
    {
        public const string KubeConfig = "KUBECONFIG";
        public const string ApprovalRoot = "K8S_MCP_APPROVAL_ROOT";
        public const string AllowedNamespaces = "K8S_MCP_ALLOWED_NAMESPACES";
    }

    public static class ApprovalStorage
    {
        public const string PendingDirectory = "pending";
        public const string ApprovedDirectory = "approved";
        public const string AppliedDirectory = "applied";
        public const string AuditFileName = "audit.jsonl";
        public const string DefaultRootDirectory = ".mcp-approvals";
        public const string JsonExtension = ".json";
        public const string Sha256Extension = ".sha256";
    }

    public static class AuditEvents
    {
        public const string PlanRequested = "plan_requested";
        public const string ApprovalHashMismatch = "approval_hash_mismatch";
        public const string PlanApproved = "plan_approved";
        public const string PlanApplied = "plan_applied";
        public const string ApplyDenied = "apply_denied";
        public const string ApplyFailed = "apply_failed";
    }

    public static class ApprovalSources
    {
        public const string McpElicitation = "mcp_elicitation";
    }

    public static class DateTimeFormats
    {
        public const string PlanIdTimestamp = "yyyyMMddHHmmss";
        public const string RoundTrip = "O";
    }

    public static class PlanOperations
    {
        public const string Apply = "apply";
        public const string Delete = "delete";
        public const string Scale = "scale";
        public const string Restart = "restart";
    }

    public static class PlanParameters
    {
        public const string ObjectCount = "objectCount";
        public const string Name = "name";
        public const string Replicas = "replicas";
        public const string RestartedAtUtc = "restartedAtUtc";
    }

    public static class ToolArguments
    {
        public const string PlanId = "planId";
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

    public static class K8sResources
    {
        public const string AppsV1 = "apps/v1";
        public const string V1 = "v1";
        public const string Deployment = "Deployment";
        public const string Service = "Service";
        public const string ConfigMap = "ConfigMap";
        public const string RestartedAtAnnotation = "kubectl.kubernetes.io/restartedAt";

        public const string DeploymentTypeKey = AppsV1 + "/" + Deployment;
        public const string ServiceTypeKey = V1 + "/" + Service;
        public const string ConfigMapTypeKey = V1 + "/" + ConfigMap;
        public const string DeploymentDisplayName = AppsV1 + " " + Deployment;
        public const string SupportedKindsDescription =
            DeploymentDisplayName + ", " + V1 + " " + Service + ", " + V1 + " " + ConfigMap;

        public static K8sObjectRef DeploymentRef(string namespaceName, string name) =>
            new(AppsV1, Deployment, namespaceName, name);

        public static bool IsDeployment(K8sObjectRef obj) =>
            string.Equals(obj.ApiVersion, AppsV1, StringComparison.Ordinal) &&
            string.Equals(obj.Kind, Deployment, StringComparison.Ordinal);
    }
}
