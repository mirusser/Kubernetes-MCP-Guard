using InfraGate.Approvals;

namespace InfraGate.McpServer;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
internal static class K8sConventions
{
    public const string ServiceName = "infra-gate-mcp";
    public const string DefaultNamespace = "mcp-nginx-demo";
    public const int MaxReplicas = 5;
    public const int DefaultEventLimit = 50;
    public const int MaxEventLimit = 100;
    public const int DefaultLogTailLines = 200;
    public const int MaxLogTailLines = 500;
    public const int LogLimitBytes = 65536;
    public const int MaxDiagnosticsRelatedItems = 50;

    public static class EnvironmentVariables
    {
        public const string KubeConfig = "KUBECONFIG";
        public const string UseInClusterConfig = "K8S_MCP_USE_IN_CLUSTER";
        public const string ApprovalRoot = ApprovalConventions.EnvironmentVariables.ApprovalRoot;
        public const string AllowedNamespaces = "K8S_MCP_ALLOWED_NAMESPACES";
        public const string LogPath = "K8S_MCP_LOG_PATH";
    }

    public static class PlanOperations
    {
        public const string Apply = "apply";
        public const string Delete = "delete";
        public const string Scale = "scale";
        public const string Restart = "restart";
        public const string SetImage = "set-image";
    }

    public static class PlanParameters
    {
        public const string ObjectCount = "objectCount";
        public const string Name = "name";
        public const string Replicas = "replicas";
        public const string RestartedAtUtc = "restartedAtUtc";
        public const string Container = "container";
        public const string CurrentImage = "currentImage";
        public const string Image = "image";
    }

    public static class DryRunPhases
    {
        public const string Request = "request";
        public const string Apply = "apply";
    }

    public static class DryRunStatuses
    {
        public const string Succeeded = "succeeded";
    }

    public static class PlanResponse
    {
        public const string PendingGatewayApproval = "pending_gateway_approval";
        public const string PolicyPassed = "passed";
        public const string PolicyNotApplicable = "not_applicable";
        public const string PolicyPassedWithPrefix = "passed_with_";
        public const string PolicyWarningSuffix = "_warning";
        public const string PolicyWarningsSuffix = "_warnings";
        public const string RiskMedium = "medium";
    }

    // Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
    public static class K8sApi
    {
        public const string DryRunAll = "All";
        public const string FieldValidationStrict = "Strict";
        public const string WarningHeader = "Warning";
    }

    public static class ToolArguments
    {
        public const string PlanId = "planId";
        public const string PodName = "podName";
        public const string Container = "container";
        public const string TailLines = "tailLines";
        public const string Previous = "previous";
        public const string Kind = "kind";
        public const string Name = "name";
        public const string FieldSelector = "fieldSelector";
        public const string Limit = "limit";
        public const string Image = "image";
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

    public static class PolicyCodes
    {
        public const string DeploymentPrivilegedContainer = "DEPLOYMENT_PRIVILEGED_CONTAINER";
        public const string DeploymentHostPath = "DEPLOYMENT_HOST_PATH";
        public const string DeploymentHostNamespace = "DEPLOYMENT_HOST_NAMESPACE";
        public const string DeploymentAddedCapabilities = "DEPLOYMENT_ADDED_CAPABILITIES";
        public const string ImageLatestTag = "IMAGE_LATEST_TAG";
        public const string ServiceLoadBalancer = "SERVICE_LOAD_BALANCER";
        public const string ServiceNodePort = "SERVICE_NODE_PORT";
        public const string ConfigMapSecretLikeKey = "CONFIG_MAP_SECRET_LIKE_KEY";
    }

    public static class LabelSelectorOperators
    {
        public const string In = "In";
        public const string NotIn = "NotIn";
        public const string Exists = "Exists";
        public const string DoesNotExist = "DoesNotExist";
    }

    // Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
    public static class K8sResources
    {
        public const string AppsV1 = "apps/v1";
        public const string V1 = "v1";
        public const string Deployment = "Deployment";
        public const string ReplicaSet = "ReplicaSet";
        public const string Pod = "Pod";
        public const string Service = "Service";
        public const string ConfigMap = "ConfigMap";
        public const string Secret = "Secret";
        public const string RestartedAtAnnotation = "kubectl.kubernetes.io/restartedAt";

        public const string DeploymentTypeKey = AppsV1 + "/" + Deployment;
        public const string ServiceTypeKey = V1 + "/" + Service;
        public const string ConfigMapTypeKey = V1 + "/" + ConfigMap;
        public const string DeploymentDisplayName = AppsV1 + " " + Deployment;
        public const string SupportedKindsDescription =
            DeploymentDisplayName + ", " + V1 + " " + Service + ", " + V1 + " " + ConfigMap;
        public const string SupportedResourceSummaryKindsDescription =
            Deployment + ", " + ReplicaSet + ", " + Pod + ", " + Service + ", " + ConfigMap;

        public static K8sObjectRef DeploymentRef(string namespaceName, string name) =>
            new(AppsV1, Deployment, namespaceName, name);

        public static bool IsDeployment(K8sObjectRef obj) =>
            string.Equals(obj.ApiVersion, AppsV1, StringComparison.Ordinal) &&
            string.Equals(obj.Kind, Deployment, StringComparison.Ordinal);
    }
}
