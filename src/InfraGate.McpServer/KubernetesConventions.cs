using InfraGate.DownstreamAuth;
using InfraGate.McpServer.Models;

namespace InfraGate.McpServer;

internal static class KubernetesConventions
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
        public const string KubeConfig = "InfraGate__Kubernetes__KubeConfig";
        public const string UseInClusterConfig = "InfraGate__Kubernetes__UseInClusterConfig";
        public const string AllowedNamespaces = "InfraGate__Kubernetes__AllowedNamespaces";
        public const string LogPath = "InfraGate__Kubernetes__LogPath";
    }

    public static class ConfigurationKeys
    {
        public const string AllowedNamespaces = "InfraGate:Kubernetes:AllowedNamespaces";
        public const string KubeConfig = "InfraGate:Kubernetes:KubeConfig";
        public const string LogPath = "InfraGate:Kubernetes:LogPath";
        public const string UseInClusterConfig = "InfraGate:Kubernetes:UseInClusterConfig";
    }

    public static class DiffChangeTypes
    {
        public const string Create = "create";
        public const string Update = "update";
        public const string Delete = "delete";
        public const string NoOp = "no-op";
    }

    public static class DateTimeFormats
    {
        public const string RoundTrip = "O";
    }

    public static class PolicyCodes
    {
        public const string DeploymentPrivilegedContainer = "DEPLOYMENT_PRIVILEGED_CONTAINER";
        public const string DeploymentHostNamespace = "DEPLOYMENT_HOST_NAMESPACE";
        public const string DeploymentHostPath = "DEPLOYMENT_HOST_PATH";
        public const string DeploymentAddedCapabilities = "DEPLOYMENT_ADDED_CAPABILITIES";
        public const string ImageLatestTag = "IMAGE_LATEST_TAG";
        public const string ServiceLoadBalancer = "SERVICE_LOAD_BALANCER";
        public const string ServiceNodePort = "SERVICE_NODE_PORT";
    }

    public static class ExecutionMessages
    {
        public const string PolicyRefusal = "Apply refused by policy:";
        public const string ApplySuccess = "Applied";
    }

    public static class MutationOperations
    {
        public const string Apply = "apply";
        public const string Delete = "delete";
        public const string Scale = "scale";
        public const string Restart = "restart";
        public const string SetImage = "set-image";
    }

    public static class DryRunStatuses
    {
        public const string Succeeded = "succeeded";
    }

    public static class DriftCheckResult
    {
        public const string NoDrift = "ok";
    }

    public static class KubernetesApi
    {
        public const string DryRunAll = "All";
        public const string FieldValidationStrict = "Strict";
        public const string WarningHeader = "Warning";
    }

    public static class ToolArguments
    {
        public const string PodName = "podName";
        public const string Container = "container";
        public const string TailLines = "tailLines";
        public const string Previous = "previous";
        public const string Kind = "kind";
        public const string Name = "name";
        public const string FieldSelector = "fieldSelector";
        public const string Limit = "limit";
        public const string Image = "image";
        public const string Manifest = "manifest";
        public const string Namespace = "namespace";
        public const string Replicas = "replicas";
        public const string Operation = "operation";
        public const string DiffsJson = "diffsJson";
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
        public const string GetAllowedNamespaces = "get_allowed_namespaces";

        public const string DryRunApplyManifest = "dry_run_apply_manifest";
        public const string DryRunDeleteManifest = "dry_run_delete_manifest";
        public const string DryRunScaleDeployment = "dry_run_scale_deployment";
        public const string DryRunRestartDeployment = "dry_run_restart_deployment";
        public const string DryRunSetDeploymentImage = "dry_run_set_deployment_image";
        public const string DiffManifest = "diff_manifest";
        public const string CheckLiveDrift = "check_live_drift";
        public const string DiffDeployment = "diff_deployment";

        public const string ApplyManifest = "apply_manifest";
        public const string DeleteManifest = "delete_manifest";
        public const string ScaleDeployment = "scale_deployment";
        public const string RestartDeployment = "restart_deployment";
        public const string SetDeploymentImage = "set_deployment_image";
    }

    public static class LabelSelectorOperators
    {
        public const string In = "In";
        public const string NotIn = "NotIn";
        public const string Exists = "Exists";
        public const string DoesNotExist = "DoesNotExist";
    }

    public static class KubernetesResources
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

        public static KubernetesObjectRef DeploymentRef(string namespaceName, string name) =>
            new(AppsV1, Deployment, namespaceName, name);

        public static bool IsDeployment(KubernetesObjectRef obj) =>
            string.Equals(obj.ApiVersion, AppsV1, StringComparison.Ordinal) &&
            string.Equals(obj.Kind, Deployment, StringComparison.Ordinal);
    }
}
