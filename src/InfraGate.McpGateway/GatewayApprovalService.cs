using InfraGate.Approvals;
using InfraGate.Approvals.AuditPayloads;
using InfraGate.KubernetesAdapter;

namespace InfraGate.McpGateway;

public sealed class GatewayApprovalService
{
    private readonly ApprovalStore approvalStore;
    private readonly ApprovalChallengeStore challengeStore;
    private readonly McpGatewayOptions options;
    private readonly IHttpContextAccessor httpContextAccessor;

    public GatewayApprovalService(
        ApprovalStore approvalStore,
        ApprovalChallengeStore challengeStore,
        McpGatewayOptions options,
        IHttpContextAccessor httpContextAccessor)
    {
        this.approvalStore = approvalStore;
        this.challengeStore = challengeStore;
        this.options = options;
        this.httpContextAccessor = httpContextAccessor;
    }

    public async Task<ApprovalGateResult> EnsureApprovedOrCreateChallengeAsync(
        string planId,
        CancellationToken cancellationToken)
    {
        var requester = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        if (requester is null)
        {
            return ApprovalGateResult.RequiresApproval("Refused: apply approval requires an authenticated OAuth subject.");
        }

        var granted = await approvalStore.GetGrantedPlanAsync(planId, cancellationToken);
        if (granted.IsGranted && granted.Envelope is not null && granted.Grant is not null)
        {
            var decoded = KubernetesApprovalAdapter.Decode(granted.Envelope);
            if (!decoded.Succeeded || decoded.Plan is null)
            {
                return ApprovalGateResult.RequiresApproval($"Refused: {decoded.Message}");
            }

            if (!SameSubject(decoded.Plan.Requester.Subject, requester.Subject))
            {
                return ApprovalGateResult.RequiresApproval("Refused: apply approval requires the same authenticated subject that requested the plan.");
            }

            var approvedRefusal = GetPlanReadinessRefusal(decoded.Plan, planId);
            if (approvedRefusal is not null)
            {
                return ApprovalGateResult.RequiresApproval($"Refused: {approvedRefusal}");
            }

            return ApprovalGateResult.Approved();
        }

        if (!granted.IsGranted && granted.GrantExists)
        {
            return ApprovalGateResult.RequiresApproval($"Refused: {granted.Message}");
        }

        var pending = await approvalStore.GetPendingPlanAsync(planId, cancellationToken);
        if (!pending.IsPending || pending.Envelope is null || pending.Hash is null)
        {
            return ApprovalGateResult.RequiresApproval($"Refused: {pending.Message}");
        }

        var pendingPlan = KubernetesApprovalAdapter.Decode(pending.Envelope);
        if (!pendingPlan.Succeeded || pendingPlan.Plan is null)
        {
            return ApprovalGateResult.RequiresApproval($"Refused: {pendingPlan.Message}");
        }

        if (!SameSubject(pendingPlan.Plan.Requester.Subject, requester.Subject))
        {
            return ApprovalGateResult.RequiresApproval("Refused: apply approval requires the same authenticated subject that requested the plan.");
        }

        var pendingRefusal = GetPlanReadinessRefusal(pendingPlan.Plan, planId);
        if (pendingRefusal is not null)
        {
            return ApprovalGateResult.RequiresApproval($"Refused: {pendingRefusal}");
        }

        var challenge = await challengeStore.CreateAsync(
            planId,
            pending.Hash,
            requester.Subject,
            requester.AuthenticationType,
            options.ApprovalChallengeTtl,
            pending.Envelope.IntentDigest,
            pending.Envelope.ReviewDigest,
            cancellationToken);
        await approvalStore.WriteAuditAsync(
            ApprovalConventions.AuditEvents.ApprovalChallengeCreated,
            new ApprovalChallengeCreatedPayload(
                challenge.Id,
                challenge.PlanId,
                challenge.PlanHash,
                challenge.RequesterSubject,
                challenge.RequesterAuthenticationType,
                challenge.ExpiresAtUtc),
            cancellationToken);

        return ApprovalGateResult.RequiresApproval(FormatApprovalRequiredMessage(pendingPlan.Plan, challenge));
    }

    private static string? GetPlanReadinessRefusal(KubernetesPlan? plan, string planId)
    {
        if (plan?.DryRun is null)
        {
            return MissingDryRunMessage(planId);
        }

        if (plan.Diffs.Length == 0)
        {
            return MissingDiffMessage(planId);
        }

        return null;
    }

    public async Task<ApprovalPageModel> GetApprovalPageAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var validation = await ValidatePendingChallengeAsync(challengeId, cancellationToken);

        return validation.Error is not null
            ? new ApprovalPageModel(false, validation.Error, validation.Challenge, validation.Plan)
            : new ApprovalPageModel(true, null, validation.Challenge, validation.Plan);
    }

    public async Task<ApprovalDecisionResult> ApproveChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var validation = await ValidatePendingChallengeAsync(challengeId, cancellationToken);
        if (validation.Error is not null ||
            validation.Challenge is null ||
            validation.Plan is null)
        {
            return new ApprovalDecisionResult(false, validation.Error ?? "Approval challenge is invalid.");
        }

        var approver = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User)!;
        var decidedAt = DateTimeOffset.UtcNow;
        var grant = await approvalStore.CreateGrantAsync(
            validation.Plan.Envelope,
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
        await approvalStore.WriteAuditAsync(
            ApprovalConventions.AuditEvents.ApprovalChallengeApproved,
            new ApprovalChallengeApprovedPayload(
                updated.Id,
                updated.PlanId,
                updated.PlanHash,
                updated.RequesterSubject,
                updated.ApproverSubject,
                decidedAt),
            cancellationToken);

        return new ApprovalDecisionResult(
            true,
            $"Plan '{updated.PlanId}' was approved with grant '{grant.Id}'. Return to your MCP client and call apply_approved_plan again.");
    }

    public async Task<ApprovalDecisionResult> DenyChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var challenge = await challengeStore.GetAsync(challengeId, cancellationToken);
        if (challenge is null)
        {
            return new ApprovalDecisionResult(false, "Approval challenge was not found.");
        }

        var approver = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        if (approver is null)
        {
            return new ApprovalDecisionResult(false, "Approval requires an authenticated OAuth subject.");
        }

        if (!IsPending(challenge))
        {
            return new ApprovalDecisionResult(false, $"Approval challenge is already {challenge.Status}.");
        }

        if (!SameSubject(challenge.RequesterSubject, approver.Subject))
        {
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                "Approver subject did not match requester subject.",
                cancellationToken);

            return new ApprovalDecisionResult(false, "Approval must be denied by the same authenticated subject that requested it.");
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
                denied.PlanHash,
                denied.RequesterSubject,
                denied.ApproverSubject,
                decidedAt),
            cancellationToken);

        return new ApprovalDecisionResult(true, $"Plan '{denied.PlanId}' was denied.");
    }

    private async Task<ChallengeValidation> ValidatePendingChallengeAsync(
        string challengeId,
        CancellationToken cancellationToken)
    {
        var challenge = await challengeStore.GetAsync(challengeId, cancellationToken);
        if (challenge is null)
        {
            return ChallengeValidation.Invalid("Approval challenge was not found.");
        }

        if (!IsPending(challenge))
        {
            return ChallengeValidation.Invalid($"Approval challenge is already {challenge.Status}.", challenge);
        }

        if (challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow)
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
                    expired.PlanHash,
                    expired.RequesterSubject,
                    expired.ExpiresAtUtc),
                cancellationToken);

            return ChallengeValidation.Invalid("Approval challenge expired. Ask the MCP client to request a new approval URL.", expired);
        }

        var approver = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        if (approver is null)
        {
            return ChallengeValidation.Invalid("Approval requires an authenticated OAuth subject.", challenge);
        }

        if (!SameSubject(challenge.RequesterSubject, approver.Subject))
        {
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                "Approver subject did not match requester subject.",
                cancellationToken);

            return ChallengeValidation.Invalid("Approval requires the same authenticated subject that requested the plan.", challenge);
        }

        var pending = await approvalStore.GetPendingPlanAsync(challenge.PlanId, cancellationToken);
        if (!pending.IsPending || pending.Envelope is null || pending.Hash is null)
        {
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                pending.Message,
                cancellationToken);

            return ChallengeValidation.Invalid(pending.Message, challenge);
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

            return ChallengeValidation.Invalid(message, challenge);
        }

        var decoded = KubernetesApprovalAdapter.Decode(pending.Envelope);
        if (!decoded.Succeeded || decoded.Plan is null)
        {
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                decoded.Message,
                cancellationToken);

            return ChallengeValidation.Invalid(decoded.Message, challenge);
        }

        if (!SameSubject(challenge.RequesterSubject, decoded.Plan.Requester.Subject))
        {
            const string message = "The pending plan requester changed after this approval URL was created. Ask the MCP client to request a new approval URL.";
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                message,
                cancellationToken);

            return ChallengeValidation.Invalid(message, challenge, decoded.Plan);
        }

        if (!FixedTimeStringComparer.Equals(challenge.PlanHash, pending.Hash))
        {
            const string message = "The pending plan changed after this approval URL was created. Ask the MCP client to request a new approval URL.";
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                message,
                cancellationToken);

            return ChallengeValidation.Invalid(message, challenge, decoded.Plan);
        }

        if (decoded.Plan.DryRun is null)
        {
            var message = MissingDryRunMessage(challenge.PlanId);
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                message,
                cancellationToken);

            return ChallengeValidation.Invalid(message, challenge, decoded.Plan);
        }

        if (decoded.Plan.Diffs.Length == 0)
        {
            var message = MissingDiffMessage(challenge.PlanId);
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                message,
                cancellationToken);

            return ChallengeValidation.Invalid(message, challenge, decoded.Plan);
        }

        return ChallengeValidation.Valid(challenge, decoded.Plan);
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
                challenge.PlanHash,
                challenge.RequesterSubject,
                approverSubject,
                reason),
            cancellationToken);
    }

    private string FormatApprovalRequiredMessage(KubernetesPlan plan, ApprovalChallenge challenge)
    {
        var objects = string.Join(
            Environment.NewLine,
            plan.Objects.Select(obj => $"  - {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}"));

        return $"""
               Approval required.
               PlanId: {plan.Id}
               Operation: {plan.Operation}
               Namespace: {plan.Namespace}
               Objects:
               {objects}
               Intent Digest: {plan.Envelope.IntentDigest.Value}
               Review Digest: {plan.Envelope.ReviewDigest.Value}
               Approval URL: {CreateApprovalUrl(challenge.Id)}
               Expires at UTC: {challenge.ExpiresAtUtc:O}

               Open the approval URL in a browser, sign in with the same identity, review the Gateway-rendered plan, then call apply_approved_plan again.
               """;
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

    private static string MissingDryRunMessage(string planId) =>
        $"Plan '{planId}' is missing recorded server-side dry-run data. Ask the MCP client to re-request the plan.";

    private static string MissingDiffMessage(string planId) =>
        $"Plan '{planId}' is missing recorded diff data. Ask the MCP client to re-request the plan.";

    private sealed record ChallengeValidation(
        string? Error,
        ApprovalChallenge? Challenge,
        KubernetesPlan? Plan)
    {
        public static ChallengeValidation Valid(ApprovalChallenge challenge, KubernetesPlan plan) =>
            new(null, challenge, plan);

        public static ChallengeValidation Invalid(
            string error,
            ApprovalChallenge? challenge = null,
            KubernetesPlan? plan = null) =>
            new(error, challenge, plan);
    }
}
