namespace InfraGate.Approvals;

public interface IApprovalChallengeStore
{
    // Callers are responsible for validating the plan validity window and capping ttl to the remaining
    // window before calling. The store does not enforce window validity.
    Task<ApprovalChallenge> CreateAsync(
        string planId,
        string pendingPlanHash,
        string requesterSubject,
        string? requesterAuthenticationType,
        TimeSpan ttl,
        ApprovalDigest intentDigest,
        ApprovalDigest reviewDigest,
        CancellationToken cancellationToken);

    Task<ApprovalChallenge?> GetAsync(string challengeId, CancellationToken cancellationToken);

    Task<ApprovalChallenge?> FindApprovedAsync(
        string planId,
        string pendingPlanHash,
        string subject,
        CancellationToken cancellationToken);

    Task<ApprovalChallenge?> FindPendingAsync(
        string planId,
        string pendingPlanHash,
        string subject,
        ApprovalDigest intentDigest,
        ApprovalDigest reviewDigest,
        CancellationToken cancellationToken);

    Task SaveAsync(ApprovalChallenge challenge, CancellationToken cancellationToken);
}
