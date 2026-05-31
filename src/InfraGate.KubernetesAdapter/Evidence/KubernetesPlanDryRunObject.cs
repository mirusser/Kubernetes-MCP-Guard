namespace InfraGate.KubernetesAdapter.Evidence;

public sealed record class KubernetesPlanDryRunObject(
    string Object,
    string ResponseJson);
