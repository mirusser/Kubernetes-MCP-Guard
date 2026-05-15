using InfraGate.Approvals;

namespace InfraGate.McpGateway;

public sealed record ApprovalPageModel(
    bool CanDecide,
    string? Error,
    ApprovalChallenge? Challenge,
    IPlanReview? PlanReview);
