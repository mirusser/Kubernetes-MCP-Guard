using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Execution;

internal static class OperationDispatchMap
{
    private static readonly IReadOnlyDictionary<string, OperationDispatch> dispatches =
        new Dictionary<string, OperationDispatch>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.PlanOperations.Apply] = new(
                KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest,
                KubernetesAdapterConventions.MutationTools.ApplyManifest,
                BuildManifestArgs),
            [KubernetesAdapterConventions.PlanOperations.Delete] = new(
                KubernetesAdapterConventions.EvidenceTools.DryRunDeleteManifest,
                KubernetesAdapterConventions.MutationTools.DeleteManifest,
                BuildManifestArgs),
            [KubernetesAdapterConventions.PlanOperations.Scale] = new(
                KubernetesAdapterConventions.EvidenceTools.DryRunScaleDeployment,
                KubernetesAdapterConventions.MutationTools.ScaleDeployment,
                BuildScaleArgs),
            [KubernetesAdapterConventions.PlanOperations.Restart] = new(
                KubernetesAdapterConventions.EvidenceTools.DryRunRestartDeployment,
                KubernetesAdapterConventions.MutationTools.RestartDeployment,
                BuildRestartArgs),
            [KubernetesAdapterConventions.PlanOperations.SetImage] = new(
                KubernetesAdapterConventions.EvidenceTools.DryRunSetDeploymentImage,
                KubernetesAdapterConventions.MutationTools.SetDeploymentImage,
                BuildSetImageArgs)
        };

    public static bool TryGetValue(string operation, out OperationDispatch? dispatch) =>
        dispatches.TryGetValue(operation, out dispatch);

    private static Dictionary<string, object?> BuildManifestArgs(KubernetesPlanPayload payload) =>
        new(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
            [KubernetesAdapterConventions.EvidenceArguments.Manifest] = payload.Manifest ?? string.Empty
        };

    private static Dictionary<string, object?> BuildScaleArgs(KubernetesPlanPayload payload) =>
        new(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
            [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty),
            [KubernetesAdapterConventions.EvidenceArguments.Replicas] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Replicas, "0")
        };

    private static Dictionary<string, object?> BuildRestartArgs(KubernetesPlanPayload payload) =>
        new(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
            [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty)
        };

    private static Dictionary<string, object?> BuildSetImageArgs(KubernetesPlanPayload payload) =>
        new(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
            [KubernetesAdapterConventions.EvidenceArguments.Name] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Name, string.Empty),
            [KubernetesAdapterConventions.EvidenceArguments.Container] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Container, string.Empty),
            [KubernetesAdapterConventions.EvidenceArguments.Image] = payload.Parameters.GetValueOrDefault(KubernetesAdapterConventions.PlanParameters.Image, string.Empty)
        };
}
