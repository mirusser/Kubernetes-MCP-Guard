using InfraGate.Approvals;

namespace InfraGate.McpGateway;

public sealed record class ApprovalPageModel(
    bool CanDecide,
    string? Error,
    ApprovalChallenge? Challenge,
    IPlanReview? PlanReview);
