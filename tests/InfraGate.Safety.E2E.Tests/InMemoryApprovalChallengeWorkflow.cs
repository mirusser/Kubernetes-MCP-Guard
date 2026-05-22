using InfraGate.Approvals;

namespace InfraGate.Safety.E2E.Tests;

/// <summary>
/// Lightweight in-memory IApprovalChallengeWorkflow + IApprovalExecutionWorkflow used by
/// E2E tests in place of the removed file-backed ApprovalChallengeStore.
/// </summary>
public sealed class InMemoryApprovalChallengeWorkflow : IApprovalChallengeWorkflow, IApprovalExecutionWorkflow
{
    private readonly Dictionary<string, ApprovalChallenge> challenges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApprovalGrant> grants = new(StringComparer.Ordinal);
    private readonly HashSet<string> appliedPlans = new(StringComparer.Ordinal);

    // ── IApprovalChallengeWorkflow ────────────────────────────────────────────

    public Task<ApprovalChallenge> CreateChallengeAsync(
        string planId,
        string pendingPlanHash,
        string requesterSubject,
        string? requesterAuthenticationType,
        TimeSpan ttl,
        ApprovalDigest intentDigest,
        ApprovalDigest reviewDigest,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var challenge = new ApprovalChallenge(
            ApprovalIds.NewChallengeId(),
            planId,
            pendingPlanHash,
            requesterSubject,
            requesterAuthenticationType,
            now,
            now.Add(ttl),
            ApprovalConventions.ChallengeStatuses.Pending,
            ApproverSubject: null,
            DecidedAtUtc: null,
            intentDigest,
            reviewDigest);
        challenges[challenge.Id] = challenge;
        return Task.FromResult(challenge);
    }

    public Task<ApprovalChallenge?> GetChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(challenges.GetValueOrDefault(challengeId));

    public Task<ApprovalChallenge?> FindPendingChallengeAsync(
        string planId,
        string pendingPlanHash,
        string subject,
        ApprovalDigest intentDigest,
        ApprovalDigest reviewDigest,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var match = challenges.Values.FirstOrDefault(c =>
            string.Equals(c.Status, ApprovalConventions.ChallengeStatuses.Pending, StringComparison.Ordinal) &&
            c.ExpiresAtUtc > now &&
            string.Equals(c.PlanId, planId, StringComparison.Ordinal) &&
            FixedTimeStringComparer.Equals(c.PendingPlanHash, pendingPlanHash) &&
            string.Equals(c.RequesterSubject, subject, StringComparison.Ordinal) &&
            SameDigest(c.IntentDigest, intentDigest) &&
            SameDigest(c.ReviewDigest, reviewDigest));
        return Task.FromResult(match);
    }

    public Task<ApprovalGrant> ApproveChallengeAsync(
        ApprovalChallenge challenge,
        PlanEnvelope envelope,
        string approverSubject,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var grant = new ApprovalGrant(
            ApprovalIds.NewGrantId(),
            envelope.Id,
            envelope.Requester.Subject,
            approverSubject,
            challenge.Id,
            envelope.IntentDigest,
            envelope.ReviewDigest,
            envelope.ApprovalPolicy,
            envelope.ExecutionReusePolicy,
            now,
            envelope.ValidUntilUtc);
        grants[envelope.Id] = grant;

        challenges[challenge.Id] = challenge with
        {
            Status = ApprovalConventions.ChallengeStatuses.Approved,
            ApproverSubject = approverSubject,
            DecidedAtUtc = now,
            Outcome = new ChallengeOutcome(
                ApprovalIds.NewChallengeOutcomeId(),
                challenge.Id,
                ApprovalConventions.ChallengeOutcomeStatuses.Approved,
                approverSubject,
                now,
                reason: null,
                grantId: grant.Id)
        };
        return Task.FromResult(grant);
    }

    public Task<ApprovalChallenge> RecordChallengeOutcomeAsync(
        ApprovalChallenge challenge,
        ChallengeOutcome outcome,
        PlanAudit audit,
        CancellationToken cancellationToken)
    {
        var updated = challenge with
        {
            Status = outcome.Status,
            ApproverSubject = outcome.ActorSubject,
            DecidedAtUtc = outcome.DecidedAtUtc,
            Outcome = outcome
        };
        challenges[challenge.Id] = updated;
        return Task.FromResult(updated);
    }

    // ── IApprovalExecutionWorkflow ────────────────────────────────────────────

    public Task<BeginExecutionAttemptResult> BeginExecutionAttemptAsync(
        string planId,
        ApprovalGrant grant,
        CancellationToken cancellationToken)
    {
        var attempt = new ExecutionAttempt(
            ApprovalIds.NewExecutionAttemptId(),
            planId,
            grant.Id,
            DateTimeOffset.UtcNow);
        return Task.FromResult(BeginExecutionAttemptResult.Started(attempt));
    }

    public Task RecordExecutionBlockedAsync(
        ExecutionAttempt attempt,
        string message,
        string? reasonCode,
        PlanAudit audit,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task RecordExecutionFailedAsync(
        ExecutionAttempt attempt,
        string message,
        string? reasonCode,
        PlanAudit audit,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task RecordExecutionSucceededAsync(
        ExecutionAttempt attempt,
        ApprovalGrant grant,
        string targetNamespace,
        string message,
        PlanAudit audit,
        CancellationToken cancellationToken)
    {
        appliedPlans.Add(attempt.PlanId);
        return Task.CompletedTask;
    }

    // ── Convenience methods for E2E tests ─────────────────────────────────────

    public Task<ApprovalChallenge?> GetAsync(string challengeId, CancellationToken cancellationToken) =>
        GetChallengeAsync(challengeId, cancellationToken);

    public Task SaveAsync(ApprovalChallenge challenge, CancellationToken cancellationToken)
    {
        challenges[challenge.Id] = challenge;
        return Task.CompletedTask;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool SameDigest(ApprovalDigest left, ApprovalDigest right) =>
        string.Equals(left.Algorithm, right.Algorithm, StringComparison.Ordinal) &&
        string.Equals(left.Canonicalization, right.Canonicalization, StringComparison.Ordinal) &&
        FixedTimeStringComparer.Equals(left.Value, right.Value);
}
