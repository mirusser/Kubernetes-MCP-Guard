namespace InfraGate.KubernetesAdapter;

internal sealed record class KubernetesPreExecutionCheckedAdapterPayload(
    string NamespaceName,
    string[] Objects,
    string[] FreshnessChecks);
