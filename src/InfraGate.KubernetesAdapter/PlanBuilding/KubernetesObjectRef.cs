namespace InfraGate.KubernetesAdapter.PlanBuilding;

public sealed record class KubernetesObjectRef(
    string ApiVersion,
    string Kind,
    string Namespace,
    string Name);
