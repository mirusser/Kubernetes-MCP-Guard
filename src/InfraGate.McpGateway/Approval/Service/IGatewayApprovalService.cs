namespace InfraGate.McpGateway;

public interface IGatewayApprovalService
{
    Task<ApprovalGateResult> EnsureApprovedOrCreateChallengeAsync(
        string planId,
        CancellationToken cancellationToken);

    Task<ApprovalPageModel> GetApprovalPageAsync(
        string challengeId,
        CancellationToken cancellationToken);

    Task<ApprovalDecisionResult> ApproveChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken);

    Task<ApprovalDecisionResult> DenyChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken);

    Task<ApprovalDecisionResult> CancelChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken);
}
