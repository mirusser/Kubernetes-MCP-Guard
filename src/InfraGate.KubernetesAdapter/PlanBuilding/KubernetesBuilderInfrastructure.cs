using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter.Approval;
using InfraGate.KubernetesAdapter.Policy;
using k8s;
using k8s.Models;
using YamlDotNet.Core;

namespace InfraGate.KubernetesAdapter.PlanBuilding;

internal static class KubernetesBuilderInfrastructure
{
    internal static readonly IReadOnlyList<FreshnessCheck> manifestFreshnessChecks =
    [
        new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.LiveDrift, new Dictionary<string, string>(StringComparer.Ordinal)),
        new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>(StringComparer.Ordinal))
    ];

    internal static readonly IReadOnlyList<FreshnessCheck> deploymentFreshnessChecks =
    [
        new FreshnessCheck(KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun, new Dictionary<string, string>(StringComparer.Ordinal))
    ];

    private static readonly Dictionary<string, Type> manifestTypeMap = new(StringComparer.Ordinal)
    {
        [$"{KubernetesAdapterConventions.ApiVersions.AppsV1}/{KubernetesAdapterConventions.ResourceKinds.Deployment}"] = typeof(V1Deployment),
        [$"{KubernetesAdapterConventions.ApiVersions.V1}/{KubernetesAdapterConventions.ResourceKinds.Service}"] = typeof(V1Service),
        [$"{KubernetesAdapterConventions.ApiVersions.V1}/{KubernetesAdapterConventions.ResourceKinds.ConfigMap}"] = typeof(V1ConfigMap)
    };

    internal static PlanBuildResult BuildEnvelope(
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

    internal static bool TryGetString(IReadOnlyDictionary<string, object?> args, string key, out string value)
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
            // JSON string value guaranteed non-null after kind+null checks above.
            value = element.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    internal static bool TryGetInt(IReadOnlyDictionary<string, object?> args, string key, out int value)
    {
        if (!args.TryGetValue(key, out var raw))
        {
            value = 0;
            return false;
        }

        return TryParseIntObject(raw, out value);
    }

    internal static bool TryParseIntObject(object? raw, out int value)
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

    internal static bool TryGetIntFromJsonElement(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
            return true;

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
            return true;

        value = 0;
        return false;
    }

    internal static KubernetesPolicyResult CheckManifestPolicy(string manifest)
    {
        try
        {
            var objects = KubernetesYaml.LoadAllFromString(manifest, manifestTypeMap)
                .OfType<IKubernetesObject<V1ObjectMeta>>()
                .ToList();
            return KubernetesPolicyValidator.Validate(objects, KubernetesPolicyOptions.Default);
        }
        catch (YamlException)
        {
            return new KubernetesPolicyResult([]);
        }
        catch (ArgumentException)
        {
            return new KubernetesPolicyResult([]);
        }
        catch (KeyNotFoundException)
        {
            return new KubernetesPolicyResult([]);
        }
    }
}
