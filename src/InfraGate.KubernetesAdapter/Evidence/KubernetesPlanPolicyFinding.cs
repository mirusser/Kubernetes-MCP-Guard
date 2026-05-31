using InfraGate.Approvals;
using InfraGate.Approvals.Plan;

namespace InfraGate.KubernetesAdapter.Evidence;

public sealed record class KubernetesPlanPolicyFinding(
    string Severity,
    string Code,
    string ObjectRef,
    string Message) : IDomainPolicyCheck;
