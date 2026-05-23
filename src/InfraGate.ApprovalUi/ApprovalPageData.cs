using InfraGate.Approvals;

namespace InfraGate.ApprovalUi;

public sealed record class ApprovalPageData(
    bool CanDecide,
    string? Error,
    ApprovalChallengeInfo? Challenge,
    IPlanReview? PlanReview,
    ApprovalActionUrls Actions);
