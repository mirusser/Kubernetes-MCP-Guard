namespace InfraGate.Approvals;

public interface IPlanReviewRenderer
{
    string RenderReviewContent(IPlanReview planReview);
    string RenderApprovalRequiredMessage(IPlanReview planReview, string approvalUrl, DateTimeOffset expiresAtUtc);
}
