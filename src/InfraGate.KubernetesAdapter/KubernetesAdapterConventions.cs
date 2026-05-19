namespace InfraGate.KubernetesAdapter;

public static class KubernetesAdapterConventions
{
    public const string AdapterId = "kubernetes";

    public static class Canonicalizations
    {
        public const string IntentV1 = "infra-gate.kubernetes.intent.v1";
        public const string DryRunEvidenceV1 = "infra-gate.kubernetes.evidence.dry-run.v1";
        public const string DiffEvidenceV1 = "infra-gate.kubernetes.evidence.diff.v1";
        public const string PolicyFindingsEvidenceV1 = "infra-gate.kubernetes.evidence.policy-findings.v1";
    }

    public static class ReviewRenderers
    {
        public const string PlanReviewV1 = "infra-gate.kubernetes.review.v1";
    }

    public static class FreshnessCheckTypes
    {
        public const string LiveDrift = "kubernetes.live-drift";
        public const string PreExecuteDryRun = "kubernetes.pre-execute-dry-run";
    }

    public static class EvidenceArtifactTypes
    {
        public const string DryRun = "kubernetes.dry-run";
        public const string Diff = "kubernetes.diff";
        public const string PolicyFindings = "kubernetes.policy-findings";
    }

    public static class MutationTools
    {
        public const string ApplyManifest = "apply_manifest";
        public const string DeleteManifest = "delete_manifest";
        public const string ScaleDeployment = "scale_deployment";
        public const string RestartDeployment = "restart_deployment";
        public const string SetDeploymentImage = "set_deployment_image";
    }

    public static class EvidenceTools
    {
        public const string DryRunApplyManifest = "dry_run_apply_manifest";
        public const string DryRunDeleteManifest = "dry_run_delete_manifest";
        public const string DryRunScaleDeployment = "dry_run_scale_deployment";
        public const string DryRunRestartDeployment = "dry_run_restart_deployment";
        public const string DryRunSetDeploymentImage = "dry_run_set_deployment_image";
        public const string DiffManifest = "diff_manifest";
        public const string DiffDeployment = "diff_deployment";
        public const string CheckLiveDrift = "check_live_drift";
    }

    public static class EvidenceArguments
    {
        public const string Namespace = ToolArguments.Namespace;
        public const string Manifest = ToolArguments.Manifest;
        public const string Name = ToolArguments.Name;
        public const string Replicas = ToolArguments.Replicas;
        public const string Container = ToolArguments.Container;
        public const string Image = ToolArguments.Image;
        public const string Operation = "operation";
        public const string DiffsJson = "diffsJson";
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
        public const string Replicas = "replicas";
        public const string Container = "container";
        public const string TailLines = "tailLines";
        public const string Previous = "previous";
        public const string Limit = "limit";
        public const string Image = "image";
    }

    public static class DriftCheckResults
    {
        public const string NoDrift = "ok";
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
}
