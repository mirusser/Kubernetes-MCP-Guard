namespace InfraGate.KubernetesAdapter;

internal sealed record class KubernetesExecutionStartedAdapterPayload(
    string NamespaceName,
    string[] Objects,
    IReadOnlyDictionary<string, string> Parameters);
