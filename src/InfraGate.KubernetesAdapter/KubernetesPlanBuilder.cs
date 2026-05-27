using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Audit;
using InfraGate.KubernetesAdapter.Policy;

namespace InfraGate.KubernetesAdapter;

public sealed class KubernetesPlanBuilder(IToolCaller toolCaller) : IDomainPlanBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyList<FreshnessCheck> ManifestFreshnessChecks =
    [
        new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.LiveDrift, new Dictionary<string, string>(StringComparer.Ordinal)),
        new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>(StringComparer.Ordinal))
    ];

    private static readonly IReadOnlyList<FreshnessCheck> DeploymentFreshnessChecks =
    [
        new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>(StringComparer.Ordinal))
    ];

    public Task<PlanBuildResult> BuildAsync(
        string mutationToolName,
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        CancellationToken ct) =>
        BuildAsync(mutationToolName, arguments, requester, ApprovalPolicy.SameSubject(), ct);

    public Task<PlanBuildResult> BuildAsync(
        string mutationToolName,
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        CancellationToken ct) =>
        mutationToolName switch
        {
            KubernetesAdapterConventions.MutationTools.ApplyManifest =>
                BuildApplyManifestAsync(arguments, requester, approvalPolicy, ct),
            KubernetesAdapterConventions.MutationTools.DeleteManifest =>
                BuildDeleteManifestAsync(arguments, requester, approvalPolicy, ct),
            KubernetesAdapterConventions.MutationTools.ScaleDeployment =>
                BuildScaleDeploymentAsync(arguments, requester, approvalPolicy, ct),
            KubernetesAdapterConventions.MutationTools.RestartDeployment =>
                BuildRestartDeploymentAsync(arguments, requester, approvalPolicy, ct),
            KubernetesAdapterConventions.MutationTools.SetDeploymentImage =>
                BuildSetDeploymentImageAsync(arguments, requester, approvalPolicy, ct),
            _ => Task.FromResult(PlanBuildResult.Failed(
                $"Unsupported mutation tool '{mutationToolName}'.",
                KubernetesAdapterConventions.ResultReasonCodes.UnsupportedMutationTool))
        };

    private async Task<PlanBuildResult> BuildApplyManifestAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Manifest, out var manifest))
        {
            return PlanBuildResult.Failed(
                "Missing required arguments: namespace and manifest.",
                KubernetesAdapterConventions.ResultReasonCodes.MissingArguments);
        }

        var applyEvidence = await GetApplyEvidenceAsync(namespaceName, manifest, ct).ConfigureAwait(false);
        if (applyEvidence.Error is not null)
        {
            return applyEvidence.Error;
        }

        if (applyEvidence.Evidence!.PolicyBlocked)
        {
            var message = $"Manifest rejected by policy:{Environment.NewLine}{applyEvidence.Evidence.PolicyRefusal}";
            return PlanBuildResult.Failed(
                message,
                DryRunAudit(KubernetesAdapterConventions.PlanOperations.Apply, namespaceName, message),
                KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked);
        }

        var diffs = await GetManifestDiffsAsync(namespaceName, manifest, applyEvidence.Evidence.DryRun.Objects, ct)
            .ConfigureAwait(false);
        if (diffs.Error is not null)
        {
            return diffs.Error;
        }

        var objects = diffs.Diffs!.Select(d => d.Object).ToArray();
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Apply {objects.Length} supported Kubernetes object(s) in namespace '{namespaceName}'.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.PlanParameters.ObjectCount] = objects.Length.ToString()
            },
            objects)
        {
            Manifest = manifest,
            DryRun = applyEvidence.Evidence.DryRun,
            Diffs = diffs.Diffs!,
            PolicyFindings = applyEvidence.Evidence.PolicyFindings
        };

        return BuildEnvelope(
            KubernetesAdapterConventions.PlanOperations.Apply,
            payload,
            requester,
            approvalPolicy,
            new FreshnessPolicy(ManifestFreshnessChecks));
    }

    private sealed record class ApplyEvidenceResult(PlanBuildResult? Error, KubernetesApplyEvidence? Evidence);

    private async Task<ApplyEvidenceResult> GetApplyEvidenceAsync(
        string namespaceName, string manifest, CancellationToken ct)
    {
        var json = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = manifest
            },
            ct).ConfigureAwait(false);

        KubernetesApplyEvidence? evidence;
        try
        {
            evidence = JsonSerializer.Deserialize<KubernetesApplyEvidence>(json, JsonOptions);
        }
        catch (JsonException)
        {
            var message = $"Evidence dry-run failed: {json}";
            return new ApplyEvidenceResult(
                PlanBuildResult.Failed(message,
                    DryRunAudit(KubernetesAdapterConventions.PlanOperations.Apply, namespaceName, message),
                    KubernetesAdapterConventions.ResultReasonCodes.DryRunFailed),
                null);
        }

        if (evidence is null)
        {
            const string message = "Evidence dry-run returned an empty result.";
            return new ApplyEvidenceResult(
                PlanBuildResult.Failed(message,
                    DryRunAudit(KubernetesAdapterConventions.PlanOperations.Apply, namespaceName, message),
                    KubernetesAdapterConventions.ResultReasonCodes.DryRunFailed),
                null);
        }

        return new ApplyEvidenceResult(null, evidence);
    }

    private sealed record class DiffsResult(PlanBuildResult? Error, KubernetesPlanDiff[]? Diffs);

    private async Task<DiffsResult> GetManifestDiffsAsync(
        string namespaceName, string manifest, IEnumerable<KubernetesPlanDryRunObject> dryRunObjects, CancellationToken ct)
    {
        var json = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DiffManifest,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = manifest
            },
            ct).ConfigureAwait(false);

        string[] objectList = dryRunObjects.Select(obj => obj.Object).ToArray();

        KubernetesPlanDiff[]? diffs;
        try
        {
            diffs = JsonSerializer.Deserialize<KubernetesPlanDiff[]>(json, JsonOptions);
        }
        catch (JsonException)
        {
            var message = $"Diff evidence failed: {json}";
            return new DiffsResult(
                PlanBuildResult.Failed(
                    message,
                    DiffAudit(KubernetesAdapterConventions.PlanOperations.Apply, namespaceName, objectList, message),
                    KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceFailed),
                null);
        }

        if (diffs is null)
        {
            const string message = "Diff evidence returned an empty result.";
            return new DiffsResult(
                PlanBuildResult.Failed(
                    message,
                    DiffAudit(KubernetesAdapterConventions.PlanOperations.Apply, namespaceName, objectList, message),
                    KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceEmpty),
                null);
        }

        return new DiffsResult(null, diffs);
    }

    private async Task<PlanBuildResult> BuildDeleteManifestAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Manifest, out var manifest))
        {
            return PlanBuildResult.Failed(
                "Missing required arguments: namespace and manifest.",
                KubernetesAdapterConventions.ResultReasonCodes.MissingArguments);
        }

        var dryRunJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunDeleteManifest,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = manifest
            },
            ct).ConfigureAwait(false);

        var dryRun = DeserializeDryRun(dryRunJson);
        if (dryRun is null)
        {
            return PlanBuildResult.Failed(
                $"Evidence dry-run failed: {dryRunJson}",
                KubernetesAdapterConventions.ResultReasonCodes.DryRunFailed);
        }

        var diffJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DiffManifest,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = manifest
            },
            ct).ConfigureAwait(false);

        KubernetesPlanDiff[]? diffs;
        try
        {
            diffs = JsonSerializer.Deserialize<KubernetesPlanDiff[]>(diffJson, JsonOptions);
        }
        catch (JsonException)
        {
            var message = $"Diff evidence failed: {diffJson}";
            return PlanBuildResult.Failed(
                message,
                DiffAudit(
                    KubernetesAdapterConventions.PlanOperations.Delete,
                    namespaceName,
                    dryRun.Objects.Select(obj => obj.Object).ToArray(),
                    message),
                KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceFailed);
        }

        if (diffs is null)
        {
            const string message = "Diff evidence returned an empty result.";
            return PlanBuildResult.Failed(
                message,
                DiffAudit(
                    KubernetesAdapterConventions.PlanOperations.Delete,
                    namespaceName,
                    dryRun.Objects.Select(obj => obj.Object).ToArray(),
                    message),
                KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceEmpty);
        }

        var objects = diffs.Select(d => d.Object).ToArray();
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Delete {objects.Length} supported Kubernetes object(s) from namespace '{namespaceName}'.",
            new Dictionary<string, string>(StringComparer.Ordinal)
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
            approvalPolicy,
            new FreshnessPolicy(ManifestFreshnessChecks));
    }

    private async Task<PlanBuildResult> BuildScaleDeploymentAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Name, out var name) ||
            !TryGetInt(arguments, KubernetesAdapterConventions.EvidenceArguments.Replicas, out int replicas))
        {
            return PlanBuildResult.Failed(
                "Missing required arguments: namespace, name, and replicas.",
                KubernetesAdapterConventions.ResultReasonCodes.MissingArguments);
        }

        var dryRunJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunScaleDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Name] = name,
                [KubernetesAdapterConventions.EvidenceArguments.Replicas] = replicas
            },
            ct).ConfigureAwait(false);

        var dryRun = DeserializeDryRun(dryRunJson);
        if (dryRun is null)
        {
            return PlanBuildResult.Failed(
                $"Evidence dry-run failed: {dryRunJson}",
                KubernetesAdapterConventions.ResultReasonCodes.DryRunFailed);
        }

        var diffJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DiffDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
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
            var message = $"Diff evidence failed: {diffJson}";
            return PlanBuildResult.Failed(
                message,
                DiffAudit(
                    KubernetesAdapterConventions.PlanOperations.Scale,
                    namespaceName,
                    dryRun.Objects.Select(obj => obj.Object).ToArray(),
                    message),
                KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceFailed);
        }

        var deploymentRef = new KubernetesObjectRef("apps/v1", "Deployment", namespaceName, name);
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Scale Deployment '{name}' in namespace '{namespaceName}' to {replicas} replicas.",
            new Dictionary<string, string>(StringComparer.Ordinal)
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
            approvalPolicy,
            new FreshnessPolicy(DeploymentFreshnessChecks));
    }

    private async Task<PlanBuildResult> BuildRestartDeploymentAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Name, out var name))
        {
            return PlanBuildResult.Failed(
                "Missing required arguments: namespace and name.",
                KubernetesAdapterConventions.ResultReasonCodes.MissingArguments);
        }

        var dryRunJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunRestartDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Name] = name
            },
            ct).ConfigureAwait(false);

        var dryRun = DeserializeDryRun(dryRunJson);
        if (dryRun is null)
        {
            return PlanBuildResult.Failed(
                $"Evidence dry-run failed: {dryRunJson}",
                KubernetesAdapterConventions.ResultReasonCodes.DryRunFailed);
        }

        var diffJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DiffDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
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
            var message = $"Diff evidence failed: {diffJson}";
            return PlanBuildResult.Failed(
                message,
                DiffAudit(
                    KubernetesAdapterConventions.PlanOperations.Restart,
                    namespaceName,
                    dryRun.Objects.Select(obj => obj.Object).ToArray(),
                    message),
                KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceFailed);
        }

        var restartedAtUtc = DateTimeOffset.UtcNow.ToString(ApprovalConventions.DateTimeFormats.RoundTrip);
        var deploymentRef = new KubernetesObjectRef("apps/v1", "Deployment", namespaceName, name);
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Restart Deployment '{name}' in namespace '{namespaceName}'.",
            new Dictionary<string, string>(StringComparer.Ordinal)
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
            approvalPolicy,
            new FreshnessPolicy(DeploymentFreshnessChecks));
    }

    private async Task<PlanBuildResult> BuildSetDeploymentImageAsync(
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        CancellationToken ct)
    {
        if (!TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Namespace, out var namespaceName) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Name, out var name) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Container, out var container) ||
            !TryGetString(arguments, KubernetesAdapterConventions.EvidenceArguments.Image, out var image))
        {
            return PlanBuildResult.Failed(
                "Missing required arguments: namespace, name, container, and image.",
                KubernetesAdapterConventions.ResultReasonCodes.MissingArguments);
        }

        var policyResult = KubernetesPolicyValidator.ValidateSetDeploymentImage(
            namespaceName,
            name,
            container,
            image,
            KubernetesPolicyOptions.Default);
        if (policyResult.IsDenied)
        {
            return PlanBuildResult.Failed(
                $"Set deployment image rejected by policy:{Environment.NewLine}{policyResult.FormatRefusal()}",
                KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked);
        }

        var dryRunJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunSetDeploymentImage,
            new Dictionary<string, object?>(StringComparer.Ordinal)
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
            return PlanBuildResult.Failed(
                $"Evidence dry-run failed: {dryRunJson}",
                KubernetesAdapterConventions.ResultReasonCodes.DryRunFailed);
        }

        var diffJson = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DiffDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal)
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
            var message = $"Diff evidence failed: {diffJson}";
            return PlanBuildResult.Failed(
                message,
                DiffAudit(
                    KubernetesAdapterConventions.PlanOperations.SetImage,
                    namespaceName,
                    dryRun.Objects.Select(obj => obj.Object).ToArray(),
                    message),
                KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceFailed);
        }

        var deploymentRef = new KubernetesObjectRef("apps/v1", "Deployment", namespaceName, name);
        var payload = new KubernetesPlanPayload(
            namespaceName,
            $"Update Deployment '{name}' container '{container}' image to '{image}'.",
            new Dictionary<string, string>(StringComparer.Ordinal)
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
            approvalPolicy,
            new FreshnessPolicy(DeploymentFreshnessChecks));
    }

    private static PlanBuildResult BuildEnvelope(
        string operation,
        KubernetesPlanPayload payload,
        PlanRequester requester,
        ApprovalPolicy approvalPolicy,
        FreshnessPolicy freshnessPolicy)
    {
        var planId = ApprovalIds.NewPlanId();
        var envelope = KubernetesApprovalAdapter.ToEnvelope(
            KubernetesApprovalAdapter.CreateEnvelope(
                planId,
                operation,
                DateTimeOffset.UtcNow,
                requester,
                payload,
                freshnessPolicy: freshnessPolicy,
                approvalPolicy: approvalPolicy));

        return PlanBuildResult.Success(envelope, planId, payload.Namespace);
    }

    private static KubernetesPlanDryRun? DeserializeDryRun(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<KubernetesPlanDryRun>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static KubernetesPlanDiff[]? DeserializeDiffs(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<KubernetesPlanDiff[]>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PlanAudit DryRunAudit(string operation, string namespaceName, string message) =>
        new(
            ApprovalConventions.AuditEvents.DryRunFailed,
            new InfraGate.Approvals.AuditPayloads.DryRunFailedPayload(
                "request",
                ApprovalIds.NewPlanId(),
                operation,
                namespaceName,
                Array.Empty<string>(),
                message));

    private static PlanAudit DiffAudit(
        string operation,
        string namespaceName,
        string[] objects,
        string message) =>
        new(
            ApprovalConventions.AuditEvents.DiffFailed,
            new InfraGate.Approvals.AuditPayloads.DiffFailedPayload(
                ApprovalIds.NewPlanId(),
                operation,
                namespaceName,
                objects,
                message));

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
        if (!args.TryGetValue(key, out var raw))
        {
            value = 0;
            return false;
        }

        return TryParseIntObject(raw, out value);
    }

    private static bool TryParseIntObject(object? raw, out int value)
    {
        switch (raw)
        {
            case int i:
                value = i;
                return true;
            case long l:
                value = (int)l;
                return true;
            case double d when double.IsInteger(d) && d >= int.MinValue && d <= int.MaxValue:
                value = (int)d;
                return true;
            case string s when int.TryParse(s, out value):
                return true;
            case JsonElement element when TryGetIntFromJsonElement(element, out value):
                return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetIntFromJsonElement(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
            return true;

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
            return true;

        value = 0;
        return false;
    }
}
