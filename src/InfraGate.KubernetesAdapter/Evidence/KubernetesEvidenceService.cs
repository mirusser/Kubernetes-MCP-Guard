using System.Text.Json;
using InfraGate.Approvals.Execution;

namespace InfraGate.KubernetesAdapter.Evidence;

internal sealed class KubernetesEvidenceService(IToolCaller toolCaller) : IKubernetesEvidenceService
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<KubernetesApplyEvidence?> GetApplyEvidenceAsync(
        string namespaceName,
        string manifest,
        CancellationToken ct)
    {
        var json = await toolCaller.CallAsync(
            KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = namespaceName,
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = manifest
            },
            ct).ConfigureAwait(false);

        return Deserialize<KubernetesApplyEvidence>(json);
    }

    public async Task<KubernetesPlanDryRun?> GetDryRunAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct)
    {
        var json = await toolCaller.CallAsync(toolName, arguments, ct).ConfigureAwait(false);

        return Deserialize<KubernetesPlanDryRun>(json);
    }

    public async Task<KubernetesPlanDiff[]?> GetDiffsAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct)
    {
        var json = await toolCaller.CallAsync(toolName, arguments, ct).ConfigureAwait(false);

        return Deserialize<KubernetesPlanDiff[]>(json);
    }

    public Task<KubernetesApplyEvidence?> CheckApplyDryRunAsync(
        string namespaceName,
        string manifest,
        CancellationToken ct) =>
        GetApplyEvidenceAsync(namespaceName, manifest, ct);

    private static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, jsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
