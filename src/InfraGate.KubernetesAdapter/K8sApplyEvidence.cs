namespace InfraGate.KubernetesAdapter;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed record K8sApplyEvidence(
    K8sPlanDryRun DryRun,
    K8sPlanPolicyFinding[] PolicyFindings,
    bool PolicyBlocked,
    string? PolicyRefusal);
