using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Challenge;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.PreExecution;
using InfraGate.Approvals.AuditPayloads;

namespace InfraGate.McpGateway.Tests.UnitTests;

internal sealed class TestApprovalWorkflow :
    IApprovalPlanWorkflow,
    IApprovalChallengeWorkflow,
    IApprovalExecutionWorkflow,
    IApprovalAuditPublisher
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly Dictionary<string, PlanRecord> plans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApprovalChallenge> challenges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApprovalGrant> grants = new(StringComparer.Ordinal);
    private readonly HashSet<string> appliedPlans = new(StringComparer.Ordinal);
    private readonly List<string> auditLines = [];
    private readonly Dictionary<string, ExecutionAttempt> attempts = new(StringComparer.Ordinal);

    // ── IApprovalPlanWorkflow ─────────────────────────────────────────────────

    public Task<ApprovalPlanResult> CreatePlanAsync(
        PlanEnvelope envelope,
        string targetNamespace,
        CancellationToken cancellationToken)
    {
        var json = ApprovalCanonicalJson.Serialize(envelope);
        var hash = ApprovalCanonicalJson.ComputeSha256Hex(json);
        plans[envelope.Id] = new PlanRecord(envelope, targetNamespace, hash);
        AppendAudit(ApprovalConventions.AuditEvents.PlanRequested,
            new PlanRequestedPayload(envelope.Id, envelope.Operation, targetNamespace, hash,
                envelope.IntentDigest, envelope.ReviewDigest));
        return Task.FromResult(new ApprovalPlanResult(envelope, string.Empty, hash));
    }

    public Task<PendingPlanResult> GetPendingPlanAsync(string planId, CancellationToken cancellationToken)
    {
        if (!plans.TryGetValue(planId, out var record))
        {
            return Task.FromResult(PendingPlanResult.Denied(
                $"No pending plan exists for '{planId}'.",
                ApprovalConventions.ResultReasonCodes.PlanNotPending));
        }

        if (appliedPlans.Contains(planId))
        {
            return Task.FromResult(PendingPlanResult.Denied(
                $"Plan '{planId}' was already applied.",
                ApprovalConventions.ResultReasonCodes.PlanAlreadyApplied));
        }

        return Task.FromResult(PendingPlanResult.Found(record.Envelope, string.Empty, record.Hash));
    }

    public Task<GrantedPlanResult> GetGrantedPlanAsync(string planId, CancellationToken cancellationToken)
    {
        if (!plans.TryGetValue(planId, out var record))
        {
            return Task.FromResult(GrantedPlanResult.MissingGrant(
                $"No pending plan exists for '{planId}'.",
                ApprovalConventions.ResultReasonCodes.PlanNotPending));
        }

        if (appliedPlans.Contains(planId))
        {
            return Task.FromResult(GrantedPlanResult.Denied(
                $"Plan '{planId}' was already applied.",
                grantExists: false,
                reasonCode: ApprovalConventions.ResultReasonCodes.PlanAlreadyApplied));
        }

        if (!grants.TryGetValue(planId, out var grant))
        {
            return Task.FromResult(GrantedPlanResult.MissingGrant(
                $"Plan '{planId}' is not approved yet.",
                ApprovalConventions.ResultReasonCodes.PlanNotApproved));
        }

        var validation = ApprovalGrantValidation.Validate(record.Envelope, grant);
        return Task.FromResult(validation is null
            ? GrantedPlanResult.Granted(record.Envelope, grant)
            : GrantedPlanResult.Denied(validation.Value.Message, reasonCode: validation.Value.ReasonCode));
    }

    public Task<PlanStatusResult> GetPlanStatusAsync(string planId, CancellationToken cancellationToken)
    {
        if (appliedPlans.Contains(planId))
            return Task.FromResult(new PlanStatusResult(PlanStatus.Applied));

        if (grants.TryGetValue(planId, out var grant))
        {
            var status = grant.ExpiresAtUtc <= DateTimeOffset.UtcNow
                ? PlanStatus.Expired
                : PlanStatus.Approved;
            return Task.FromResult(new PlanStatusResult(status));
        }

        return Task.FromResult(plans.ContainsKey(planId)
            ? new PlanStatusResult(PlanStatus.ApprovalRequired)
            : new PlanStatusResult(PlanStatus.NotFound));
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

    public Task<ApprovalChallenge?> GetChallengeAsync(string challengeId, CancellationToken cancellationToken) =>
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
            ApprovalGrantValidation.SameDigest(c.IntentDigest, intentDigest) &&
            ApprovalGrantValidation.SameDigest(c.ReviewDigest, reviewDigest));
        return Task.FromResult(match);
    }

    public Task<ApprovalGrant> ApproveChallengeAsync(
        ApprovalChallenge challenge,
        PlanEnvelope envelope,
        string approverSubject,
        CancellationToken cancellationToken)
    {
        var grant = CreateGrant(envelope, approverSubject, challenge.Id);
        var decidedAt = DateTimeOffset.UtcNow;
        challenges[challenge.Id] = challenge with
        {
            Status = ApprovalConventions.ChallengeStatuses.Approved,
            ApproverSubject = approverSubject,
            DecidedAtUtc = decidedAt,
            Outcome = new ChallengeOutcome(
                ApprovalIds.NewChallengeOutcomeId(),
                challenge.Id,
                ApprovalConventions.ChallengeOutcomeStatuses.Approved,
                approverSubject,
                decidedAt,
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
        AppendAudit(audit.EventName, audit.Payload);
        return Task.FromResult(updated);
    }

    // ── IApprovalExecutionWorkflow ────────────────────────────────────────────

    public async Task<BeginExecutionAttemptResult> BeginExecutionAttemptAsync(
        string planId,
        ApprovalGrant grant,
        CancellationToken cancellationToken)
    {
        var granted = await GetGrantedPlanAsync(planId, cancellationToken).ConfigureAwait(false);
        if (!granted.IsGranted || granted.Envelope is null)
        {
            return BeginExecutionAttemptResult.Refused(
                granted.Message,
                granted.ReasonCode ?? ApprovalConventions.ResultReasonCodes.PlanNotApproved);
        }

        var attempt = new ExecutionAttempt(
            ApprovalIds.NewExecutionAttemptId(),
            planId,
            grant.Id,
            DateTimeOffset.UtcNow);
        attempts[attempt.Id] = attempt;

        return BeginExecutionAttemptResult.Started(attempt);
    }

    public Task RecordExecutionBlockedAsync(
        ExecutionAttempt attempt,
        string message,
        string? reasonCode,
        PlanAudit audit,
        CancellationToken cancellationToken)
    {
        AppendAudit(audit.EventName, audit.Payload);
        return Task.CompletedTask;
    }

    public Task RecordExecutionFailedAsync(
        ExecutionAttempt attempt,
        string message,
        string? reasonCode,
        PlanAudit audit,
        CancellationToken cancellationToken)
    {
        AppendAudit(audit.EventName, audit.Payload);
        return Task.CompletedTask;
    }

    public Task RecordExecutionSucceededAsync(
        ExecutionAttempt attempt,
        ApprovalGrant grant,
        string targetNamespace,
        string message,
        PlanAudit audit,
        CancellationToken cancellationToken)
    {
        appliedPlans.Add(attempt.PlanId);
        AppendAudit(audit.EventName, audit.Payload);
        return Task.CompletedTask;
    }

    // ── IApprovalAuditPublisher ───────────────────────────────────────────────

    public Task PublishAsync(PlanAudit audit, CancellationToken cancellationToken)
    {
        AppendAudit(audit.EventName, audit.Payload);
        return Task.CompletedTask;
    }

    // ── Test setup helpers ────────────────────────────────────────────────────

    public Task<ApprovalGrant> CreateGrantAsync(
        PlanEnvelope envelope,
        string approverSubject,
        string sourceChallengeId,
        CancellationToken cancellationToken)
    {
        var grant = CreateGrant(envelope, approverSubject, sourceChallengeId);
        return Task.FromResult(grant);
    }

    public Task MarkAppliedAsync(
        PlanEnvelope envelope,
        string targetNamespace,
        ApprovalGrant grant,
        CancellationToken cancellationToken)
    {
        appliedPlans.Add(envelope.Id);
        return Task.CompletedTask;
    }

    public Task<ApprovalGrant?> GetGrantAsync(string planId, CancellationToken cancellationToken) =>
        Task.FromResult(grants.GetValueOrDefault(planId));

    // ── Test tamper helpers ───────────────────────────────────────────────────

    public void TamperPlanHash(string planId, string newHash = "tampered-hash")
    {
        if (plans.TryGetValue(planId, out var record))
        {
            plans[planId] = record with { Hash = newHash };
        }
    }

    public void TamperChallengeExpiry(string challengeId, DateTimeOffset expiry)
    {
        if (challenges.TryGetValue(challengeId, out var challenge))
        {
            challenges[challengeId] = challenge with { ExpiresAtUtc = expiry };
        }
    }

    public void TamperEvidenceArtifactDigest(string planId, string newValue = "tampered-evidence")
    {
        if (!plans.TryGetValue(planId, out var record))
        {
            return;
        }

        var artifacts = record.Envelope.EvidenceArtifacts;
        if (artifacts.Length == 0)
        {
            return;
        }

        var newArtifacts = (EvidenceArtifactSummary[])artifacts.Clone();
        newArtifacts[0] = artifacts[0] with
        {
            Digest = artifacts[0].Digest with { Value = newValue }
        };
        plans[planId] = record with { Envelope = record.Envelope with { EvidenceArtifacts = newArtifacts } };
    }

    // ── Test inspection helpers ───────────────────────────────────────────────

    public bool IsGranted(string planId) => grants.ContainsKey(planId);

    public bool IsApplied(string planId) => appliedPlans.Contains(planId);

    public int ChallengeCount => challenges.Count;

    public ApprovalChallenge? GetChallenge(string challengeId) =>
        challenges.GetValueOrDefault(challengeId);

    public string GetAuditEventsJson() =>
        string.Join(Environment.NewLine, auditLines);

    // ── Private helpers ───────────────────────────────────────────────────────

    private ApprovalGrant CreateGrant(PlanEnvelope envelope, string approverSubject, string sourceChallengeId)
    {
        var now = DateTimeOffset.UtcNow;
        var grant = new ApprovalGrant(
            ApprovalIds.NewGrantId(),
            envelope.Id,
            envelope.Requester.Subject,
            approverSubject,
            sourceChallengeId,
            envelope.IntentDigest,
            envelope.ReviewDigest,
            envelope.ApprovalPolicy,
            envelope.ExecutionReusePolicy,
            now,
            envelope.ValidUntilUtc);
        grants[envelope.Id] = grant;
        return grant;
    }

    private void AppendAudit(string eventName, object payload)
    {
        var line = JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            eventName,
            payload
        }, jsonOptions);
        auditLines.Add(line);
    }

    private sealed record class PlanRecord(PlanEnvelope Envelope, string TargetNamespace, string Hash);
}
