namespace InfraGate.RunProfiles;

internal sealed record class KubernetesAdapterProfile(string KubeConfig, IReadOnlyList<string> AllowedNamespaces);
