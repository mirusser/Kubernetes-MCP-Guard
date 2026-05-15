using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;

namespace InfraGate.McpGateway;

public sealed record ApprovalPageModel(
    bool CanDecide,
    string? Error,
    ApprovalChallenge? Challenge,
    KubernetesPlan? Plan);
