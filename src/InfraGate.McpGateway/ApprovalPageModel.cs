using InfraGate.Approvals;

namespace InfraGate.McpGateway;

public sealed record ApprovalPageModel(
    bool CanDecide,
    string? Error,
    ApprovalChallenge? Challenge,
    K8sPlan? Plan);
