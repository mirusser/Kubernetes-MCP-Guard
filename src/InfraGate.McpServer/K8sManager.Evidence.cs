using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using InfraGate.McpServer.Diff;
using InfraGate.KubernetesAdapter.Policy;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpServer;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed partial class K8sManager
{
    public async Task<string> EvidenceDryRunApplyManifestAsync(
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
        var policyFindings = policyResult.Findings
            .Select(f => new K8sPlanPolicyFinding(f.Severity.ToString(), f.Code, f.ObjectRef, f.Message))
            .ToArray();

        var dryRunResult = await DryRunApplyManifestAsync(parsed.Objects, cancellationToken);
        if (!dryRunResult.Succeeded || dryRunResult.DryRun is null)
        {
            return dryRunResult.Message;
        }

        var evidence = new K8sApplyEvidence(
            dryRunResult.DryRun,
            policyFindings,
            policyResult.IsDenied,
            policyResult.IsDenied ? policyResult.FormatRefusal() : null);

        return JsonSerializer.Serialize(evidence, JsonOptions);
    }

    public async Task<string> EvidenceDryRunDeleteManifestAsync(
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

        var result = await DryRunDeleteManifestAsync(parsed.ObjectRefs, cancellationToken);
        return result.Succeeded && result.DryRun is not null
            ? JsonSerializer.Serialize(result.DryRun, JsonOptions)
            : result.Message;
    }

    public async Task<string> EvidenceDryRunScaleDeploymentAsync(
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

        var result = await DryRunScaleDeploymentAsync(namespaceName, name, replicas, cancellationToken);
        return result.Succeeded && result.DryRun is not null
            ? JsonSerializer.Serialize(result.DryRun, JsonOptions)
            : result.Message;
    }

    public async Task<string> EvidenceDryRunRestartDeploymentAsync(
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
        var result = await DryRunRestartDeploymentAsync(namespaceName, name, restartedAtUtc, cancellationToken);
        return result.Succeeded && result.DryRun is not null
            ? JsonSerializer.Serialize(result.DryRun, JsonOptions)
            : result.Message;
    }

    public async Task<string> EvidenceDryRunSetDeploymentImageAsync(
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

        var result = await DryRunSetDeploymentImageAsync(namespaceName, name, container, image, cancellationToken);
        return result.Succeeded && result.DryRun is not null
            ? JsonSerializer.Serialize(result.DryRun, JsonOptions)
            : result.Message;
    }

    public async Task<string> EvidenceCheckLiveDriftAsync(
        string namespaceName,
        string operation,
        string diffsJson,
        CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        K8sPlanDiff[]? diffs;
        try
        {
            diffs = JsonSerializer.Deserialize<K8sPlanDiff[]>(diffsJson, JsonOptions);
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
            var drift = await K8sDiffService.FindDriftAsync(client, operation, diffs, cancellationToken);
            return drift ?? K8sConventions.DriftCheckResult.NoDrift;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Drift check failed for {Operation} in {Namespace}", operation, namespaceName);
            return FormatApiException("Drift check failed", ex);
        }
    }

    public async Task<string> EvidenceDiffManifestAsync(
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

        var dryRunResult = await DryRunApplyManifestAsync(parsed.Objects, cancellationToken);
        if (!dryRunResult.Succeeded || dryRunResult.DryRun is null)
        {
            return dryRunResult.Message;
        }

        try
        {
            var diffs = await K8sDiffService.BuildDiffsAsync(
                client,
                K8sConventions.MutationOperations.Apply,
                parsed.ObjectRefs,
                dryRunResult.DryRun.Objects,
                cancellationToken);

            return JsonSerializer.Serialize(diffs, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Diff generation failed for manifest in namespace {Namespace}", namespaceName);
            return FormatApiException("Diff generation failed", ex);
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
        var validation = ValidateNamespace(namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            DryRunResult dryRunResult = operation switch
            {
                K8sConventions.MutationOperations.Scale
                    => await DryRunScaleDeploymentAsync(namespaceName, name, replicas ?? 1, cancellationToken),
                K8sConventions.MutationOperations.Restart
                    => await DryRunRestartDeploymentAsync(
                        namespaceName, name,
                        DateTimeOffset.UtcNow.ToString(ApprovalConventions.DateTimeFormats.RoundTrip),
                        cancellationToken),
                K8sConventions.MutationOperations.SetImage
                    => await DryRunSetDeploymentImageAsync(namespaceName, name, container ?? string.Empty, image ?? string.Empty, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported operation '{operation}' for deployment diff.")
            };

            if (!dryRunResult.Succeeded || dryRunResult.DryRun is null)
            {
                return dryRunResult.Message;
            }

            var obj = K8sConventions.K8sResources.DeploymentRef(namespaceName, name);
            var diffs = await K8sDiffService.BuildDiffsAsync(
                client,
                operation,
                [obj],
                dryRunResult.DryRun.Objects,
                cancellationToken).ConfigureAwait(false);

            return JsonSerializer.Serialize(diffs, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Deployment diff generation failed for {Namespace}/{Name}", namespaceName, name);
            return FormatApiException("Deployment diff generation failed", ex);
        }
    }
}
