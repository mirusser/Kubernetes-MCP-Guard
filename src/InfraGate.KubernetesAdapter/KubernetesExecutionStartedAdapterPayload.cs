namespace InfraGate.KubernetesAdapter;

internal sealed record KubernetesExecutionStartedAdapterPayload(
    string NamespaceName,
    string[] Objects,
    IReadOnlyDictionary<string, string> Parameters);
