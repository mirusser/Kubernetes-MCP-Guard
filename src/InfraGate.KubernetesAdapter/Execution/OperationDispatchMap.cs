using System.Text.Json;
using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Execution;

internal static class OperationDispatchMap
{
    private static readonly JsonSerializerOptions resourceVersionJsonOptions = new(JsonSerializerDefaults.Web);

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

    private static void ValidateRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} must not be null or empty.", parameterName);
    }

    private static Dictionary<string, object?> BuildManifestArgs(KubernetesPlanPayload payload)
    {
        ValidateRequired(payload.Namespace, nameof(payload.Namespace));
        ValidateRequired(payload.Manifest, nameof(payload.Manifest));

        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
            [KubernetesAdapterConventions.EvidenceArguments.Manifest] = payload.Manifest
        };

        if (payload.Diffs.Length > 0)
        {
            var resourceVersions = payload.Diffs
                .Where(d => d.ResourceVersion is not null)
                .Select(d => new { Key = $"{d.Object.ApiVersion} {d.Object.Kind} {d.Object.Namespace}/{d.Object.Name}", d.ResourceVersion })
                .ToList();

            if (resourceVersions.Count > 0)
            {
                args[KubernetesAdapterConventions.EvidenceArguments.ResourceVersions] =
                    JsonSerializer.Serialize(resourceVersions, resourceVersionJsonOptions);
            }
        }

        return args;
    }

    private static Dictionary<string, object?> BuildScaleArgs(KubernetesPlanPayload payload)
    {
        ValidateRequired(payload.Namespace, nameof(payload.Namespace));

        if (!payload.Parameters.TryGetValue(KubernetesAdapterConventions.PlanParameters.Name, out var name)
            || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Name}\"] must not be null or empty.",
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Name}\"]");

        if (!payload.Parameters.TryGetValue(KubernetesAdapterConventions.PlanParameters.Replicas, out var replicas)
            || string.IsNullOrWhiteSpace(replicas))
            throw new ArgumentException(
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Replicas}\"] must not be null or empty.",
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Replicas}\"]");

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
            [KubernetesAdapterConventions.EvidenceArguments.Name] = name,
            [KubernetesAdapterConventions.EvidenceArguments.Replicas] = replicas
        };
    }

    private static Dictionary<string, object?> BuildRestartArgs(KubernetesPlanPayload payload)
    {
        ValidateRequired(payload.Namespace, nameof(payload.Namespace));

        if (!payload.Parameters.TryGetValue(KubernetesAdapterConventions.PlanParameters.Name, out var name)
            || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Name}\"] must not be null or empty.",
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Name}\"]");

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
            [KubernetesAdapterConventions.EvidenceArguments.Name] = name
        };
    }

    private static Dictionary<string, object?> BuildSetImageArgs(KubernetesPlanPayload payload)
    {
        ValidateRequired(payload.Namespace, nameof(payload.Namespace));

        if (!payload.Parameters.TryGetValue(KubernetesAdapterConventions.PlanParameters.Name, out var name)
            || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Name}\"] must not be null or empty.",
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Name}\"]");

        if (!payload.Parameters.TryGetValue(KubernetesAdapterConventions.PlanParameters.Container, out var container)
            || string.IsNullOrWhiteSpace(container))
            throw new ArgumentException(
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Container}\"] must not be null or empty.",
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Container}\"]");

        if (!payload.Parameters.TryGetValue(KubernetesAdapterConventions.PlanParameters.Image, out var image)
            || string.IsNullOrWhiteSpace(image))
            throw new ArgumentException(
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Image}\"] must not be null or empty.",
                $"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Image}\"]");

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = payload.Namespace,
            [KubernetesAdapterConventions.EvidenceArguments.Name] = name,
            [KubernetesAdapterConventions.EvidenceArguments.Container] = container,
            [KubernetesAdapterConventions.EvidenceArguments.Image] = image
        };
    }
}
