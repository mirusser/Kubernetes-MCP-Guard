using System.Text.Json;
using InfraGate.Approvals;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace InfraGate.McpServer;

public sealed partial class K8sManager
{
    private async Task<DryRunResult> DryRunApplyManifestAsync(
        IReadOnlyList<IKubernetesObject<V1ObjectMeta>> objects,
        CancellationToken cancellationToken)
    {
        try
        {
            var dryRunObjects = new List<K8sPlanDryRunObject>();
            var warnings = new List<string>();

            foreach (var obj in objects)
            {
                var result = await DryRunApplyObjectAsync(obj, cancellationToken);
                dryRunObjects.Add(result.Object);
                warnings.AddRange(result.Warnings);
            }

            return DryRunResult.Success(dryRunObjects, warnings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DryRunResult.Failed(FormatApiException("Server-side dry-run failed", ex));
        }
    }

    private async Task<DryRunResult> DryRunDeleteManifestAsync(
        IReadOnlyList<K8sObjectRef> objects,
        CancellationToken cancellationToken)
    {
        try
        {
            var dryRunObjects = new List<K8sPlanDryRunObject>();
            var warnings = new List<string>();

            foreach (var obj in objects)
            {
                var result = await DryRunDeleteObjectAsync(obj, cancellationToken);
                dryRunObjects.Add(result.Object);
                warnings.AddRange(result.Warnings);
            }

            return DryRunResult.Success(dryRunObjects, warnings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DryRunResult.Failed(FormatApiException("Server-side dry-run failed", ex));
        }
    }

    private async Task<DryRunResult> DryRunScaleDeploymentAsync(
        string namespaceName,
        string name,
        int replicas,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.AppsV1.PatchNamespacedDeploymentScaleWithHttpMessagesAsync(
                CreateScaleDeploymentPatch(replicas),
                name,
                namespaceName,
                dryRun: K8sConventions.K8sApi.DryRunAll,
                fieldManager: FieldManager,
                fieldValidation: K8sConventions.K8sApi.FieldValidationStrict,
                cancellationToken: cancellationToken);

            var dryRunObject = CaptureDryRunObject(
                K8sConventions.K8sResources.DeploymentRef(namespaceName, name),
                response);

            return DryRunResult.Success([dryRunObject], ExtractWarnings(response));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DryRunResult.Failed(FormatApiException("Server-side dry-run failed", ex));
        }
    }

    private async Task<DryRunResult> DryRunRestartDeploymentAsync(
        string namespaceName,
        string name,
        string restartedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.AppsV1.PatchNamespacedDeploymentWithHttpMessagesAsync(
                CreateRestartDeploymentPatch(restartedAtUtc),
                name,
                namespaceName,
                dryRun: K8sConventions.K8sApi.DryRunAll,
                fieldManager: FieldManager,
                fieldValidation: K8sConventions.K8sApi.FieldValidationStrict,
                cancellationToken: cancellationToken);

            var dryRunObject = CaptureDryRunObject(
                K8sConventions.K8sResources.DeploymentRef(namespaceName, name),
                response);

            return DryRunResult.Success([dryRunObject], ExtractWarnings(response));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DryRunResult.Failed(FormatApiException("Server-side dry-run failed", ex));
        }
    }

    private async Task<DryRunResult> DryRunSetDeploymentImageAsync(
        string namespaceName,
        string name,
        string container,
        string image,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.AppsV1.PatchNamespacedDeploymentWithHttpMessagesAsync(
                CreateSetDeploymentImagePatch(container, image),
                name,
                namespaceName,
                dryRun: K8sConventions.K8sApi.DryRunAll,
                fieldManager: FieldManager,
                fieldValidation: K8sConventions.K8sApi.FieldValidationStrict,
                cancellationToken: cancellationToken);

            var dryRunObject = CaptureDryRunObject(
                K8sConventions.K8sResources.DeploymentRef(namespaceName, name),
                response);

            return DryRunResult.Success([dryRunObject], ExtractWarnings(response));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DryRunResult.Failed(FormatApiException("Server-side dry-run failed", ex));
        }
    }

    private async Task<DryRunObjectResult> DryRunApplyObjectAsync(
        IKubernetesObject<V1ObjectMeta> obj,
        CancellationToken cancellationToken)
    {
        if (obj is V1Deployment deployment)
        {
            using var response = await client.AppsV1.PatchNamespacedDeploymentWithHttpMessagesAsync(
                new V1Patch(deployment, V1Patch.PatchType.ApplyPatch),
                deployment.Metadata.Name,
                deployment.Metadata.NamespaceProperty,
                dryRun: K8sConventions.K8sApi.DryRunAll,
                fieldManager: FieldManager,
                fieldValidation: K8sConventions.K8sApi.FieldValidationStrict,
                force: true,
                cancellationToken: cancellationToken);

            return CaptureDryRunResult(deployment, response);
        }

        if (obj is V1Service service)
        {
            using var response = await client.CoreV1.PatchNamespacedServiceWithHttpMessagesAsync(
                new V1Patch(service, V1Patch.PatchType.ApplyPatch),
                service.Metadata.Name,
                service.Metadata.NamespaceProperty,
                dryRun: K8sConventions.K8sApi.DryRunAll,
                fieldManager: FieldManager,
                fieldValidation: K8sConventions.K8sApi.FieldValidationStrict,
                force: true,
                cancellationToken: cancellationToken);

            return CaptureDryRunResult(service, response);
        }

        if (obj is V1ConfigMap configMap)
        {
            using var response = await client.CoreV1.PatchNamespacedConfigMapWithHttpMessagesAsync(
                new V1Patch(configMap, V1Patch.PatchType.ApplyPatch),
                configMap.Metadata.Name,
                configMap.Metadata.NamespaceProperty,
                dryRun: K8sConventions.K8sApi.DryRunAll,
                fieldManager: FieldManager,
                fieldValidation: K8sConventions.K8sApi.FieldValidationStrict,
                force: true,
                cancellationToken: cancellationToken);

            return CaptureDryRunResult(configMap, response);
        }

        throw new InvalidOperationException("Unsupported object for server-side dry-run.");
    }

    private async Task<DryRunObjectResult> DryRunDeleteObjectAsync(
        K8sObjectRef obj,
        CancellationToken cancellationToken)
    {
        switch (obj.ApiVersion, obj.Kind)
        {
            case (K8sConventions.K8sResources.AppsV1, K8sConventions.K8sResources.Deployment):
            {
                using var response = await client.AppsV1.DeleteNamespacedDeploymentWithHttpMessagesAsync(
                    obj.Name,
                    obj.Namespace,
                    body: new V1DeleteOptions(),
                    dryRun: K8sConventions.K8sApi.DryRunAll,
                    cancellationToken: cancellationToken);

                return CaptureDryRunResult(obj, response);
            }
            case (K8sConventions.K8sResources.V1, K8sConventions.K8sResources.Service):
            {
                using var response = await client.CoreV1.DeleteNamespacedServiceWithHttpMessagesAsync(
                    obj.Name,
                    obj.Namespace,
                    body: new V1DeleteOptions(),
                    dryRun: K8sConventions.K8sApi.DryRunAll,
                    cancellationToken: cancellationToken);

                return CaptureDryRunResult(obj, response);
            }
            case (K8sConventions.K8sResources.V1, K8sConventions.K8sResources.ConfigMap):
            {
                using var response = await client.CoreV1.DeleteNamespacedConfigMapWithHttpMessagesAsync(
                    obj.Name,
                    obj.Namespace,
                    body: new V1DeleteOptions(),
                    dryRun: K8sConventions.K8sApi.DryRunAll,
                    cancellationToken: cancellationToken);

                return CaptureDryRunResult(obj, response);
            }
            default:
                throw new InvalidOperationException($"Unsupported object for server-side dry-run: {FormatObjectRef(obj)}.");
        }
    }

    private static V1Patch CreateScaleDeploymentPatch(int replicas) =>
        new(new
        {
            spec = new
            {
                replicas
            }
        }, V1Patch.PatchType.MergePatch);

    private static V1Patch CreateRestartDeploymentPatch(string restartedAtUtc) =>
        new(new
        {
            spec = new
            {
                template = new
                {
                    metadata = new
                    {
                        annotations = new Dictionary<string, string>
                        {
                            [RestartedAtAnnotation] = restartedAtUtc
                        }
                    }
                }
            }
        }, V1Patch.PatchType.StrategicMergePatch);

    private static V1Patch CreateSetDeploymentImagePatch(string container, string image) =>
        new(new
        {
            spec = new
            {
                template = new
                {
                    spec = new
                    {
                        containers = new[]
                        {
                            new
                            {
                                name = container,
                                image
                            }
                        }
                    }
                }
            }
        }, V1Patch.PatchType.StrategicMergePatch);

    private static DryRunObjectResult CaptureDryRunResult<T>(
        IKubernetesObject<V1ObjectMeta> obj,
        IHttpOperationResponse<T> response) =>
        new(
            CaptureDryRunObject(FormatObjectRef(obj), response),
            ExtractWarnings(response));

    private static DryRunObjectResult CaptureDryRunResult<T>(
        K8sObjectRef obj,
        IHttpOperationResponse<T> response) =>
        new(
            CaptureDryRunObject(obj, response),
            ExtractWarnings(response));

    private static K8sPlanDryRunObject CaptureDryRunObject<T>(
        K8sObjectRef obj,
        IHttpOperationResponse<T> response) =>
        CaptureDryRunObject(FormatObjectRef(obj), response);

    private static K8sPlanDryRunObject CaptureDryRunObject<T>(
        string obj,
        IHttpOperationResponse<T> response) =>
        new(obj, JsonSerializer.Serialize(response.Body, JsonOptions));

    private static string[] ExtractWarnings<T>(IHttpOperationResponse<T> response) =>
        response.Response.Headers.TryGetValues(K8sConventions.K8sApi.WarningHeader, out var values)
            ? values.ToArray()
            : [];

    private static string FormatObjectRef(IKubernetesObject<V1ObjectMeta> obj) =>
        $"{obj.ApiVersion} {obj.Kind} {obj.Metadata.NamespaceProperty}/{obj.Metadata.Name}";

    private static string FormatObjectRef(K8sObjectRef obj) =>
        $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}";

    private sealed record DryRunObjectResult(
        K8sPlanDryRunObject Object,
        string[] Warnings);

    private sealed record DryRunResult(
        bool Succeeded,
        K8sPlanDryRun? DryRun,
        string Message)
    {
        public static DryRunResult Success(
            IReadOnlyList<K8sPlanDryRunObject> objects,
            IEnumerable<string> warnings)
        {
            const string message = "Server-side dry-run succeeded.";
            var dryRun = new K8sPlanDryRun(
                K8sConventions.DryRunStatuses.Succeeded,
                DateTimeOffset.UtcNow,
                objects.ToArray(),
                warnings.Distinct(StringComparer.Ordinal).ToArray(),
                message);

            return new DryRunResult(true, dryRun, message);
        }

        public static DryRunResult Failed(string message) => new(false, null, message);
    }
}
