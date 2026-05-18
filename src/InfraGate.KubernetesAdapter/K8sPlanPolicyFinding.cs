using InfraGate.Approvals;

namespace InfraGate.KubernetesAdapter;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed record K8sPlanPolicyFinding(
    string Severity,
    string Code,
    string ObjectRef,
    string Message) : IDomainPolicyCheck;
