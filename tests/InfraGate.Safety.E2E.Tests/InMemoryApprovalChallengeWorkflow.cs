using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Challenge;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.PreExecution;

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
    private readonly IApprovalAuditPublisher auditPublisher;
    private readonly ApprovalStore? approvalStore;

    public InMemoryApprovalChallengeWorkflow(IApprovalAuditPublisher? auditPublisher = null)
    {
        this.auditPublisher = auditPublisher ?? NoOpApprovalAuditPublisher.Instance;
        this.approvalStore = auditPublisher as ApprovalStore;
    }

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

    public async Task<ApprovalGrant> ApproveChallengeAsync(
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

        if (approvalStore is not null)
        {
            var path = approvalStore.GetGrantPath(envelope.Id);
            var json = System.Text.Json.JsonSerializer.Serialize(grant);
            await System.IO.File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }

        await auditPublisher.PublishAsync(
            new PlanAudit(
                ApprovalConventions.AuditEvents.ApprovalChallengeApproved,
                new InfraGate.Approvals.AuditPayloads.ApprovalChallengeApprovedPayload(
                    challenge.Id,
                    challenge.PlanId,
                    challenge.PendingPlanHash,
                    challenge.RequesterSubject,
                    approverSubject,
                    now)),
            cancellationToken).ConfigureAwait(false);

        await auditPublisher.PublishAsync(
            new PlanAudit(
                ApprovalConventions.AuditEvents.GrantIssued,
                new InfraGate.Approvals.AuditPayloads.ApprovalGrantIssuedPayload(
                    grant.PlanId,
                    grant.Id,
                    grant.SourceChallengeId,
                    grant.RequesterSubject,
                    grant.ApproverSubject,
                    grant.IntentDigest,
                    grant.ReviewDigest,
                    grant.ExpiresAtUtc)),
            cancellationToken).ConfigureAwait(false);

        return grant;
    }

    public async Task<ApprovalChallenge> RecordChallengeOutcomeAsync(
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
        await auditPublisher.PublishAsync(audit, cancellationToken).ConfigureAwait(false);
        return updated;
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
        auditPublisher.PublishAsync(audit, cancellationToken);

    public Task RecordExecutionFailedAsync(
        ExecutionAttempt attempt,
        string message,
        string? reasonCode,
        PlanAudit audit,
        CancellationToken cancellationToken) =>
        auditPublisher.PublishAsync(audit, cancellationToken);

    public async Task RecordExecutionSucceededAsync(
        ExecutionAttempt attempt,
        ApprovalGrant grant,
        string targetNamespace,
        string message,
        PlanAudit audit,
        CancellationToken cancellationToken)
    {
        appliedPlans.Add(attempt.PlanId);
        if (approvalStore is not null)
        {
            var path = approvalStore.GetAppliedPath(attempt.PlanId);
            await System.IO.File.WriteAllTextAsync(path, "{}", cancellationToken).ConfigureAwait(false);
        }
        await auditPublisher.PublishAsync(audit, cancellationToken).ConfigureAwait(false);
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
