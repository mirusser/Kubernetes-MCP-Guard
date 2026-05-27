using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.Evidence;
using InfraGate.KubernetesAdapter.PlanBuilding;
using InfraGate.KubernetesAdapter.Policy;
using InfraGate.McpServer.Diff;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpServer;

public sealed class KubernetesEvidenceService
{
    private const string DryRunFailedMessage = "Server-side dry-run failed";

    private readonly IKubernetes client;
    private readonly ILogger<KubernetesEvidenceService> logger;
    private readonly KubernetesMcpOptions options;

    public KubernetesEvidenceService(IKubernetes client, ILogger<KubernetesEvidenceService> logger, KubernetesMcpOptions options)
    {
        this.client = client;
        this.logger = logger;
        this.options = options;
    }

    public async Task<string> EvidenceDryRunApplyManifestAsync(
        string namespaceName,
        string manifest,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        KubernetesParsedManifest parsed;
        try
        {
            parsed = KubernetesManifestParser.ParseSupported(manifest, namespaceName);
        }
        catch (KubernetesValidationException ex)
        {
            return ex.Message;
        }

        var policyResult = KubernetesPolicyValidator.Validate(parsed.Objects, KubernetesPolicyOptions.Default);
        var policyFindings = policyResult.Findings
            .Select(f => new KubernetesPlanPolicyFinding(f.Severity.ToString(), f.Code, f.ObjectRef, f.Message))
            .ToArray();

        var dryRunResult = await DryRunApplyManifestAsync(parsed.Objects, cancellationToken).ConfigureAwait(false);
        if (!dryRunResult.Succeeded || dryRunResult.DryRun is null)
        {
            return dryRunResult.Message;
        }

        var evidence = new KubernetesApplyEvidence(
            dryRunResult.DryRun,
            policyFindings,
            policyResult.IsDenied,
            policyResult.IsDenied ? policyResult.FormatRefusal() : null);

        return JsonSerializer.Serialize(evidence, KubernetesManagerHelpers.JsonOptions);
    }

    public async Task<string> EvidenceDryRunDeleteManifestAsync(
        string namespaceName,
        string manifest,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        KubernetesParsedManifest parsed;
        try
        {
            parsed = KubernetesManifestParser.ParseSupported(manifest, namespaceName);
        }
        catch (KubernetesValidationException ex)
        {
            return ex.Message;
        }

        var result = await DryRunDeleteManifestAsync(parsed.ObjectRefs, cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.DryRun is not null
            ? JsonSerializer.Serialize(result.DryRun, KubernetesManagerHelpers.JsonOptions)
            : result.Message;
    }

    public async Task<string> EvidenceDryRunScaleDeploymentAsync(
        string namespaceName,
        string name,
        int replicas,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName) ?? KubernetesManagerHelpers.ValidateName(name) ?? KubernetesManagerHelpers.ValidateReplicas(replicas);
        if (validation is not null)
        {
            return validation;
        }

        var result = await DryRunScaleDeploymentAsync(namespaceName, name, replicas, cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.DryRun is not null
            ? JsonSerializer.Serialize(result.DryRun, KubernetesManagerHelpers.JsonOptions)
            : result.Message;
    }

    public async Task<string> EvidenceDryRunRestartDeploymentAsync(
        string namespaceName,
        string name,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName) ?? KubernetesManagerHelpers.ValidateName(name);
        if (validation is not null)
        {
            return validation;
        }

        var restartedAtUtc = DateTimeOffset.UtcNow.ToString(ApprovalConventions.DateTimeFormats.RoundTrip);
        var result = await DryRunRestartDeploymentAsync(namespaceName, name, restartedAtUtc, cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.DryRun is not null
            ? JsonSerializer.Serialize(result.DryRun, KubernetesManagerHelpers.JsonOptions)
            : result.Message;
    }

    public async Task<string> EvidenceDryRunSetDeploymentImageAsync(
        string namespaceName,
        string name,
        string container,
        string image,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName) ??
            KubernetesManagerHelpers.ValidateName(name) ??
            KubernetesManagerHelpers.ValidateRequiredText(container, "Container name") ??
            KubernetesManagerHelpers.ValidateRequiredText(image, "Image");
        if (validation is not null)
        {
            return validation;
        }

        var result = await DryRunSetDeploymentImageAsync(namespaceName, name, container, image, cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.DryRun is not null
            ? JsonSerializer.Serialize(result.DryRun, KubernetesManagerHelpers.JsonOptions)
            : result.Message;
    }

    public async Task<string> EvidenceCheckLiveDriftAsync(
        string namespaceName,
        string operation,
        string diffsJson,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        KubernetesPlanDiff[]? diffs;
        try
        {
            diffs = JsonSerializer.Deserialize<KubernetesPlanDiff[]>(diffsJson, KubernetesManagerHelpers.JsonOptions);
        }
        catch (JsonException ex)
        {
            return $"Could not parse diffs: {ex.Message}";
        }

        if (diffs is null || diffs.Length == 0)
        {
            return "Recorded diff data is empty.";
        }

        try
        {
            var drift = await KubernetesDiffService.FindDriftAsync(client, operation, diffs, cancellationToken).ConfigureAwait(false);
            return drift ?? KubernetesConventions.DriftCheckResult.NoDrift;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Drift check failed for {Operation} in {Namespace}", operation, namespaceName);
            return KubernetesManagerHelpers.FormatApiException("Drift check failed", ex);
        }
    }

    public async Task<string> EvidenceDiffManifestAsync(
        string namespaceName,
        string manifest,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        KubernetesParsedManifest parsed;
        try
        {
            parsed = KubernetesManifestParser.ParseSupported(manifest, namespaceName);
        }
        catch (KubernetesValidationException ex)
        {
            return ex.Message;
        }

        var dryRunResult = await DryRunApplyManifestAsync(parsed.Objects, cancellationToken).ConfigureAwait(false);
        if (!dryRunResult.Succeeded || dryRunResult.DryRun is null)
        {
            return dryRunResult.Message;
        }

        try
        {
            var diffs = await KubernetesDiffService.BuildDiffsAsync(
                client,
                KubernetesConventions.MutationOperations.Apply,
                parsed.ObjectRefs,
                dryRunResult.DryRun.Objects,
                cancellationToken).ConfigureAwait(false);

            return JsonSerializer.Serialize(diffs, KubernetesManagerHelpers.JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Diff generation failed for manifest in namespace {Namespace}", namespaceName);
            return KubernetesManagerHelpers.FormatApiException("Diff generation failed", ex);
        }
    }

    public async Task<string> EvidenceDiffDeploymentAsync(
        string namespaceName,
        string name,
        string operation,
        int? replicas,
        string? container,
        string? image,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            DryRunResult dryRunResult = operation switch
            {
                KubernetesConventions.MutationOperations.Scale
                    => await DryRunScaleDeploymentAsync(namespaceName, name, replicas ?? 1, cancellationToken).ConfigureAwait(false),
                KubernetesConventions.MutationOperations.Restart
                    => await DryRunRestartDeploymentAsync(
                        namespaceName, name,
                        DateTimeOffset.UtcNow.ToString(ApprovalConventions.DateTimeFormats.RoundTrip),
                        cancellationToken).ConfigureAwait(false),
                KubernetesConventions.MutationOperations.SetImage
                    => await DryRunSetDeploymentImageAsync(namespaceName, name, container ?? string.Empty, image ?? string.Empty, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported operation '{operation}' for deployment diff.")
            };

            if (!dryRunResult.Succeeded || dryRunResult.DryRun is null)
            {
                return dryRunResult.Message;
            }

            var obj = KubernetesConventions.KubernetesResources.DeploymentRef(namespaceName, name);
            var diffs = await KubernetesDiffService.BuildDiffsAsync(
                client,
                operation,
                [obj],
                dryRunResult.DryRun.Objects,
                cancellationToken).ConfigureAwait(false);

            return JsonSerializer.Serialize(diffs, KubernetesManagerHelpers.JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Deployment diff generation failed for {Namespace}/{Name}", namespaceName, name);
            return KubernetesManagerHelpers.FormatApiException("Deployment diff generation failed", ex);
        }
    }

    private async Task<DryRunResult> DryRunApplyManifestAsync(
        IReadOnlyList<IKubernetesObject<V1ObjectMeta>> objects,
        CancellationToken cancellationToken)
    {
        try
        {
            var dryRunObjects = new List<KubernetesPlanDryRunObject>();
            var warnings = new List<string>();

            foreach (var obj in objects)
            {
                var result = await DryRunApplyObjectAsync(obj, cancellationToken).ConfigureAwait(false);
                dryRunObjects.Add(result.Object);
                warnings.AddRange(result.Warnings);
            }

            return DryRunResult.Success(dryRunObjects, warnings);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Server-side dry-run apply failed for {ObjectCount} object(s)", objects.Count);
            return DryRunResult.Failed(KubernetesManagerHelpers.FormatServerSideApplyException(DryRunFailedMessage, ex));
        }
    }

    private async Task<DryRunResult> DryRunDeleteManifestAsync(
        KubernetesObjectRef[] objects,
        CancellationToken cancellationToken)
    {
        try
        {
            var dryRunObjects = new List<KubernetesPlanDryRunObject>();
            var warnings = new List<string>();

            foreach (var obj in objects)
            {
                var result = await DryRunDeleteObjectAsync(obj, cancellationToken).ConfigureAwait(false);
                dryRunObjects.Add(result.Object);
                warnings.AddRange(result.Warnings);
            }

            return DryRunResult.Success(dryRunObjects, warnings);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Server-side dry-run delete failed for {ObjectCount} object(s)", objects.Length);
            return DryRunResult.Failed(KubernetesManagerHelpers.FormatApiException(DryRunFailedMessage, ex));
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
                dryRun: KubernetesConventions.KubernetesApi.DryRunAll,
                fieldManager: KubernetesManagerHelpers.FieldManager,
                fieldValidation: KubernetesConventions.KubernetesApi.FieldValidationStrict,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var dryRunObject = CaptureDryRunObject(
                KubernetesConventions.KubernetesResources.DeploymentRef(namespaceName, name),
                response);

            return DryRunResult.Success([dryRunObject], ExtractWarnings(response));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Server-side dry-run scale failed for Deployment {Namespace}/{Name} to {Replicas} replicas", namespaceName, name, replicas);
            return DryRunResult.Failed(KubernetesManagerHelpers.FormatApiException(DryRunFailedMessage, ex));
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
                dryRun: KubernetesConventions.KubernetesApi.DryRunAll,
                fieldManager: KubernetesManagerHelpers.FieldManager,
                fieldValidation: KubernetesConventions.KubernetesApi.FieldValidationStrict,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var dryRunObject = CaptureDryRunObject(
                KubernetesConventions.KubernetesResources.DeploymentRef(namespaceName, name),
                response);

            return DryRunResult.Success([dryRunObject], ExtractWarnings(response));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Server-side dry-run restart failed for Deployment {Namespace}/{Name}", namespaceName, name);
            return DryRunResult.Failed(KubernetesManagerHelpers.FormatApiException(DryRunFailedMessage, ex));
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
                dryRun: KubernetesConventions.KubernetesApi.DryRunAll,
                fieldManager: KubernetesManagerHelpers.FieldManager,
                fieldValidation: KubernetesConventions.KubernetesApi.FieldValidationStrict,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var dryRunObject = CaptureDryRunObject(
                KubernetesConventions.KubernetesResources.DeploymentRef(namespaceName, name),
                response);

            return DryRunResult.Success([dryRunObject], ExtractWarnings(response));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Server-side dry-run set-image failed for Deployment {Namespace}/{Name} container {Container}", namespaceName, name, container);
            return DryRunResult.Failed(KubernetesManagerHelpers.FormatApiException(DryRunFailedMessage, ex));
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
                dryRun: KubernetesConventions.KubernetesApi.DryRunAll,
                fieldManager: KubernetesManagerHelpers.FieldManager,
                fieldValidation: KubernetesConventions.KubernetesApi.FieldValidationStrict,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return CaptureDryRunResult(deployment, response);
        }

        if (obj is V1Service service)
        {
            using var response = await client.CoreV1.PatchNamespacedServiceWithHttpMessagesAsync(
                new V1Patch(service, V1Patch.PatchType.ApplyPatch),
                service.Metadata.Name,
                service.Metadata.NamespaceProperty,
                dryRun: KubernetesConventions.KubernetesApi.DryRunAll,
                fieldManager: KubernetesManagerHelpers.FieldManager,
                fieldValidation: KubernetesConventions.KubernetesApi.FieldValidationStrict,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return CaptureDryRunResult(service, response);
        }

        if (obj is V1ConfigMap configMap)
        {
            using var response = await client.CoreV1.PatchNamespacedConfigMapWithHttpMessagesAsync(
                new V1Patch(configMap, V1Patch.PatchType.ApplyPatch),
                configMap.Metadata.Name,
                configMap.Metadata.NamespaceProperty,
                dryRun: KubernetesConventions.KubernetesApi.DryRunAll,
                fieldManager: KubernetesManagerHelpers.FieldManager,
                fieldValidation: KubernetesConventions.KubernetesApi.FieldValidationStrict,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return CaptureDryRunResult(configMap, response);
        }

        throw new InvalidOperationException("Unsupported object for server-side dry-run.");
    }

    private async Task<DryRunObjectResult> DryRunDeleteObjectAsync(
        KubernetesObjectRef obj,
        CancellationToken cancellationToken)
    {
        switch (obj.ApiVersion, obj.Kind)
        {
            case (KubernetesConventions.KubernetesResources.AppsV1, KubernetesConventions.KubernetesResources.Deployment):
                {
                    using var response = await client.AppsV1.DeleteNamespacedDeploymentWithHttpMessagesAsync(
                        obj.Name,
                        obj.Namespace,
                        body: CreateDryRunDeleteOptions(),
                        dryRun: KubernetesConventions.KubernetesApi.DryRunAll,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    return CaptureDryRunResult(obj, response);
                }
            case (KubernetesConventions.KubernetesResources.V1, KubernetesConventions.KubernetesResources.Service):
                {
                    using var response = await client.CoreV1.DeleteNamespacedServiceWithHttpMessagesAsync(
                        obj.Name,
                        obj.Namespace,
                        body: CreateDryRunDeleteOptions(),
                        dryRun: KubernetesConventions.KubernetesApi.DryRunAll,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    return CaptureDryRunResult(obj, response);
                }
            case (KubernetesConventions.KubernetesResources.V1, KubernetesConventions.KubernetesResources.ConfigMap):
                {
                    using var response = await client.CoreV1.DeleteNamespacedConfigMapWithHttpMessagesAsync(
                        obj.Name,
                        obj.Namespace,
                        body: CreateDryRunDeleteOptions(),
                        dryRun: KubernetesConventions.KubernetesApi.DryRunAll,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    return CaptureDryRunResult(obj, response);
                }
            default:
                throw new InvalidOperationException($"Unsupported object for server-side dry-run: {KubernetesManagerHelpers.FormatObjectRef(obj)}.");
        }
    }

    private static V1DeleteOptions CreateDryRunDeleteOptions() =>
        new()
        {
            DryRun = [KubernetesConventions.KubernetesApi.DryRunAll]
        };

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
                        annotations = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [KubernetesManagerHelpers.RestartedAtAnnotation] = restartedAtUtc
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
        KubernetesObjectRef obj,
        IHttpOperationResponse<T> response) =>
        new(
            CaptureDryRunObject(obj, response),
            ExtractWarnings(response));

    private static KubernetesPlanDryRunObject CaptureDryRunObject<T>(
        KubernetesObjectRef obj,
        IHttpOperationResponse<T> response) =>
        CaptureDryRunObject(KubernetesManagerHelpers.FormatObjectRef(obj), response);

    private static KubernetesPlanDryRunObject CaptureDryRunObject<T>(
        string obj,
        IHttpOperationResponse<T> response) =>
        new(obj, JsonSerializer.Serialize(response.Body, KubernetesManagerHelpers.JsonOptions));

    private static string[] ExtractWarnings<T>(IHttpOperationResponse<T> response) =>
        response.Response.Headers.TryGetValues(KubernetesConventions.KubernetesApi.WarningHeader, out var values)
            ? values.ToArray()
            : [];

    private static string FormatObjectRef(IKubernetesObject<V1ObjectMeta> obj) =>
        $"{obj.ApiVersion} {obj.Kind} {obj.Metadata.NamespaceProperty}/{obj.Metadata.Name}";

    private sealed record class DryRunObjectResult(
        KubernetesPlanDryRunObject Object,
        string[] Warnings);

    private sealed record class DryRunResult(
        bool Succeeded,
        KubernetesPlanDryRun? DryRun,
        string Message)
    {
        public static DryRunResult Success(
            IReadOnlyList<KubernetesPlanDryRunObject> objects,
            IEnumerable<string> warnings)
        {
            const string message = "Server-side dry-run succeeded.";
            var dryRun = new KubernetesPlanDryRun(
                KubernetesConventions.DryRunStatuses.Succeeded,
                DateTimeOffset.UtcNow,
                objects.ToArray(),
                warnings.Distinct(StringComparer.Ordinal).ToArray(),
                message);

            return new DryRunResult(true, dryRun, message);
        }

        public static DryRunResult Failed(string message) => new(false, null, message);
    }
}
