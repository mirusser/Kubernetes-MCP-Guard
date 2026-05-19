using InfraGate.Approvals;
using InfraGate.KubernetesAdapter.Policy;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpServer;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed partial class K8sManager
{
    public async Task<string> ExecuteApplyManifestAsync(
        string namespaceName,
        string manifest,
        CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        K8sParsedManifest parsed;
        try
        {
            parsed = K8sManifestParser.ParseSupported(manifest, namespaceName);
        }
        catch (K8sValidationException ex)
        {
            return ex.Message;
        }

        var policyResult = K8sPolicyValidator.Validate(parsed.Objects, K8sPolicyOptions.Default);
        if (policyResult.IsDenied)
        {
            return $"Apply refused by policy:{Environment.NewLine}{policyResult.FormatRefusal()}";
        }

        var messages = new List<string>();
        foreach (var obj in parsed.Objects)
        {
            try
            {
                await ApplyObjectAsync(obj, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Apply failed for {ApiVersion} {Kind} {Namespace}/{Name}",
                    obj.ApiVersion, obj.Kind, obj.Metadata.NamespaceProperty, obj.Metadata.Name);
                return FormatServerSideApplyException("Apply failed", ex);
            }

            messages.Add($"Applied {obj.ApiVersion} {obj.Kind} {obj.Metadata.NamespaceProperty}/{obj.Metadata.Name}");
        }

        return string.Join(Environment.NewLine, messages);
    }

    public async Task<string> ExecuteDeleteManifestAsync(
        string namespaceName,
        string manifest,
        CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        K8sParsedManifest parsed;
        try
        {
            parsed = K8sManifestParser.ParseSupported(manifest, namespaceName);
        }
        catch (K8sValidationException ex)
        {
            return ex.Message;
        }

        var messages = new List<string>();
        foreach (var obj in parsed.ObjectRefs)
        {
            messages.Add(await DeleteObjectAsync(obj, cancellationToken));
        }

        return string.Join(Environment.NewLine, messages);
    }

    public async Task<string> ExecuteScaleDeploymentAsync(
        string namespaceName,
        string name,
        int replicas,
        CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName) ?? ValidateName(name) ?? ValidateReplicas(replicas);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            await client.AppsV1.PatchNamespacedDeploymentScaleAsync(
                CreateScaleDeploymentPatch(replicas),
                name,
                namespaceName,
                fieldManager: FieldManager,
                cancellationToken: cancellationToken);

            return $"Scaled {K8sConventions.K8sResources.DeploymentDisplayName} {namespaceName}/{name} to {replicas} replicas.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scale failed for {Namespace}/{Name}", namespaceName, name);
            return FormatApiException("Scale failed", ex);
        }
    }

    public async Task<string> ExecuteRestartDeploymentAsync(
        string namespaceName,
        string name,
        CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName) ?? ValidateName(name);
        if (validation is not null)
        {
            return validation;
        }

        var restartedAtUtc = DateTimeOffset.UtcNow.ToString(ApprovalConventions.DateTimeFormats.RoundTrip);

        try
        {
            await client.AppsV1.PatchNamespacedDeploymentAsync(
                CreateRestartDeploymentPatch(restartedAtUtc),
                name,
                namespaceName,
                fieldManager: FieldManager,
                cancellationToken: cancellationToken);

            return $"Restarted {K8sConventions.K8sResources.DeploymentDisplayName} {namespaceName}/{name} at {restartedAtUtc}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Restart failed for {Namespace}/{Name}", namespaceName, name);
            return FormatApiException("Restart failed", ex);
        }
    }

    public async Task<string> ExecuteSetDeploymentImageAsync(
        string namespaceName,
        string name,
        string container,
        string image,
        CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName) ??
            ValidateName(name) ??
            ValidateRequiredText(container, "Container name") ??
            ValidateRequiredText(image, "Image");
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            await client.AppsV1.PatchNamespacedDeploymentAsync(
                CreateSetDeploymentImagePatch(container, image),
                name,
                namespaceName,
                fieldManager: FieldManager,
                cancellationToken: cancellationToken);

            return $"Updated {K8sConventions.K8sResources.DeploymentDisplayName} {namespaceName}/{name} container '{container}' image to '{image}'.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Set image failed for {Namespace}/{Name}", namespaceName, name);
            return FormatApiException("Set image failed", ex);
        }
    }
}
