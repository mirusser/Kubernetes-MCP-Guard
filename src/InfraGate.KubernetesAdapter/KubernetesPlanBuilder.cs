using System.Text.Json;
using InfraGate.Approvals;

namespace InfraGate.KubernetesAdapter;

public sealed class KubernetesPlanBuilder(IToolCaller toolCaller) : IDomainPlanBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyList<FreshnessCheck> ManifestFreshnessChecks =
    [
        new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.LiveDrift, new Dictionary<string, string>()),
        new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>())
    ];

    private static readonly IReadOnlyList<FreshnessCheck> DeploymentFreshnessChecks =
    [
        new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>())
    ];

    public Task<PlanBuildResult> BuildAsync(
        string mutationToolName,
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        CancellationToken ct) =>
        mutationToolName switch
        {
            KubernetesAdapterConventions.MutationTools.ApplyManifest =>
                BuildApplyManifestAsync(arguments, requester, ct),
            KubernetesAdapterConventions.MutationTools.DeleteManifest =>
                BuildDeleteManifestAsync(arguments, requester, ct),
            KubernetesAdapterConventions.MutationTools.ScaleDeployment =>
                BuildScaleDeploymentAsync(arguments, requester, ct),
            KubernetesAdapterConventions.MutationTools.RestartDeployment =>
                BuildRestartDeploymentAsync(arguments, requester, ct),
            KubernetesAdapterConventions.MutationTools.SetDeploymentImage =>
                BuildSetDeploymentImageAsync(arguments, requester, ct),
            _ => Task.FromResult(PlanBuildResult.Failed($"Unsupported mutation tool '{mutationToolName}'."))
        };

    private async Task<PlanBuildResult> BuildApplyManifestAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Manifest, out var manifest))
        {
            return PlanBuildResult.Failed("Missing required arguments: namespace and manifest.");
        }

        var applyEvidenceJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest,
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = manifest
            },
            ct).ConfigureAwait(false);

        K8sApplyEvidence? applyEvidence;
        try
        {
            applyEvidence = JsonSerializer.Deserialize<K8sApplyEvidence>(applyEvidenceJson, JsonOptions);
        }
        catch (JsonException)
        {
            return PlanBuildResult.Failed($"Evidence dry-run failed: {applyEvidenceJson}");
        }

        if (applyEvidence is null)
        {
            return PlanBuildResult.Failed("Evidence dry-run returned an empty result.");
        }

        if (applyEvidence.PolicyBlocked)
        {
            return PlanBuildResult.Failed($"Manifest rejected by policy:{Environment.NewLine}{applyEvidence.PolicyRefusal}");
        }

        var diffJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DiffManifest,
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = manifest
            },
            ct).ConfigureAwait(false);

        K8sPlanDiff[]? diffs;
        try
        {
            diffs = JsonSerializer.Deserialize<K8sPlanDiff[]>(diffJson, JsonOptions);
        }
        catch (JsonException)
        {
            return PlanBuildResult.Failed($"Diff evidence failed: {diffJson}");
        }

        if (diffs is null)
        {
            return PlanBuildResult.Failed("Diff evidence returned an empty result.");
        }

        var objects = diffs.Select(d => d.Object).ToArray();
        var objectCount = objects.Length;
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Apply {objectCount} supported Kubernetes object(s) in namespace '{namespaceName}'.",
            new Dictionary<string, string>
            {
                [KubernetesAdapterConventions.PlanParameters.ObjectCount] = objectCount.ToString()
            },
            objects)
        {
            Manifest = manifest,
            DryRun = applyEvidence.DryRun,
            Diffs = diffs,
            PolicyFindings = applyEvidence.PolicyFindings
        };

        return BuildEnvelope(
            KubernetesAdapterConventions.PlanOperations.Apply,
            payload,
            requester,
            new FreshnessPolicy(ManifestFreshnessChecks));
    }

    private async Task<PlanBuildResult> BuildDeleteManifestAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Manifest, out var manifest))
        {
            return PlanBuildResult.Failed("Missing required arguments: namespace and manifest.");
        }

        var dryRunJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunDeleteManifest,
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = manifest
            },
            ct).ConfigureAwait(false);

        var dryRun = DeserializeDryRun(dryRunJson);
        if (dryRun is null)
        {
            return PlanBuildResult.Failed($"Evidence dry-run failed: {dryRunJson}");
        }

        var diffJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DiffManifest,
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = manifest
            },
            ct).ConfigureAwait(false);

        K8sPlanDiff[]? diffs;
        try
        {
            diffs = JsonSerializer.Deserialize<K8sPlanDiff[]>(diffJson, JsonOptions);
        }
        catch (JsonException)
        {
            return PlanBuildResult.Failed($"Diff evidence failed: {diffJson}");
        }

        if (diffs is null)
        {
            return PlanBuildResult.Failed("Diff evidence returned an empty result.");
        }

        var objects = diffs.Select(d => d.Object).ToArray();
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Delete {objects.Length} supported Kubernetes object(s) from namespace '{namespaceName}'.",
            new Dictionary<string, string>
            {
                [KubernetesAdapterConventions.PlanParameters.ObjectCount] = objects.Length.ToString()
            },
            objects)
        {
            Manifest = manifest,
            DryRun = dryRun,
            Diffs = diffs
        };

        return BuildEnvelope(
            KubernetesAdapterConventions.PlanOperations.Delete,
            payload,
            requester,
            new FreshnessPolicy(ManifestFreshnessChecks));
    }

    private async Task<PlanBuildResult> BuildScaleDeploymentAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Name, out var name) ||
            !TryGetInt(arguments, KubernetesAdapterConventions.EvidenceArguments.Replicas, out int replicas))
        {
            return PlanBuildResult.Failed("Missing required arguments: namespace, name, and replicas.");
        }

        var dryRunJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunScaleDeployment,
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Name] = name,
                [KubernetesAdapterConventions.EvidenceArguments.Replicas] = replicas
            },
            ct).ConfigureAwait(false);

        var dryRun = DeserializeDryRun(dryRunJson);
        if (dryRun is null)
        {
            return PlanBuildResult.Failed($"Evidence dry-run failed: {dryRunJson}");
        }

        var diffJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DiffDeployment,
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Name] = name,
                [KubernetesAdapterConventions.EvidenceArguments.Operation] =
                    KubernetesAdapterConventions.PlanOperations.Scale,
                [KubernetesAdapterConventions.EvidenceArguments.Replicas] = replicas
            },
            ct).ConfigureAwait(false);

        var diffs = DeserializeDiffs(diffJson);
        if (diffs is null)
        {
            return PlanBuildResult.Failed($"Diff evidence failed: {diffJson}");
        }

        var deploymentRef = new K8sObjectRef("apps/v1", "Deployment", namespaceName, name);
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Scale Deployment '{name}' in namespace '{namespaceName}' to {replicas} replicas.",
            new Dictionary<string, string>
            {
                [KubernetesAdapterConventions.PlanParameters.Name] = name,
                [KubernetesAdapterConventions.PlanParameters.Replicas] = replicas.ToString()
            },
            [deploymentRef])
        {
            DryRun = dryRun,
            Diffs = diffs
        };

        return BuildEnvelope(
            KubernetesAdapterConventions.PlanOperations.Scale,
            payload,
            requester,
            new FreshnessPolicy(DeploymentFreshnessChecks));
    }

    private async Task<PlanBuildResult> BuildRestartDeploymentAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Name, out var name))
        {
            return PlanBuildResult.Failed("Missing required arguments: namespace and name.");
        }

        var dryRunJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunRestartDeployment,
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Name] = name
            },
            ct).ConfigureAwait(false);

        var dryRun = DeserializeDryRun(dryRunJson);
        if (dryRun is null)
        {
            return PlanBuildResult.Failed($"Evidence dry-run failed: {dryRunJson}");
        }

        var diffJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DiffDeployment,
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Name] = name,
                [KubernetesAdapterConventions.EvidenceArguments.Operation] =
                    KubernetesAdapterConventions.PlanOperations.Restart
            },
            ct).ConfigureAwait(false);

        var diffs = DeserializeDiffs(diffJson);
        if (diffs is null)
        {
            return PlanBuildResult.Failed($"Diff evidence failed: {diffJson}");
        }

        var restartedAtUtc = DateTimeOffset.UtcNow.ToString(ApprovalConventions.DateTimeFormats.RoundTrip);
        var deploymentRef = new K8sObjectRef("apps/v1", "Deployment", namespaceName, name);
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Restart Deployment '{name}' in namespace '{namespaceName}'.",
            new Dictionary<string, string>
            {
                [KubernetesAdapterConventions.PlanParameters.Name] = name,
                [KubernetesAdapterConventions.PlanParameters.RestartedAtUtc] = restartedAtUtc
            },
            [deploymentRef])
        {
            DryRun = dryRun,
            Diffs = diffs
        };

        return BuildEnvelope(
            KubernetesAdapterConventions.PlanOperations.Restart,
            payload,
            requester,
            new FreshnessPolicy(DeploymentFreshnessChecks));
    }

    private async Task<PlanBuildResult> BuildSetDeploymentImageAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Name, out var name) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Container, out var container) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Image, out var image))
        {
            return PlanBuildResult.Failed("Missing required arguments: namespace, name, container, and image.");
        }

        var dryRunJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunSetDeploymentImage,
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Name] = name,
                [KubernetesAdapterConventions.EvidenceArguments.Container] = container,
                [KubernetesAdapterConventions.EvidenceArguments.Image] = image
            },
            ct).ConfigureAwait(false);

        var dryRun = DeserializeDryRun(dryRunJson);
        if (dryRun is null)
        {
            return PlanBuildResult.Failed($"Evidence dry-run failed: {dryRunJson}");
        }

        var diffJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DiffDeployment,
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Name] = name,
                [KubernetesAdapterConventions.EvidenceArguments.Operation] =
                    KubernetesAdapterConventions.PlanOperations.SetImage,
                [KubernetesAdapterConventions.EvidenceArguments.Container] = container,
                [KubernetesAdapterConventions.EvidenceArguments.Image] = image
            },
            ct).ConfigureAwait(false);

        var diffs = DeserializeDiffs(diffJson);
        if (diffs is null)
        {
            return PlanBuildResult.Failed($"Diff evidence failed: {diffJson}");
        }

        var deploymentRef = new K8sObjectRef("apps/v1", "Deployment", namespaceName, name);
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Update Deployment '{name}' container '{container}' image to '{image}'.",
            new Dictionary<string, string>
            {
                [KubernetesAdapterConventions.PlanParameters.Name] = name,
                [KubernetesAdapterConventions.PlanParameters.Container] = container,
                [KubernetesAdapterConventions.PlanParameters.Image] = image
            },
            [deploymentRef])
        {
            DryRun = dryRun,
            Diffs = diffs
        };

        return BuildEnvelope(
            KubernetesAdapterConventions.PlanOperations.SetImage,
            payload,
            requester,
            new FreshnessPolicy(DeploymentFreshnessChecks));
    }

    private static PlanBuildResult BuildEnvelope(
        string operation,
        KubernetesPlanPayload payload,
        PlanRequester requester,
        FreshnessPolicy freshnessPolicy)
    {
        var planId = ApprovalStore.NewPlanId();
        var envelope = KubernetesApprovalAdapter.ToEnvelope(
            KubernetesApprovalAdapter.CreateEnvelope(
                planId,
                operation,
                DateTimeOffset.UtcNow,
                requester,
                payload,
                freshnessPolicy: freshnessPolicy));

        return PlanBuildResult.Success(envelope, planId, payload.Namespace);
    }

    private static K8sPlanDryRun? DeserializeDryRun(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<K8sPlanDryRun>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static K8sPlanDiff[]? DeserializeDiffs(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<K8sPlanDiff[]>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetString(IReadOnlyDictionary<string, object?> args, string key, out string value)
    {
        if (args.TryGetValue(key, out var raw) && raw is string s && !string.IsNullOrWhiteSpace(s))
        {
            value = s;
            return true;
        }

        if (args.TryGetValue(key, out raw) &&
            raw is JsonElement { ValueKind: JsonValueKind.String } element &&
            !string.IsNullOrWhiteSpace(element.GetString()))
        {
            value = element.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, object?> args, string key, out int value)
    {
        if (args.TryGetValue(key, out var raw))
        {
            if (raw is int i)
            {
                value = i;
                return true;
            }

            if (raw is long l)
            {
                value = (int)l;
                return true;
            }

            if (raw is double d && d % 1 == 0 && d >= int.MinValue && d <= int.MaxValue)
            {
                value = (int)d;
                return true;
            }

            if (raw is string s && int.TryParse(s, out value))
            {
                return true;
            }

            if (raw is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
                {
                    return true;
                }

                if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
                {
                    return true;
                }
            }
        }

        value = 0;
        return false;
    }
}
