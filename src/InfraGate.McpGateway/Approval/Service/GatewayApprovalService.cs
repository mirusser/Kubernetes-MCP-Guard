using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.Challenge;
using InfraGate.Approvals.PreExecution;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.AuditPayloads;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Notifications;

namespace InfraGate.McpGateway;

internal sealed class GatewayApprovalService(
    IApprovalPlanWorkflow approvalPlans,
    IApprovalChallengeWorkflow approvalChallenges,
    IApprovalAuditOutbox auditOutbox,
    IPlanReviewAdapter planReviewAdapter,
    IAuthorizationCheck authorizationCheck,
    McpGatewayOptions options,
    IHttpContextAccessor httpContextAccessor,
    IApprovalNotificationDispatcher notificationDispatcher,
    ILogger<GatewayApprovalService> logger) : IGatewayApprovalService
{

    public async Task<ApprovalGateResult> EnsureApprovedOrCreateChallengeAsync(
        string planId,
        CancellationToken cancellationToken)
    {
        var requester = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        if (requester is null)
        {
            return ApprovalGateResult.Refused(
                McpGatewayMessages.Authorization.RefusedAuthenticatedSubjectRequired(),
                McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired);
        }

        var granted = await approvalPlans.GetGrantedPlanAsync(planId, cancellationToken).ConfigureAwait(false);
        if (granted.IsGranted && granted.Envelope is not null && granted.Grant is not null)
        {
            return await GetGrantedApprovalResultAsync(planId, granted.Envelope, requester, cancellationToken).ConfigureAwait(false);
        }

        if (!granted.IsGranted && granted.GrantExists)
        {
            await WriteApplyDeniedAuditAsync(planId, granted.Message, cancellationToken).ConfigureAwait(false);

            return ApprovalGateResult.Refused($"Refused: {granted.Message}", granted.ReasonCode);
        }

        return await HandlePendingPlanAsync(planId, requester, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ApprovalGateResult> GetGrantedApprovalResultAsync(
        string planId,
        PlanEnvelope envelope,
        GatewayApprovalIdentity requester,
        CancellationToken cancellationToken)
    {
        var decoded = planReviewAdapter.TryDecodeForReview(envelope, out var decodeError);
        if (decoded is null)
        {
            var message = decodeError ?? McpGatewayMessages.Approval.AdapterDecodeFailedWithId(planId);
            await WriteApplyDeniedAuditAsync(planId, message, cancellationToken).ConfigureAwait(false);

            return ApprovalGateResult.Refused(
                $"Refused: {message}",
                McpGatewayConventions.ApprovalReasonCodes.AdapterDecodeFailed);
        }

        var grantedAuthz = await authorizationCheck.EvaluateAsync(
            new PlanAuthorizationContext(
                decoded.Envelope.Requester.Subject,
                requester.Subject,
                decoded.Envelope.ApprovalPolicy,
                GetActorGroups()),
            cancellationToken).ConfigureAwait(false);
        if (!grantedAuthz.IsAuthorized)
        {
            return ApprovalGateResult.Refused(
                McpGatewayMessages.Authorization.RefusedSameSubjectRequired(),
                McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired);
        }

        var approvedRefusal = GetPlanReadinessRefusal(decoded, planId);
        if (approvedRefusal is not null)
        {
            return ApprovalGateResult.Refused($"Refused: {approvedRefusal.Message}", approvedRefusal.ReasonCode);
        }

        return ApprovalGateResult.Approved();
    }

    private async Task<ApprovalGateResult> HandlePendingPlanAsync(
        string planId,
        GatewayApprovalIdentity requester,
        CancellationToken cancellationToken)
    {
        var pending = await approvalPlans.GetPendingPlanAsync(planId, cancellationToken).ConfigureAwait(false);
        if (!pending.IsPending || pending.Envelope is null || pending.Hash is null)
        {
            return ApprovalGateResult.Refused($"Refused: {pending.Message}", pending.ReasonCode);
        }

        var pendingPlan = planReviewAdapter.TryDecodeForReview(pending.Envelope, out var pendingError);
        if (pendingPlan is null)
        {
            var adapterError = pendingError ?? McpGatewayMessages.Approval.AdapterDecodeFailedWithId(planId);
            return ApprovalGateResult.Refused(
                $"Refused: {adapterError}",
                McpGatewayConventions.ApprovalReasonCodes.AdapterDecodeFailed);
        }

        var pendingAuthz = await authorizationCheck.EvaluateAsync(
            new PlanAuthorizationContext(
                pendingPlan.Envelope.Requester.Subject,
                requester.Subject,
                pendingPlan.Envelope.ApprovalPolicy,
                GetActorGroups()),
            cancellationToken).ConfigureAwait(false);
        if (!pendingAuthz.IsAuthorized)
        {
            return ApprovalGateResult.Refused(
                McpGatewayMessages.Authorization.RefusedSameSubjectRequired(),
                McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired);
        }

        var pendingRefusal = GetPlanReadinessRefusal(pendingPlan, planId);
        if (pendingRefusal is not null)
        {
            return ApprovalGateResult.Refused($"Refused: {pendingRefusal.Message}", pendingRefusal.ReasonCode);
        }

        // Use the plan's stored requester subject — not re-resolved from the HTTP context — so the
        // challenge's RequesterSubject always matches decoded.Envelope.Requester.Subject at approval time.
        // GatewayAuditIdentityResolver (used when writing the plan) and GatewayApprovalIdentityResolver
        // (used for the current HTTP context) format service-account subjects differently.
        var planRequesterSubject = pendingPlan.Envelope.Requester.Subject;

        var existingChallenge = await approvalChallenges.FindPendingChallengeAsync(
            planId,
            pending.Hash,
            planRequesterSubject,
            pending.Envelope.IntentDigest,
            pending.Envelope.ReviewDigest,
            cancellationToken).ConfigureAwait(false);
        if (existingChallenge is not null)
        {
            var approvalUrl = CreateApprovalUrl(existingChallenge.Id);
            return ApprovalGateResult.RequiresApproval(
                ApprovalMessageFormatter.RenderApprovalRequiredMessage(
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
                McpGatewayMessages.Approval.RefusedPlanNotStarted(planId),
                McpGatewayConventions.ApprovalReasonCodes.PlanNotStarted);
        }

        if (now >= pending.Envelope.ValidUntilUtc)
        {
            return ApprovalGateResult.Refused(
                McpGatewayMessages.Approval.RefusedPlanExpired(planId),
                McpGatewayConventions.ApprovalReasonCodes.PlanExpired);
        }

        var remainingWindow = pending.Envelope.ValidUntilUtc - now;
        var effectiveTtl = remainingWindow < options.ApprovalChallengeTtl ? remainingWindow : options.ApprovalChallengeTtl;

        var challenge = await approvalChallenges.CreateChallengeAsync(
            planId,
            pending.Hash,
            planRequesterSubject,
            requester.AuthenticationType,
            effectiveTtl,
            pending.Envelope.IntentDigest,
            pending.Envelope.ReviewDigest,
            cancellationToken).ConfigureAwait(false);

        var challengeApprovalUrl = CreateApprovalUrl(challenge.Id);
        return ApprovalGateResult.RequiresApproval(
            ApprovalMessageFormatter.RenderApprovalRequiredMessage(
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
                McpGatewayMessages.Approval.MissingEvidence(planId),
                ApprovalConventions.ResultReasonCodes.MissingReviewEvidence);
        }

        return null;
    }

    public async Task<ApprovalPageModel> GetApprovalPageAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var validation = await ValidatePendingChallengeAsync(challengeId, cancellationToken).ConfigureAwait(false);

        return validation.Error is not null
            ? new ApprovalPageModel(false, validation.Error, validation.Challenge, validation.PlanReview)
            : new ApprovalPageModel(true, null, validation.Challenge, validation.PlanReview);
    }

    public async Task<ApprovalDecisionResult> ApproveChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var validation = await ValidatePendingChallengeAsync(challengeId, cancellationToken).ConfigureAwait(false);
        if (validation.Error is not null ||
            validation.Challenge is null ||
            validation.PlanReview is null)
        {
            return new ApprovalDecisionResult(
                false,
                validation.Error ?? McpGatewayMessages.Approval.ChallengeInvalid,
                validation.ReasonCode ?? ApprovalConventions.ResultReasonCodes.ChallengeInvalid);
        }

        var approver = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User)!;
        var decidedAt = DateTimeOffset.UtcNow;
        var grant = await approvalChallenges.ApproveChallengeAsync(
            validation.Challenge,
            validation.PlanReview.Envelope,
            approver.Subject,
            cancellationToken).ConfigureAwait(false);
        var updated = validation.Challenge with
        {
            Status = ApprovalConventions.ChallengeStatuses.Approved,
            ApproverSubject = approver.Subject,
            DecidedAtUtc = decidedAt,
            Outcome = new ChallengeOutcome(
                ApprovalIds.NewChallengeOutcomeId(),
                validation.Challenge.Id,
                ApprovalConventions.ChallengeOutcomeStatuses.Approved,
                approver.Subject,
                decidedAt,
                reason: null,
                grantId: grant.Id)
        };
        await notificationDispatcher.NotifyPlanApprovedAsync(updated.PlanId, cancellationToken).ConfigureAwait(false);

        return new ApprovalDecisionResult(
            true,
            McpGatewayMessages.Approval.PlanApproved(updated.PlanId, grant.Id));
    }

    public async Task<ApprovalDecisionResult> DenyChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var challenge = await approvalChallenges.GetChallengeAsync(challengeId, cancellationToken).ConfigureAwait(false);
        if (challenge is null)
        {
            return new ApprovalDecisionResult(
                false,
                McpGatewayMessages.Approval.ChallengeNotFound,
                ApprovalConventions.ResultReasonCodes.ChallengeNotFound);
        }

        var approver = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        if (approver is null)
        {
            return new ApprovalDecisionResult(
                false,
                McpGatewayMessages.Authorization.AuthenticatedSubjectRequired,
                McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired);
        }

        if (!IsPending(challenge))
        {
            return new ApprovalDecisionResult(
                false,
                McpGatewayMessages.Approval.ChallengeAlreadyTerminal(challenge.Status),
                ApprovalConventions.ResultReasonCodes.ChallengeAlreadyTerminal);
        }

        var policyFailure = await ValidateChallengeDecisionPolicyAsync(
            challenge,
            approver,
            cancellationToken).ConfigureAwait(false);
        if (policyFailure is not null)
        {
            return policyFailure;
        }

        var decidedAt = DateTimeOffset.UtcNow;
        var denied = await approvalChallenges.RecordChallengeOutcomeAsync(
            challenge,
            new ChallengeOutcome(
                ApprovalIds.NewChallengeOutcomeId(),
                challenge.Id,
                ApprovalConventions.ChallengeOutcomeStatuses.Denied,
                approver.Subject,
                decidedAt,
                reason: null,
                grantId: null),
            new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.ApprovalChallengeDenied,
                new ApprovalChallengeDeniedPayload(
                    challenge.Id,
                    challenge.PlanId,
                    challenge.PendingPlanHash,
                    challenge.RequesterSubject,
                    approver.Subject,
                    decidedAt),
                PlanId: challenge.PlanId,
                ChallengeId: challenge.Id,
                ActorSubject: approver.Subject,
                Outcome: ApprovalConventions.ChallengeOutcomeStatuses.Denied),
            cancellationToken).ConfigureAwait(false);

        return new ApprovalDecisionResult(true, McpGatewayMessages.Approval.PlanDenied(denied.PlanId));
    }

    public async Task<ApprovalDecisionResult> CancelChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var challenge = await approvalChallenges.GetChallengeAsync(challengeId, cancellationToken).ConfigureAwait(false);
        if (challenge is null)
        {
            return new ApprovalDecisionResult(
                false,
                McpGatewayMessages.Approval.ChallengeNotFound,
                ApprovalConventions.ResultReasonCodes.ChallengeNotFound);
        }

        var actor = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        if (actor is null)
        {
            return new ApprovalDecisionResult(
                false,
                McpGatewayMessages.Authorization.CancelAuthenticatedSubjectRequired,
                McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired);
        }

        if (!IsPending(challenge))
        {
            return new ApprovalDecisionResult(
                false,
                McpGatewayMessages.Approval.ChallengeAlreadyTerminal(challenge.Status),
                ApprovalConventions.ResultReasonCodes.ChallengeAlreadyTerminal);
        }

        if (challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            var expired = await ExpireChallengeAsync(challenge, cancellationToken).ConfigureAwait(false);

            return new ApprovalDecisionResult(
                false,
                McpGatewayMessages.Approval.ChallengeAlreadyTerminal(expired.Status),
                ApprovalConventions.ResultReasonCodes.ChallengeExpired);
        }

        if (!SameSubject(challenge.RequesterSubject, actor.Subject))
        {
            await WriteChallengeRejectedAuditAsync(
                challenge,
                actor.Subject,
                McpGatewayMessages.Authorization.CancelSubjectMismatch,
                cancellationToken).ConfigureAwait(false);

            return new ApprovalDecisionResult(
                false,
                McpGatewayMessages.Authorization.CancelSameSubjectRequired,
                McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired);
        }

        var decidedAt = DateTimeOffset.UtcNow;
        var canceled = await approvalChallenges.RecordChallengeOutcomeAsync(
            challenge,
            new ChallengeOutcome(
                ApprovalIds.NewChallengeOutcomeId(),
                challenge.Id,
                ApprovalConventions.ChallengeOutcomeStatuses.Canceled,
                actor.Subject,
                decidedAt,
                reason: null,
                grantId: null),
            new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.ApprovalChallengeCanceled,
                new ApprovalChallengeCanceledPayload(
                    challenge.Id,
                    challenge.PlanId,
                    challenge.PendingPlanHash,
                    challenge.RequesterSubject,
                    actor.Subject,
                    decidedAt),
                PlanId: challenge.PlanId,
                ChallengeId: challenge.Id,
                ActorSubject: actor.Subject,
                Outcome: ApprovalConventions.ChallengeOutcomeStatuses.Canceled),
            cancellationToken).ConfigureAwait(false);

        return new ApprovalDecisionResult(true, McpGatewayMessages.Approval.PlanCanceled(canceled.PlanId));
    }

    private async Task<ChallengeValidation> ValidatePendingChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var challenge = await RetrieveChallengeAsync(challengeId, cancellationToken).ConfigureAwait(false);
        if (challenge is null)
        {
            return ChallengeValidation.Invalid(
                McpGatewayMessages.Approval.ChallengeNotFound,
                ApprovalConventions.ResultReasonCodes.ChallengeNotFound);
        }

        var stateError = await ValidateChallengeStateAsync(challenge, cancellationToken).ConfigureAwait(false);
        if (stateError is not null)
        {
            return stateError;
        }

        var approver = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        var approverError = ValidateApprover(challenge, approver);
        if (approverError is not null)
        {
            return approverError;
        }

        var pending = await approvalPlans.GetPendingPlanAsync(challenge.PlanId, cancellationToken).ConfigureAwait(false);
        var pendingError = await ValidatePendingPlanStateAsync(challenge, pending, approver!.Subject, cancellationToken)
            .ConfigureAwait(false);
        if (pendingError is not null)
        {
            return pendingError;
        }

        var policyError = await ValidateApprovalPolicyAsync(challenge, pending.Envelope!, approver, cancellationToken)
            .ConfigureAwait(false);
        if (policyError is not null)
        {
            return policyError;
        }

        return await ValidateDecodedReviewAsync(challenge, pending, approver!.Subject, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ApprovalChallenge?> RetrieveChallengeAsync(string challengeId, CancellationToken ct)
    {
        return await approvalChallenges.GetChallengeAsync(challengeId, ct).ConfigureAwait(false);
    }

    private async Task<ChallengeValidation?> ValidateChallengeStateAsync(
        ApprovalChallenge challenge, CancellationToken ct)
    {
        if (!IsPending(challenge))
        {
            return ChallengeValidation.Invalid(
                McpGatewayMessages.Approval.ChallengeAlreadyTerminal(challenge.Status),
                ApprovalConventions.ResultReasonCodes.ChallengeAlreadyTerminal,
                challenge);
        }

        if (challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            var expired = await ExpireChallengeAsync(challenge, ct).ConfigureAwait(false);
            return ChallengeValidation.Invalid(
                McpGatewayMessages.Approval.ChallengeExpired,
                ApprovalConventions.ResultReasonCodes.ChallengeExpired,
                expired);
        }

        return null;
    }

    private static ChallengeValidation? ValidateApprover(
        ApprovalChallenge challenge, GatewayApprovalIdentity? approver)
    {
        if (approver is null)
        {
            return ChallengeValidation.Invalid(
                McpGatewayMessages.Authorization.AuthenticatedSubjectRequired,
                McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired,
                challenge);
        }

        return null;
    }

    private async Task<ChallengeValidation?> ValidateApprovalPolicyAsync(
        ApprovalChallenge challenge,
        PlanEnvelope envelope,
        GatewayApprovalIdentity approver,
        CancellationToken ct)
    {
        if (IsActorAuthorizedForChallengeOutcome(envelope.ApprovalPolicy, challenge.RequesterSubject, approver.Subject))
        {
            return null;
        }

        string reason = envelope.ApprovalPolicy.Type switch
        {
            ApprovalConventions.ApprovalPolicyTypes.OperatorApproval =>
                McpGatewayMessages.Authorization.ApproverNotInOperatorGroup,
            _ => McpGatewayMessages.Authorization.ApproverSubjectMismatch
        };
        string message = envelope.ApprovalPolicy.Type switch
        {
            ApprovalConventions.ApprovalPolicyTypes.OperatorApproval =>
                McpGatewayMessages.Authorization.RequiresOperatorGroup,
            _ => McpGatewayMessages.Authorization.RequiresSameSubject
        };
        string reasonCode = envelope.ApprovalPolicy.Type switch
        {
            ApprovalConventions.ApprovalPolicyTypes.OperatorApproval =>
                McpGatewayConventions.ApprovalReasonCodes.OperatorGroupRequired,
            _ => McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired
        };

        await WriteChallengeRejectedAuditAsync(challenge, approver.Subject, reason, ct).ConfigureAwait(false);

        return ChallengeValidation.Invalid(message, reasonCode, challenge);
    }

    private async Task<ApprovalDecisionResult?> ValidateChallengeDecisionPolicyAsync(
        ApprovalChallenge challenge,
        GatewayApprovalIdentity actor,
        CancellationToken ct)
    {
        var pending = await approvalPlans.GetPendingPlanAsync(challenge.PlanId, ct).ConfigureAwait(false);
        if (!pending.IsPending || pending.Envelope is null)
        {
            return new ApprovalDecisionResult(
                false,
                pending.Message,
                pending.ReasonCode ?? ApprovalConventions.ResultReasonCodes.PlanNotPending);
        }

        if (IsActorAuthorizedForChallengeOutcome(pending.Envelope.ApprovalPolicy, challenge.RequesterSubject, actor.Subject))
        {
            return null;
        }

        string reason = pending.Envelope.ApprovalPolicy.Type switch
        {
            ApprovalConventions.ApprovalPolicyTypes.OperatorApproval =>
                McpGatewayMessages.Authorization.ApproverNotInOperatorGroup,
            _ => McpGatewayMessages.Authorization.ApproverSubjectMismatch
        };
        string message = pending.Envelope.ApprovalPolicy.Type switch
        {
            ApprovalConventions.ApprovalPolicyTypes.OperatorApproval =>
                McpGatewayMessages.Authorization.RequiresOperatorGroup,
            _ => McpGatewayMessages.Authorization.DenySameSubjectRequired
        };
        string reasonCode = pending.Envelope.ApprovalPolicy.Type switch
        {
            ApprovalConventions.ApprovalPolicyTypes.OperatorApproval =>
                McpGatewayConventions.ApprovalReasonCodes.OperatorGroupRequired,
            _ => McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired
        };

        await WriteChallengeRejectedAuditAsync(challenge, actor.Subject, reason, ct).ConfigureAwait(false);

        return new ApprovalDecisionResult(false, message, reasonCode);
    }

    private async Task<ChallengeValidation?> ValidatePendingPlanStateAsync(
        ApprovalChallenge challenge, PendingPlanResult pending, string? approverSubject, CancellationToken ct)
    {
        if (!pending.IsPending || pending.Envelope is null || pending.Hash is null)
        {
            await WriteChallengeRejectedAuditAsync(
                challenge, approverSubject, pending.Message, ct).ConfigureAwait(false);

            return ChallengeValidation.Invalid(
                pending.Message,
                pending.ReasonCode ?? ApprovalConventions.ResultReasonCodes.PlanNotPending,
                challenge);
        }

        if (!SameDigest(challenge.IntentDigest, pending.Envelope.IntentDigest) ||
            !SameDigest(challenge.ReviewDigest, pending.Envelope.ReviewDigest))
        {
            await WriteChallengeRejectedAuditAsync(challenge, approverSubject, McpGatewayMessages.Approval.DigestBindingChanged, ct).ConfigureAwait(false);

            return ChallengeValidation.Invalid(
                McpGatewayMessages.Approval.DigestBindingChanged, ApprovalConventions.ResultReasonCodes.DigestChanged, challenge);
        }

        if (!FixedTimeStringComparer.Equals(challenge.PendingPlanHash, pending.Hash))
        {
            await WriteChallengeRejectedAuditAsync(challenge, approverSubject, McpGatewayMessages.Approval.PendingPlanChanged, ct).ConfigureAwait(false);

            return ChallengeValidation.Invalid(
                McpGatewayMessages.Approval.PendingPlanChanged, ApprovalConventions.ResultReasonCodes.PendingPlanChanged, challenge);
        }

        return null;
    }

    private async Task<ChallengeValidation> ValidateDecodedReviewAsync(
        ApprovalChallenge challenge, PendingPlanResult pending, string? approverSubject, CancellationToken ct)
    {
        var decoded = planReviewAdapter.TryDecodeForReview(pending.Envelope!, out var decodeError);
        if (decoded is null)
        {
            var errorMessage = decodeError ?? McpGatewayMessages.Approval.AdapterDecodeFailed;
            await WriteChallengeRejectedAuditAsync(challenge, approverSubject, errorMessage, ct).ConfigureAwait(false);

            return ChallengeValidation.Invalid(
                errorMessage, McpGatewayConventions.ApprovalReasonCodes.AdapterDecodeFailed, challenge);
        }

        if (!SameSubject(challenge.RequesterSubject, decoded.Envelope.Requester.Subject))
        {
            const string message = McpGatewayMessages.Approval.RequesterChanged;
            await WriteChallengeRejectedAuditAsync(challenge, approverSubject, message, ct).ConfigureAwait(false);

            return ChallengeValidation.Invalid(
                message, ApprovalConventions.ResultReasonCodes.RequesterChanged, challenge, decoded);
        }

        if (!decoded.HasReviewEvidence)
        {
            await WriteChallengeRejectedAuditAsync(challenge, approverSubject, McpGatewayMessages.Approval.MissingEvidence(challenge.PlanId), ct).ConfigureAwait(false);

            return ChallengeValidation.Invalid(
                McpGatewayMessages.Approval.MissingEvidence(challenge.PlanId), ApprovalConventions.ResultReasonCodes.MissingReviewEvidence, challenge, decoded);
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
                ApprovalIds.NewChallengeOutcomeId(),
                challenge.Id,
                ApprovalConventions.ChallengeOutcomeStatuses.Rejected,
                approverSubject,
                decidedAt,
                reason,
                grantId: null)
        };
        await approvalChallenges.RecordChallengeOutcomeAsync(
            challenge,
            rejected.Outcome,
            new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.ApprovalChallengeRejected,
                new ApprovalChallengeRejectedPayload(
                    challenge.Id,
                    challenge.PlanId,
                    challenge.PendingPlanHash,
                    challenge.RequesterSubject,
                    approverSubject,
                    reason),
                PlanId: challenge.PlanId,
                ChallengeId: challenge.Id,
                ActorSubject: approverSubject,
                Outcome: ApprovalConventions.ChallengeOutcomeStatuses.Rejected),
            cancellationToken).ConfigureAwait(false);
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
                ApprovalIds.NewChallengeOutcomeId(),
                challenge.Id,
                ApprovalConventions.ChallengeOutcomeStatuses.Expired,
                actorSubject: null,
                decidedAt,
                McpGatewayMessages.Approval.ChallengeTtlExpired,
                grantId: null)
        };
        return await approvalChallenges.RecordChallengeOutcomeAsync(
            challenge,
            expired.Outcome,
            new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.ApprovalChallengeExpired,
                new ApprovalChallengeExpiredPayload(
                    expired.Id,
                    expired.PlanId,
                    expired.PendingPlanHash,
                    expired.RequesterSubject,
                    expired.ExpiresAtUtc),
                PlanId: expired.PlanId,
                ChallengeId: expired.Id),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteApplyDeniedAuditAsync(
        string planId,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditOutbox.AppendAsync(
                new ApprovalAuditEntry(
                    ApprovalConventions.AuditEvents.ApplyDenied,
                    new ApplyDeniedPayload(planId, message),
                    PlanId: planId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
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

    private bool IsActorAuthorizedForChallengeOutcome(
        ApprovalPolicy policy,
        string requesterSubject,
        string? actorSubject)
    {
        return policy.Type switch
        {
            ApprovalConventions.ApprovalPolicyTypes.SameSubject =>
                actorSubject is not null && SameSubject(requesterSubject, actorSubject),
            ApprovalConventions.ApprovalPolicyTypes.OperatorApproval =>
                TryGetOperatorGroup(policy, out var operatorGroup) &&
                GetActorGroups().Contains(operatorGroup),
            _ => false
        };
    }

    private static bool TryGetOperatorGroup(ApprovalPolicy policy, out string operatorGroup)
    {
        if (policy.Parameters is not null &&
            policy.Parameters.TryGetValue(ApprovalConventions.ApprovalPolicyParameters.OperatorGroup, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            operatorGroup = value;
            return true;
        }

        operatorGroup = string.Empty;
        return false;
    }

    private IReadOnlySet<string> GetActorGroups()
    {
        var groups = new HashSet<string>(StringComparer.Ordinal);
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return groups;
        }

        foreach (var claim in user.FindAll(GatewayAuthConventions.Claims.Groups))
        {
            foreach (string group in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                groups.Add(group);
                groups.Add(group.TrimStart('/'));
            }
        }

        logger.LogInformation("Actor groups resolved from token: [{Groups}]", string.Join(", ", groups));

        return groups;
    }

    private static bool SameDigest(ApprovalDigest? left, ApprovalDigest right) =>
        left is not null && left == right;

    private sealed record class ChallengeValidation(
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

    private sealed record class ResultFailure(string Message, string ReasonCode);
}
