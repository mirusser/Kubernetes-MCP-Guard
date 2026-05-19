namespace InfraGate.RunProfiles;

internal sealed record KubernetesAdapterProfile(string KubeConfig, IReadOnlyList<string> AllowedNamespaces);
