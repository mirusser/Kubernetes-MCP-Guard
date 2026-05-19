namespace InfraGate.KubernetesAdapter;

internal sealed record KubernetesPreExecutionCheckedAdapterPayload(
    string NamespaceName,
    string[] Objects,
    string[] FreshnessChecks);
