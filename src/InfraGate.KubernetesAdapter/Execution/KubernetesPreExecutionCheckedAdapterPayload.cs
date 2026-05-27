namespace InfraGate.KubernetesAdapter.Execution;

internal sealed record class KubernetesPreExecutionCheckedAdapterPayload(
    string NamespaceName,
    string[] Objects,
    string[] FreshnessChecks);
