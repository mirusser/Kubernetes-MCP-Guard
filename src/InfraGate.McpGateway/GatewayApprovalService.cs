using InfraGate.Approvals;
using InfraGate.Approvals.AuditPayloads;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Notifications;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpGateway;

internal sealed class GatewayApprovalService : IGatewayApprovalService
{
    private readonly ApprovalStore approvalStore;
    private readonly IApprovalChallengeStore challengeStore;
    private readonly IPlanReviewAdapter planReviewAdapter;
    private readonly IPlanReviewRenderer planReviewRenderer;
    private readonly IAuthorizationCheck authorizationCheck;
    private readonly McpGatewayOptions options;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly IApprovalNotificationDispatcher notificationDispatcher;
    private readonly ILogger<GatewayApprovalService> logger;

    public GatewayApprovalService(
        ApprovalStore approvalStore,
        IApprovalChallengeStore challengeStore,
        IPlanReviewAdapter planReviewAdapter,
        IPlanReviewRenderer planReviewRenderer,
        IAuthorizationCheck authorizationCheck,
        McpGatewayOptions options,
        IHttpContextAccessor httpContextAccessor,
        IApprovalNotificationDispatcher notificationDispatcher,
        ILogger<GatewayApprovalService> logger)
    {
        this.approvalStore = approvalStore;
        this.challengeStore = challengeStore;
        this.planReviewAdapter = planReviewAdapter;
        this.planReviewRenderer = planReviewRenderer;
        this.authorizationCheck = authorizationCheck;
        this.options = options;
        this.httpContextAccessor = httpContextAccessor;
        this.notificationDispatcher = notificationDispatcher;
        this.logger = logger;
    }

    public async Task<ApprovalGateResult> EnsureApprovedOrCreateChallengeAsync(
        string planId,
        CancellationToken cancellationToken)
    {
        var requester = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        if (requester is null)
        {
            return ApprovalGateResult.Refused(
                "Refused: apply approval requires an authenticated OAuth subject.",
                McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired);
        }

        var granted = await approvalStore.GetGrantedPlanAsync(planId, cancellationToken);
        if (granted.IsGranted && granted.Envelope is not null && granted.Grant is not null)
        {
            var decoded = planReviewAdapter.TryDecodeForReview(granted.Envelope, out var decodeError);
            if (decoded is null)
            {
                var message = decodeError ?? $"Plan '{planId}' could not be decoded by the approval adapter.";
                await WriteApplyDeniedAuditAsync(planId, message, cancellationToken);

                return ApprovalGateResult.Refused(
                    $"Refused: {message}",
                    McpGatewayConventions.ApprovalReasonCodes.AdapterDecodeFailed);
            }

            var grantedAuthz = await authorizationCheck.EvaluateAsync(
                new PlanAuthorizationContext(decoded.Envelope.Requester.Subject, requester.Subject),
                cancellationToken).ConfigureAwait(false);
            if (!grantedAuthz.IsAuthorized)
            {
                return ApprovalGateResult.Refused(
                    "Refused: apply approval requires the same authenticated subject that requested the plan.",
                    McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired);
            }

            var approvedRefusal = GetPlanReadinessRefusal(decoded, planId);
            if (approvedRefusal is not null)
            {
                return ApprovalGateResult.Refused($"Refused: {approvedRefusal.Message}", approvedRefusal.ReasonCode);
            }

            return ApprovalGateResult.Approved();
        }

        if (!granted.IsGranted && granted.GrantExists)
        {
            await WriteApplyDeniedAuditAsync(planId, granted.Message, cancellationToken);

            return ApprovalGateResult.Refused($"Refused: {granted.Message}", granted.ReasonCode);
        }

        var pending = await approvalStore.GetPendingPlanAsync(planId, cancellationToken);
        if (!pending.IsPending || pending.Envelope is null || pending.Hash is null)
        {
            return ApprovalGateResult.Refused($"Refused: {pending.Message}", pending.ReasonCode);
        }

        var pendingPlan = planReviewAdapter.TryDecodeForReview(pending.Envelope, out var pendingError);
        if (pendingPlan is null)
        {
            return ApprovalGateResult.Refused(
                $"Refused: {pendingError ?? $"Plan '{planId}' could not be decoded by the approval adapter."}",
                McpGatewayConventions.ApprovalReasonCodes.AdapterDecodeFailed);
        }

        var pendingAuthz = await authorizationCheck.EvaluateAsync(
            new PlanAuthorizationContext(pendingPlan.Envelope.Requester.Subject, requester.Subject),
            cancellationToken).ConfigureAwait(false);
        if (!pendingAuthz.IsAuthorized)
        {
            return ApprovalGateResult.Refused(
                "Refused: apply approval requires the same authenticated subject that requested the plan.",
                McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired);
        }

        var pendingRefusal = GetPlanReadinessRefusal(pendingPlan, planId);
        if (pendingRefusal is not null)
        {
            return ApprovalGateResult.Refused($"Refused: {pendingRefusal.Message}", pendingRefusal.ReasonCode);
        }

        var existingChallenge = await challengeStore.FindPendingAsync(
            planId,
            pending.Hash,
            requester.Subject,
            pending.Envelope.IntentDigest,
            pending.Envelope.ReviewDigest,
            cancellationToken);
        if (existingChallenge is not null)
        {
            var approvalUrl = CreateApprovalUrl(existingChallenge.Id);
            return ApprovalGateResult.RequiresApproval(
                planReviewRenderer.RenderApprovalRequiredMessage(
                    pendingPlan,
                    approvalUrl,
                    existingChallenge.ExpiresAtUtc),
                McpGatewayConventions.ApprovalReasonCodes.ApprovalRequired,
                approvalUrl,
                existingChallenge.Id,
                existingChallenge.ExpiresAtUtc);
        }

        var now = DateTimeOffset.UtcNow;
        if (now < pending.Envelope.ValidFromUtc)
        {
            return ApprovalGateResult.Refused(
                $"Refused: plan '{planId}' validity window has not started yet.",
                McpGatewayConventions.ApprovalReasonCodes.PlanNotStarted);
        }

        if (now >= pending.Envelope.ValidUntilUtc)
        {
            return ApprovalGateResult.Refused(
                $"Refused: plan '{planId}' has expired.",
                McpGatewayConventions.ApprovalReasonCodes.PlanExpired);
        }

        var remainingWindow = pending.Envelope.ValidUntilUtc - now;
        var effectiveTtl = remainingWindow < options.ApprovalChallengeTtl ? remainingWindow : options.ApprovalChallengeTtl;

        var challenge = await challengeStore.CreateAsync(
            planId,
            pending.Hash,
            requester.Subject,
            requester.AuthenticationType,
            effectiveTtl,
            pending.Envelope.IntentDigest,
            pending.Envelope.ReviewDigest,
            cancellationToken);
        await approvalStore.WriteAuditAsync(
            ApprovalConventions.AuditEvents.ApprovalChallengeCreated,
            new ApprovalChallengeCreatedPayload(
                challenge.Id,
                challenge.PlanId,
                challenge.PendingPlanHash,
                challenge.RequesterSubject,
                challenge.RequesterAuthenticationType,
                challenge.ExpiresAtUtc),
            cancellationToken);

        var challengeApprovalUrl = CreateApprovalUrl(challenge.Id);
        return ApprovalGateResult.RequiresApproval(
            planReviewRenderer.RenderApprovalRequiredMessage(
                pendingPlan,
                challengeApprovalUrl,
                challenge.ExpiresAtUtc),
            McpGatewayConventions.ApprovalReasonCodes.ApprovalRequired,
            challengeApprovalUrl,
            challenge.Id,
            challenge.ExpiresAtUtc);
    }

    private static ResultFailure? GetPlanReadinessRefusal(IPlanReview planReview, string planId)
    {
        if (!planReview.HasReviewEvidence)
        {
            return new ResultFailure(
                MissingEvidenceMessage(planId),
                ApprovalConventions.ResultReasonCodes.MissingReviewEvidence);
        }

        return null;
    }

    public async Task<ApprovalPageModel> GetApprovalPageAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var validation = await ValidatePendingChallengeAsync(challengeId, cancellationToken);

        return validation.Error is not null
            ? new ApprovalPageModel(false, validation.Error, validation.Challenge, validation.PlanReview)
            : new ApprovalPageModel(true, null, validation.Challenge, validation.PlanReview);
    }

    public async Task<ApprovalDecisionResult> ApproveChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var validation = await ValidatePendingChallengeAsync(challengeId, cancellationToken);
        if (validation.Error is not null ||
            validation.Challenge is null ||
            validation.PlanReview is null)
        {
            return new ApprovalDecisionResult(
                false,
                validation.Error ?? "Approval challenge is invalid.",
                validation.ReasonCode ?? ApprovalConventions.ResultReasonCodes.ChallengeInvalid);
        }

        var approver = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User)!;
        var decidedAt = DateTimeOffset.UtcNow;
        var grant = await approvalStore.CreateGrantAsync(
            validation.PlanReview.Envelope,
            approver.Subject,
            validation.Challenge.Id,
            cancellationToken);
        var updated = validation.Challenge with
        {
            Status = ApprovalConventions.ChallengeStatuses.Approved,
            ApproverSubject = approver.Subject,
            DecidedAtUtc = decidedAt,
            Outcome = new ChallengeOutcome(
                ApprovalConventions.ChallengeOutcomeStatuses.Approved,
                approver.Subject,
                decidedAt,
                Reason: null,
                grant.Id)
        };
        await challengeStore.SaveAsync(updated, cancellationToken);
        await notificationDispatcher.NotifyPlanApprovedAsync(updated.PlanId, cancellationToken).ConfigureAwait(false);
        await approvalStore.WriteAuditAsync(
            ApprovalConventions.AuditEvents.ApprovalChallengeApproved,
            new ApprovalChallengeApprovedPayload(
                updated.Id,
                updated.PlanId,
                updated.PendingPlanHash,
                updated.RequesterSubject,
                updated.ApproverSubject,
                decidedAt),
            cancellationToken);

        return new ApprovalDecisionResult(
            true,
            $"Plan '{updated.PlanId}' was approved with grant '{grant.Id}'. Return to your MCP client and call execute_approved_plan again.");
    }

    public async Task<ApprovalDecisionResult> DenyChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var challenge = await challengeStore.GetAsync(challengeId, cancellationToken);
        if (challenge is null)
        {
            return new ApprovalDecisionResult(
                false,
                "Approval challenge was not found.",
                ApprovalConventions.ResultReasonCodes.ChallengeNotFound);
        }

        var approver = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        if (approver is null)
        {
            return new ApprovalDecisionResult(
                false,
                "Approval requires an authenticated OAuth subject.",
                McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired);
        }

        if (!IsPending(challenge))
        {
            return new ApprovalDecisionResult(
                false,
                $"Approval challenge is already {challenge.Status}.",
                ApprovalConventions.ResultReasonCodes.ChallengeAlreadyTerminal);
        }

        if (!SameSubject(challenge.RequesterSubject, approver.Subject))
        {
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                "Approver subject did not match requester subject.",
                cancellationToken);

            return new ApprovalDecisionResult(
                false,
                "Approval must be denied by the same authenticated subject that requested it.",
                McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired);
        }

        var decidedAt = DateTimeOffset.UtcNow;
        var denied = challenge with
        {
            Status = ApprovalConventions.ChallengeStatuses.Denied,
            ApproverSubject = approver.Subject,
            DecidedAtUtc = decidedAt,
            Outcome = new ChallengeOutcome(
                ApprovalConventions.ChallengeOutcomeStatuses.Denied,
                approver.Subject,
                decidedAt,
                Reason: null,
                GrantId: null)
        };
        await challengeStore.SaveAsync(denied, cancellationToken);
        await approvalStore.WriteAuditAsync(
            ApprovalConventions.AuditEvents.ApprovalChallengeDenied,
            new ApprovalChallengeDeniedPayload(
                denied.Id,
                denied.PlanId,
                denied.PendingPlanHash,
                denied.RequesterSubject,
                denied.ApproverSubject,
                decidedAt),
            cancellationToken);

        return new ApprovalDecisionResult(true, $"Plan '{denied.PlanId}' was denied.");
    }

    public async Task<ApprovalDecisionResult> CancelChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var challenge = await challengeStore.GetAsync(challengeId, cancellationToken);
        if (challenge is null)
        {
            return new ApprovalDecisionResult(
                false,
                "Approval challenge was not found.",
                ApprovalConventions.ResultReasonCodes.ChallengeNotFound);
        }

        var actor = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        if (actor is null)
        {
            return new ApprovalDecisionResult(
                false,
                "Approval cancellation requires an authenticated OAuth subject.",
                McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired);
        }

        if (!IsPending(challenge))
        {
            return new ApprovalDecisionResult(
                false,
                $"Approval challenge is already {challenge.Status}.",
                ApprovalConventions.ResultReasonCodes.ChallengeAlreadyTerminal);
        }

        if (challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            var expired = await ExpireChallengeAsync(challenge, cancellationToken);

            return new ApprovalDecisionResult(
                false,
                $"Approval challenge is already {expired.Status}.",
                ApprovalConventions.ResultReasonCodes.ChallengeExpired);
        }

        if (!SameSubject(challenge.RequesterSubject, actor.Subject))
        {
            await WriteChallengeRejectedAuditAsync(
                challenge,
                actor.Subject,
                "Canceling subject did not match requester subject.",
                cancellationToken);

            return new ApprovalDecisionResult(
                false,
                "Approval must be canceled by the same authenticated subject that requested it.",
                McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired);
        }

        var decidedAt = DateTimeOffset.UtcNow;
        var canceled = challenge with
        {
            Status = ApprovalConventions.ChallengeStatuses.Canceled,
            ApproverSubject = actor.Subject,
            DecidedAtUtc = decidedAt,
            Outcome = new ChallengeOutcome(
                ApprovalConventions.ChallengeOutcomeStatuses.Canceled,
                actor.Subject,
                decidedAt,
                Reason: null,
                GrantId: null)
        };
        await challengeStore.SaveAsync(canceled, cancellationToken);
        await approvalStore.WriteAuditAsync(
            ApprovalConventions.AuditEvents.ApprovalChallengeCanceled,
            new ApprovalChallengeCanceledPayload(
                canceled.Id,
                canceled.PlanId,
                canceled.PendingPlanHash,
                canceled.RequesterSubject,
                canceled.Outcome.ActorSubject,
                decidedAt),
            cancellationToken);

        return new ApprovalDecisionResult(true, $"Plan '{canceled.PlanId}' approval challenge was canceled.");
    }

    private async Task<ChallengeValidation> ValidatePendingChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var challenge = await challengeStore.GetAsync(challengeId, cancellationToken);
        if (challenge is null)
        {
            return ChallengeValidation.Invalid(
                "Approval challenge was not found.",
                ApprovalConventions.ResultReasonCodes.ChallengeNotFound);
        }

        if (!IsPending(challenge))
        {
            return ChallengeValidation.Invalid(
                $"Approval challenge is already {challenge.Status}.",
                ApprovalConventions.ResultReasonCodes.ChallengeAlreadyTerminal,
                challenge);
        }

        if (challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            var expired = await ExpireChallengeAsync(challenge, cancellationToken);

            return ChallengeValidation.Invalid(
                "Approval challenge expired. Ask the MCP client to request a new approval URL.",
                ApprovalConventions.ResultReasonCodes.ChallengeExpired,
                expired);
        }

        var approver = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        if (approver is null)
        {
            return ChallengeValidation.Invalid(
                "Approval requires an authenticated OAuth subject.",
                McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired,
                challenge);
        }

        if (!SameSubject(challenge.RequesterSubject, approver.Subject))
        {
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                "Approver subject did not match requester subject.",
                cancellationToken);

            return ChallengeValidation.Invalid(
                "Approval requires the same authenticated subject that requested the plan.",
                McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired,
                challenge);
        }

        var pending = await approvalStore.GetPendingPlanAsync(challenge.PlanId, cancellationToken);
        if (!pending.IsPending || pending.Envelope is null || pending.Hash is null)
        {
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                pending.Message,
                cancellationToken);

            return ChallengeValidation.Invalid(
                pending.Message,
                pending.ReasonCode ?? ApprovalConventions.ResultReasonCodes.PlanNotPending,
                challenge);
        }

        if (!SameDigest(challenge.IntentDigest, pending.Envelope.IntentDigest) ||
            !SameDigest(challenge.ReviewDigest, pending.Envelope.ReviewDigest))
        {
            const string message = "The pending plan digest binding changed after this approval URL was created. Ask the MCP client to request a new approval URL.";
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                message,
                cancellationToken);

            return ChallengeValidation.Invalid(
                message,
                ApprovalConventions.ResultReasonCodes.DigestChanged,
                challenge);
        }

        if (!FixedTimeStringComparer.Equals(challenge.PendingPlanHash, pending.Hash))
        {
            const string message = "The pending plan changed after this approval URL was created. Ask the MCP client to request a new approval URL.";
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                message,
                cancellationToken);

            return ChallengeValidation.Invalid(
                message,
                ApprovalConventions.ResultReasonCodes.PendingPlanChanged,
                challenge);
        }

        var decoded = planReviewAdapter.TryDecodeForReview(pending.Envelope, out var decodeError);
        if (decoded is null)
        {
            var errorMessage = decodeError ?? "Plan could not be decoded by the approval adapter.";
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                errorMessage,
                cancellationToken);

            return ChallengeValidation.Invalid(
                errorMessage,
                McpGatewayConventions.ApprovalReasonCodes.AdapterDecodeFailed,
                challenge);
        }

        if (!SameSubject(challenge.RequesterSubject, decoded.Envelope.Requester.Subject))
        {
            const string message = "The pending plan requester changed after this approval URL was created. Ask the MCP client to request a new approval URL.";
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                message,
                cancellationToken);

            return ChallengeValidation.Invalid(
                message,
                ApprovalConventions.ResultReasonCodes.RequesterChanged,
                challenge,
                decoded);
        }

        if (!decoded.HasReviewEvidence)
        {
            var message = MissingEvidenceMessage(challenge.PlanId);
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                message,
                cancellationToken);

            return ChallengeValidation.Invalid(
                message,
                ApprovalConventions.ResultReasonCodes.MissingReviewEvidence,
                challenge,
                decoded);
        }

        return ChallengeValidation.Valid(challenge, decoded);
    }

    private async Task WriteChallengeRejectedAuditAsync(
        ApprovalChallenge challenge,
        string? approverSubject,
        string reason,
        CancellationToken cancellationToken)
    {
        var decidedAt = DateTimeOffset.UtcNow;
        var rejected = challenge with
        {
            Status = ApprovalConventions.ChallengeStatuses.Rejected,
            ApproverSubject = approverSubject,
            DecidedAtUtc = decidedAt,
            Outcome = new ChallengeOutcome(
                ApprovalConventions.ChallengeOutcomeStatuses.Rejected,
                approverSubject,
                decidedAt,
                reason,
                GrantId: null)
        };
        await challengeStore.SaveAsync(rejected, cancellationToken);
        await approvalStore.WriteAuditAsync(
            ApprovalConventions.AuditEvents.ApprovalChallengeRejected,
            new ApprovalChallengeRejectedPayload(
                challenge.Id,
                challenge.PlanId,
                challenge.PendingPlanHash,
                challenge.RequesterSubject,
                approverSubject,
                reason),
            cancellationToken);
    }

    private async Task<ApprovalChallenge> ExpireChallengeAsync(
        ApprovalChallenge challenge,
        CancellationToken cancellationToken)
    {
        var decidedAt = DateTimeOffset.UtcNow;
        var expired = challenge with
        {
            Status = ApprovalConventions.ChallengeStatuses.Expired,
            DecidedAtUtc = decidedAt,
            Outcome = new ChallengeOutcome(
                ApprovalConventions.ChallengeOutcomeStatuses.Expired,
                ActorSubject: null,
                decidedAt,
                "Challenge TTL expired.",
                GrantId: null)
        };
        await challengeStore.SaveAsync(expired, cancellationToken);
        await approvalStore.WriteAuditAsync(
            ApprovalConventions.AuditEvents.ApprovalChallengeExpired,
            new ApprovalChallengeExpiredPayload(
                expired.Id,
                expired.PlanId,
                expired.PendingPlanHash,
                expired.RequesterSubject,
                expired.ExpiresAtUtc),
            cancellationToken);

        return expired;
    }

    private async Task WriteApplyDeniedAuditAsync(
        string planId,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await approvalStore.WriteAuditAsync(
                ApprovalConventions.AuditEvents.ApplyDenied,
                new ApplyDeniedPayload(planId, message),
                cancellationToken);
        }
        catch (IOException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to write approval audit event {EventName} for plan {PlanId}.",
                ApprovalConventions.AuditEvents.ApplyDenied,
                planId);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to write approval audit event {EventName} for plan {PlanId}.",
                ApprovalConventions.AuditEvents.ApplyDenied,
                planId);
        }
    }

    private string CreateApprovalUrl(string challengeId)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(options.ApprovalBaseUrl)
            ? options.ApprovalBaseUrl
            : RequestBaseUrl();

        return $"{baseUrl.TrimEnd('/')}{McpGatewayConventions.Approvals.PathPrefix}/{challengeId}";
    }

    private string RequestBaseUrl()
    {
        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            return McpGatewayOptions.DefaultUrl;
        }

        return $"{request.Scheme}://{request.Host}{request.PathBase}";
    }

    private static bool IsPending(ApprovalChallenge challenge) =>
        string.Equals(challenge.Status, ApprovalConventions.ChallengeStatuses.Pending, StringComparison.Ordinal);

    private static bool SameSubject(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static bool SameDigest(ApprovalDigest? left, ApprovalDigest right) =>
        left is not null && left == right;

    private static string MissingEvidenceMessage(string planId) =>
        $"Plan '{planId}' is missing recorded evidence data. Ask the MCP client to re-request the plan.";

    private sealed record ChallengeValidation(
        string? Error,
        string? ReasonCode,
        ApprovalChallenge? Challenge,
        IPlanReview? PlanReview)
    {
        public static ChallengeValidation Valid(ApprovalChallenge challenge, IPlanReview planReview) =>
            new(null, null, challenge, planReview);

        public static ChallengeValidation Invalid(
            string error,
            string reasonCode,
            ApprovalChallenge? challenge = null,
            IPlanReview? planReview = null) =>
            new(error, reasonCode, challenge, planReview);
    }

    private sealed record ResultFailure(string Message, string ReasonCode);
}
