using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.Audit;
namespace InfraGate.Approvals.Challenge;

public interface IApprovalChallengeWorkflow
{
    Task<ApprovalChallenge> CreateChallengeAsync( // NOSONAR:S107 — Interface: 7 business params + CancellationToken. Parameter-object adds ceremony.
        string planId,
        string pendingPlanHash,
        string requesterSubject,
        string? requesterAuthenticationType,
        TimeSpan ttl,
        ApprovalDigest intentDigest,
        ApprovalDigest reviewDigest,
        CancellationToken cancellationToken);

    Task<ApprovalChallenge?> GetChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken);

    Task<ApprovalChallenge?> FindPendingChallengeAsync(
        string planId,
        string pendingPlanHash,
        string subject,
        ApprovalDigest intentDigest,
        ApprovalDigest reviewDigest,
        CancellationToken cancellationToken);

    Task<ApprovalGrant> ApproveChallengeAsync(
        ApprovalChallenge challenge,
        PlanEnvelope envelope,
        string approverSubject,
        CancellationToken cancellationToken);

    Task<ApprovalChallenge> RecordChallengeOutcomeAsync(
        ApprovalChallenge challenge,
        ChallengeOutcome outcome,
        ApprovalAuditEntry entry,
        CancellationToken cancellationToken);
}
