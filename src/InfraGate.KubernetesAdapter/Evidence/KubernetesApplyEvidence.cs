namespace InfraGate.KubernetesAdapter.Evidence;

public sealed record class KubernetesApplyEvidence(
    KubernetesPlanDryRun DryRun,
    KubernetesPlanPolicyFinding[] PolicyFindings,
    bool PolicyBlocked,
    string? PolicyRefusal);
