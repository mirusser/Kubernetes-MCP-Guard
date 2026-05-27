using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Challenge;

namespace InfraGate.McpGateway;

public sealed record class ApprovalPageModel(
    bool CanDecide,
    string? Error,
    ApprovalChallenge? Challenge,
    IPlanReview? PlanReview);
