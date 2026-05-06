using InfraGate.Approvals;

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

        var approved = await approvalStore.GetApprovedPlanAsync(planId, cancellationToken);
        if (approved.IsApproved)
        {
            var approvedChallenge = await challengeStore.FindApprovedAsync(
                planId,
                approved.Hash!,
                requester.Subject,
                cancellationToken);
            if (approvedChallenge is not null)
            {
                return ApprovalGateResult.Approved();
            }
        }

        var pending = await approvalStore.GetPendingPlanAsync(planId, cancellationToken);
        if (!pending.IsPending || pending.Plan is null || pending.Hash is null)
        {
            return ApprovalGateResult.RequiresApproval($"Refused: {pending.Message}");
        }

        var challenge = await challengeStore.CreateAsync(
            planId,
            pending.Hash,
            requester.Subject,
            requester.AuthenticationType,
            options.ApprovalChallengeTtl,
            cancellationToken);
        await approvalStore.WriteAuditAsync(ApprovalConventions.AuditEvents.ApprovalChallengeCreated, new
        {
            challenge.Id,
            challenge.PlanId,
            challenge.PlanHash,
            challenge.RequesterSubject,
            challenge.RequesterAuthenticationType,
            challenge.ExpiresAtUtc
        }, cancellationToken);

        return ApprovalGateResult.RequiresApproval(FormatApprovalRequiredMessage(pending.Plan, pending.Hash, challenge));
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
        var approved = await approvalStore.ApprovePendingPlanAsync(
            validation.Challenge.PlanId,
            validation.Challenge.PlanHash,
            ApprovalConventions.ApprovalSources.GatewayOutOfBand,
            approver.Subject,
            validation.Challenge.Id,
            cancellationToken);
        if (!approved.IsApproved)
        {
            await WriteChallengeRejectedAuditAsync(
                validation.Challenge,
                approver.Subject,
                approved.Message,
                cancellationToken);

            return new ApprovalDecisionResult(false, approved.Message);
        }

        var decidedAt = DateTimeOffset.UtcNow;
        var updated = validation.Challenge with
        {
            Status = ApprovalConventions.ChallengeStatuses.Approved,
            ApproverSubject = approver.Subject,
            DecidedAtUtc = decidedAt
        };
        await challengeStore.SaveAsync(updated, cancellationToken);
        await approvalStore.WriteAuditAsync(ApprovalConventions.AuditEvents.ApprovalChallengeApproved, new
        {
            updated.Id,
            updated.PlanId,
            updated.PlanHash,
            updated.RequesterSubject,
            updated.ApproverSubject,
            decidedAt
        }, cancellationToken);

        return new ApprovalDecisionResult(
            true,
            $"Plan '{updated.PlanId}' was approved. Return to your MCP client and call apply_approved_plan again.");
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
            DecidedAtUtc = decidedAt
        };
        await challengeStore.SaveAsync(denied, cancellationToken);
        await approvalStore.WriteAuditAsync(ApprovalConventions.AuditEvents.ApprovalChallengeDenied, new
        {
            denied.Id,
            denied.PlanId,
            denied.PlanHash,
            denied.RequesterSubject,
            denied.ApproverSubject,
            decidedAt
        }, cancellationToken);

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
            var expired = challenge with { Status = ApprovalConventions.ChallengeStatuses.Expired };
            await challengeStore.SaveAsync(expired, cancellationToken);
            await approvalStore.WriteAuditAsync(ApprovalConventions.AuditEvents.ApprovalChallengeExpired, new
            {
                expired.Id,
                expired.PlanId,
                expired.PlanHash,
                expired.RequesterSubject,
                expired.ExpiresAtUtc
            }, cancellationToken);

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
        if (!pending.IsPending || pending.Plan is null || pending.Hash is null)
        {
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                pending.Message,
                cancellationToken);

            return ChallengeValidation.Invalid(pending.Message, challenge);
        }

        if (!FixedTimeStringComparer.Equals(challenge.PlanHash, pending.Hash))
        {
            const string message = "The pending plan changed after this approval URL was created. Ask the MCP client to request a new approval URL.";
            await WriteChallengeRejectedAuditAsync(
                challenge,
                approver.Subject,
                message,
                cancellationToken);

            return ChallengeValidation.Invalid(message, challenge, pending.Plan);
        }

        return ChallengeValidation.Valid(challenge, pending.Plan);
    }

    private Task WriteChallengeRejectedAuditAsync(
        ApprovalChallenge challenge,
        string? approverSubject,
        string reason,
        CancellationToken cancellationToken) =>
        approvalStore.WriteAuditAsync(ApprovalConventions.AuditEvents.ApprovalChallengeRejected, new
        {
            challenge.Id,
            challenge.PlanId,
            challenge.PlanHash,
            challenge.RequesterSubject,
            approverSubject,
            reason
        }, cancellationToken);

    private string FormatApprovalRequiredMessage(K8sPlan plan, string hash, ApprovalChallenge challenge)
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
               Plan hash: {hash}
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

    private sealed record ChallengeValidation(
        string? Error,
        ApprovalChallenge? Challenge,
        K8sPlan? Plan)
    {
        public static ChallengeValidation Valid(ApprovalChallenge challenge, K8sPlan plan) =>
            new(null, challenge, plan);

        public static ChallengeValidation Invalid(
            string error,
            ApprovalChallenge? challenge = null,
            K8sPlan? plan = null) =>
            new(error, challenge, plan);
    }
}
