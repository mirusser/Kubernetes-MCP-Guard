using InfraGate.Approvals;

namespace InfraGate.KubernetesAdapter;

internal sealed class KubernetesDomainAdapter(
    IDomainPlanBuilder planBuilder,
    IDomainPlanExecutor planExecutor,
    IPlanReviewAdapter planReviewAdapter,
    IPlanReviewRenderer planReviewRenderer) : IDomainAdapter
{
    public string AdapterId => planReviewAdapter.AdapterId;

    public Task<PlanBuildResult> BuildAsync(
        string mutationToolName,
        IReadOnlyDictionary<string, object?> arguments,
        PlanRequester requester,
        CancellationToken ct) =>
        planBuilder.BuildAsync(mutationToolName, arguments, requester, ct);

    public Task<DomainPlanExecutionResult> CheckPreExecutionAsync(PlanEnvelope envelope, CancellationToken ct) =>
        planExecutor.CheckPreExecutionAsync(envelope, ct);

    public Task<DomainPlanExecutionResult> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct) =>
        planExecutor.ExecuteAsync(envelope, ct);

    public IPlanReview? TryDecodeForReview(PlanEnvelope envelope, out string? error) =>
        planReviewAdapter.TryDecodeForReview(envelope, out error);

    public string RenderReviewContent(IPlanReview planReview) =>
        planReviewRenderer.RenderReviewContent(planReview);

    public string RenderApprovalRequiredMessage(IPlanReview planReview, string approvalUrl, DateTimeOffset expiresAtUtc) =>
        planReviewRenderer.RenderApprovalRequiredMessage(planReview, approvalUrl, expiresAtUtc);
}
