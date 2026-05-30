using InfraGate.Approvals;
using InfraGate.Approvals.Plan;

namespace InfraGate.KubernetesAdapter.Evidence;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed record class KubernetesPlanPolicyFinding(
    string Severity,
    string Code,
    string ObjectRef,
    string Message) : IDomainPolicyCheck;
