using System.Security.Claims;
using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class GatewayApprovalServiceTests
{
    private const string Subject = "requester";
    private const string NamespaceName = "mcp-nginx-demo";

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_UnapprovedPlan_ReturnsApprovalUrl()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.ApprovalRequired, result.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.ApprovalRequired, result.ReasonCode);
        Assert.StartsWith("http://gateway.test/approvals/", result.ApprovalUrl, StringComparison.Ordinal);
        Assert.NotNull(result.ChallengeId);
        Assert.NotNull(result.ExpiresAtUtc);
        Assert.False(context.Workflow.IsGranted(plan.Id));
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_MatchingPendingChallenge_ReturnsExistingApprovalUrl()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);

        var first = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);
        var second = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(first.IsApproved);
        Assert.False(second.IsApproved);
        Assert.Equal(ApprovalGateStatus.ApprovalRequired, second.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.ApprovalRequired, second.ReasonCode);
        Assert.Equal(first.ApprovalUrl, second.ApprovalUrl);
        Assert.Equal(1, context.Workflow.ChallengeCount);
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_ExpiredPendingChallenge_ReturnsNewApprovalUrl()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var first = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);
        string firstChallengeId = first.ChallengeId!;
        context.Workflow.TamperChallengeExpiry(firstChallengeId, DateTimeOffset.UtcNow.AddMinutes(-1));

        var second = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(second.IsApproved);
        Assert.Equal(ApprovalGateStatus.ApprovalRequired, second.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.ApprovalRequired, second.ReasonCode);
        Assert.NotEqual(first.ApprovalUrl, second.ApprovalUrl);
        Assert.Equal(2, context.Workflow.ChallengeCount);
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_NoAuthenticatedUser_ReturnsRefusal()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        SetUnauthenticatedUser(context.HttpContextAccessor);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired, result.ReasonCode);
        Assert.Contains("authenticated OAuth subject", result.Message);
        Assert.Equal(0, context.Workflow.ChallengeCount);
    }

    [Fact]
    public async Task ApproveChallengeAsync_SameSubject_WritesGrantOutcomeAndRejectsReuse()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var approved = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);
        var reused = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);
        var challenge = context.Workflow.GetChallenge(challengeId);

        Assert.True(approved.Succeeded);
        Assert.True(context.Workflow.IsGranted(plan.Id));
        Assert.False(reused.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ChallengeAlreadyTerminal, reused.ReasonCode);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Approved, challenge?.Status);
        Assert.Equal(ApprovalConventions.ChallengeOutcomeStatuses.Approved, challenge?.Outcome?.Status);
    }

    [Fact]
    public async Task ApproveChallengeAsync_NoAuthenticatedUser_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        SetUnauthenticatedUser(context.HttpContextAccessor);

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired, result.ReasonCode);
        Assert.False(context.Workflow.IsGranted(plan.Id));
    }

    [Fact]
    public async Task ApproveChallengeAsync_NonExistentChallenge_Rejects()
    {
        var context = CreateContext();

        var result = await context.Service.ApproveChallengeAsync("abc123", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ChallengeNotFound, result.ReasonCode);
    }

    [Fact]
    public async Task ApproveChallengeAsync_PendingPlanMissing_Rejects()
    {
        var context = CreateContext();
        var challengeId = await CreateStoredChallengeAsync(context, "missing-plan", "missing-hash");

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanNotPending, result.ReasonCode);
    }

    [Fact]
    public async Task ApproveChallengeAsync_PendingPlanWithoutDryRun_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow, includeDryRun: false);
        var pending = await context.Workflow.GetPendingPlanAsync(plan.Id, CancellationToken.None);
        var hash = pending.Hash!;
        var challengeId = await CreateStoredChallengeAsync(context, plan.Id, hash);

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.MissingReviewEvidence, result.ReasonCode);
        Assert.False(context.Workflow.IsGranted(plan.Id));
    }

    [Fact]
    public async Task ApproveChallengeAsync_PendingPlanWithoutDiff_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(
            context.Workflow,
            includeDiff: false,
            operation: KubernetesAdapterConventions.PlanOperations.Apply);
        var pending = await context.Workflow.GetPendingPlanAsync(plan.Id, CancellationToken.None);
        var hash = pending.Hash!;
        var challengeId = await CreateStoredChallengeAsync(context, plan.Id, hash);

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.MissingReviewEvidence, result.ReasonCode);
        Assert.False(context.Workflow.IsGranted(plan.Id));
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_GrantReviewDigestMismatch_WritesApplyDeniedAudit()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        var approved = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);
        context.Workflow.TamperEvidenceArtifactDigest(plan.Id);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.True(approved.Succeeded);
        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.DigestChanged, result.ReasonCode);
        string audit = context.Workflow.GetAuditEventsJson();
        Assert.Contains($@"""eventName"": ""{ApprovalConventions.AuditEvents.ApplyDenied}""", audit);
        Assert.Contains($"\"planId\": \"{plan.Id}\"", audit);
        Assert.Contains("review digest no longer matches", audit);
    }

    [Fact]
    public async Task ApproveChallengeAsync_DifferentSubject_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        SetUser(context.HttpContextAccessor, "other-user");

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired, result.ReasonCode);
        Assert.False(context.Workflow.IsGranted(plan.Id));
    }

    [Fact]
    public async Task ApproveChallengeAsync_PendingPlanHashDrift_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        context.Workflow.TamperPlanHash(plan.Id);

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PendingPlanChanged, result.ReasonCode);
        Assert.False(context.Workflow.IsGranted(plan.Id));
    }

    [Fact]
    public async Task ApproveChallengeAsync_ExpiredChallenge_RejectsAndSetsExpiredStatus()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        context.Workflow.TamperChallengeExpiry(challengeId, DateTimeOffset.UtcNow.AddSeconds(-1));

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);
        var updated = context.Workflow.GetChallenge(challengeId);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ChallengeExpired, result.ReasonCode);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Expired, updated?.Status);
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_NonExistentPlanId_ReturnsRefusal()
    {
        var context = CreateContext();

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(
            "00000000-0000-0000-0000-000000000000", CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanNotPending, result.ReasonCode);
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_PlanWithoutDryRun_ReturnsRefusal()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow, includeDryRun: false);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.MissingReviewEvidence, result.ReasonCode);
        Assert.Equal(0, context.Workflow.ChallengeCount);
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_PlanWithoutDiff_ReturnsRefusal()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(
            context.Workflow,
            includeDiff: false,
            operation: KubernetesAdapterConventions.PlanOperations.Apply);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.MissingReviewEvidence, result.ReasonCode);
        Assert.Equal(0, context.Workflow.ChallengeCount);
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_PlanWindowNotStarted_ReturnsRefusal()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow, createdAtUtc: DateTimeOffset.UtcNow.AddHours(1));

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.PlanNotStarted, result.ReasonCode);
        Assert.Equal(0, context.Workflow.ChallengeCount);
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_PlanWindowExpired_ReturnsRefusal()
    {
        var context = CreateContext();
        // ValidFromUtc = now-2h, ValidUntilUtc = now-1h (window closed 1 hour ago)
        var plan = await CreatePendingPlanAsync(context.Workflow, createdAtUtc: DateTimeOffset.UtcNow.AddHours(-2));

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.PlanExpired, result.ReasonCode);
        Assert.Equal(0, context.Workflow.ChallengeCount);
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_PlanWindowNearExpiry_CapsChallengeTtl()
    {
        var context = CreateContext();
        // ValidUntilUtc = now+5min; configured TTL = 15min → effective TTL should be ~5min
        var plan = await CreatePendingPlanAsync(context.Workflow, createdAtUtc: DateTimeOffset.UtcNow.AddMinutes(-55));

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);
        var challenge = context.Workflow.GetChallenge(result.ChallengeId!);

        Assert.False(result.IsApproved);
        Assert.NotNull(challenge);
        Assert.True(challenge.ExpiresAtUtc < DateTimeOffset.UtcNow.AddMinutes(10),
            $"Expected ExpiresAtUtc < now+10min but was {challenge.ExpiresAtUtc}");
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_PlanWindowAmple_UsesConfiguredTtl()
    {
        var context = CreateContext();
        // ValidUntilUtc = now+1h; configured TTL = 15min → effective TTL should be 15min
        var plan = await CreatePendingPlanAsync(context.Workflow);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);
        var challenge = context.Workflow.GetChallenge(result.ChallengeId!);

        Assert.False(result.IsApproved);
        Assert.NotNull(challenge);
        Assert.True(challenge.ExpiresAtUtc >= DateTimeOffset.UtcNow.AddMinutes(14),
            $"Expected ExpiresAtUtc >= now+14min but was {challenge.ExpiresAtUtc}");
    }

    [Fact]
    public async Task GetApprovalPageAsync_ValidPlan_IncludesDiffModel()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var page = await context.Service.GetApprovalPageAsync(challengeId, CancellationToken.None);

        Assert.True(page.CanDecide);
        Assert.NotNull(page.PlanReview);
        Assert.True(page.PlanReview.HasReviewEvidence);
    }

    [Fact]
    public async Task ApproveChallengeAsync_PendingPlanHashDriftAfterDiffChange_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        context.Workflow.TamperPlanHash(plan.Id);

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PendingPlanChanged, result.ReasonCode);
        Assert.False(context.Workflow.IsGranted(plan.Id));
    }

    [Fact]
    public async Task DenyChallengeAsync_MarksDeniedWithoutApproving()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var result = await context.Service.DenyChallengeAsync(challengeId, CancellationToken.None);
        var challenge = context.Workflow.GetChallenge(challengeId);

        Assert.True(result.Succeeded);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Denied, challenge?.Status);
        Assert.False(context.Workflow.IsGranted(plan.Id));
    }

    [Fact]
    public async Task DenyChallengeAsync_NonExistentChallenge_Rejects()
    {
        var context = CreateContext();

        var result = await context.Service.DenyChallengeAsync("abc123", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ChallengeNotFound, result.ReasonCode);
    }

    [Fact]
    public async Task DenyChallengeAsync_NoAuthenticatedUser_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        SetUnauthenticatedUser(context.HttpContextAccessor);

        var result = await context.Service.DenyChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired, result.ReasonCode);
    }

    [Fact]
    public async Task DenyChallengeAsync_AlreadyDeniedChallenge_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var denied = await context.Service.DenyChallengeAsync(challengeId, CancellationToken.None);
        var reused = await context.Service.DenyChallengeAsync(challengeId, CancellationToken.None);

        Assert.True(denied.Succeeded);
        Assert.False(reused.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ChallengeAlreadyTerminal, reused.ReasonCode);
    }

    [Fact]
    public async Task DenyChallengeAsync_DifferentSubject_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        SetUser(context.HttpContextAccessor, "other-user");

        var result = await context.Service.DenyChallengeAsync(challengeId, CancellationToken.None);
        var challenge = context.Workflow.GetChallenge(challengeId);

        Assert.False(result.Succeeded);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired, result.ReasonCode);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Rejected, challenge?.Status);
        Assert.Equal(ApprovalConventions.ChallengeOutcomeStatuses.Rejected, challenge?.Outcome?.Status);
    }

    [Fact]
    public async Task CancelChallengeAsync_SameSubject_CancelsWithoutGrantAndWritesAudit()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var result = await context.Service.CancelChallengeAsync(challengeId, CancellationToken.None);
        var challenge = context.Workflow.GetChallenge(challengeId);
        string audit = context.Workflow.GetAuditEventsJson();

        Assert.True(result.Succeeded);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Canceled, challenge?.Status);
        Assert.Equal(ApprovalConventions.ChallengeOutcomeStatuses.Canceled, challenge?.Outcome?.Status);
        Assert.Equal(Subject, challenge?.Outcome?.ActorSubject);
        Assert.Null(challenge?.Outcome?.GrantId);
        Assert.False(context.Workflow.IsGranted(plan.Id));
        Assert.Contains($@"""eventName"": ""{ApprovalConventions.AuditEvents.ApprovalChallengeCanceled}""", audit);
    }

    [Fact]
    public async Task CancelChallengeAsync_AlreadyCanceled_RejectsReuse()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var canceled = await context.Service.CancelChallengeAsync(challengeId, CancellationToken.None);
        var reused = await context.Service.CancelChallengeAsync(challengeId, CancellationToken.None);

        Assert.True(canceled.Succeeded);
        Assert.False(reused.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ChallengeAlreadyTerminal, reused.ReasonCode);
    }

    [Fact]
    public async Task CancelChallengeAsync_ExpiredChallenge_AutoExpires()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        context.Workflow.TamperChallengeExpiry(challengeId, DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await context.Service.CancelChallengeAsync(challengeId, CancellationToken.None);
        var updated = context.Workflow.GetChallenge(challengeId);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ChallengeExpired, result.ReasonCode);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Expired, updated?.Status);
    }

    [Fact]
    public async Task CancelChallengeAsync_DifferentSubject_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        SetUser(context.HttpContextAccessor, "other-user");

        var result = await context.Service.CancelChallengeAsync(challengeId, CancellationToken.None);
        var challenge = context.Workflow.GetChallenge(challengeId);

        Assert.False(result.Succeeded);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired, result.ReasonCode);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Rejected, challenge?.Status);
        Assert.Equal(ApprovalConventions.ChallengeOutcomeStatuses.Rejected, challenge?.Outcome?.Status);
    }

    [Fact]
    public async Task ApproveChallengeAsync_CanceledChallenge_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Workflow);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var canceled = await context.Service.CancelChallengeAsync(challengeId, CancellationToken.None);
        var approved = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.True(canceled.Succeeded);
        Assert.False(approved.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ChallengeAlreadyTerminal, approved.ReasonCode);
        Assert.False(context.Workflow.IsGranted(plan.Id));
    }

    private static async Task<KubernetesPlan> CreatePendingPlanAsync(
        TestApprovalWorkflow workflow,
        bool includeDryRun = true,
        bool includeDiff = true,
        string operation = KubernetesAdapterConventions.PlanOperations.Scale,
        DateTimeOffset? createdAtUtc = null)
    {
        var objects = new[] { new KubernetesObjectRef("apps/v1", "Deployment", NamespaceName, "demo") };
        var payload = new KubernetesPlanPayload(
            NamespaceName,
            "Scale deployment.",
            new Dictionary<string, string>
            {
                [KubernetesAdapterConventions.PlanParameters.Name] = "demo",
                [KubernetesAdapterConventions.PlanParameters.Replicas] = "2"
            },
            objects)
        {
            DryRun = includeDryRun ? CreateDryRun(objects) : null,
            Diffs = includeDiff ? CreateDiffs(objects) : []
        };
        var typedEnvelope = KubernetesApprovalAdapter.CreateEnvelope(
            ApprovalIds.NewPlanId(),
            operation,
            createdAtUtc ?? DateTimeOffset.UtcNow,
            new PlanRequester(Subject, "test"),
            payload);
        var envelope = KubernetesApprovalAdapter.ToEnvelope(typedEnvelope);
        await workflow.CreatePlanAsync(envelope, payload.Namespace, CancellationToken.None);

        return KubernetesApprovalAdapter.Materialize(typedEnvelope);
    }

    private static KubernetesPlanDryRun CreateDryRun(IReadOnlyList<KubernetesObjectRef> objects) =>
        new(
            "succeeded",
            DateTimeOffset.UtcNow,
            objects.Select(obj => new KubernetesPlanDryRunObject(
                $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}",
                "{}")).ToArray(),
            ["299 - admission warning"],
            "Server-side dry-run succeeded.");

    private static KubernetesPlanDiff[] CreateDiffs(IReadOnlyList<KubernetesObjectRef> objects) =>
        objects.Select(obj => new KubernetesPlanDiff(
            obj,
            ApprovalConventions.DiffChangeTypes.Update,
            $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name} will be updated.",
            """
            --- live
            +++ proposed
             spec:
            -  replicas: 1
            +  replicas: 2
            """,
            """{"spec":{"replicas":1}}""",
            """{"spec":{"replicas":2}}""",
            [],
            [],
            ["/spec/replicas"])).ToArray();

    private static async Task<string> CreateChallengeAsync(TestContext context, string planId)
    {
        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(planId, CancellationToken.None);

        return result.ChallengeId!;
    }

    private static async Task<string> CreateStoredChallengeAsync(TestContext context, string planId, string pendingPlanHash)
    {
        var pending = await context.Workflow.GetPendingPlanAsync(planId, CancellationToken.None);
        var challenge = await context.Workflow.CreateChallengeAsync(
            planId,
            pendingPlanHash,
            Subject,
            "test",
            McpGatewayOptions.DefaultApprovalChallengeTtl,
            pending.Envelope?.IntentDigest ?? CreateDigest("intent"),
            pending.Envelope?.ReviewDigest ?? CreateDigest("review"),
            CancellationToken.None);

        return challenge.Id;
    }

    private static TestContext CreateContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-approval-tests", Guid.NewGuid().ToString("N"));
        var workflow = new TestApprovalWorkflow();
        var gatewayOptions = new McpGatewayOptions(
            new GatewayAuthOptions("https://issuer.example.com"),
            "downstream.csproj",
            Path.Combine(root, "guardrails"),
            Directory.GetCurrentDirectory(),
            root,
            "http://gateway.test",
            McpGatewayOptions.DefaultApprovalChallengeTtl);
        var httpContextAccessor = new HttpContextAccessor();
        SetUser(httpContextAccessor, Subject);
        var planReviewAdapter = new KubernetesPlanReviewAdapter();

        return new TestContext(
            new GatewayApprovalService(
                workflow,
                workflow,
                workflow,
                planReviewAdapter,
                new SameSubjectAuthorizationCheck(),
                gatewayOptions,
                httpContextAccessor,
                NullNotificationDispatcher.Instance,
                NullLogger<GatewayApprovalService>.Instance),
            workflow,
            httpContextAccessor,
            planReviewAdapter);
    }

    private static void SetUser(HttpContextAccessor accessor, string subject)
    {
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(GatewayAuthConventions.Claims.Subject, subject),
                new Claim(GatewayAuthConventions.Claims.Scope, "mcp:tools")
            ], "test"))
        };
    }

    private static void SetUnauthenticatedUser(HttpContextAccessor accessor)
    {
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(GatewayAuthConventions.Claims.Subject, Subject)
            ]))
        };
    }

    private static ApprovalDigest CreateDigest(string value) =>
        new(ApprovalConventions.Digests.Sha256, "test.canonicalization.v1", value);

    private sealed record class TestContext(
        IGatewayApprovalService Service,
        TestApprovalWorkflow Workflow,
        HttpContextAccessor HttpContextAccessor,
        IPlanReviewAdapter PlanReviewAdapter);

    private sealed class NullNotificationDispatcher : IApprovalNotificationDispatcher
    {
        public static readonly NullNotificationDispatcher Instance = new();

        public Task NotifyPlanApprovedAsync(string planId, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
