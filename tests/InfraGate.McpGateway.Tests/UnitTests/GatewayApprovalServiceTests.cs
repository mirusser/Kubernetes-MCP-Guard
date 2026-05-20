using System.Security.Claims;
using System.Text.Json.Nodes;
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
        var plan = await CreatePendingPlanAsync(context.Store);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.ApprovalRequired, result.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.ApprovalRequired, result.ReasonCode);
        Assert.StartsWith("http://gateway.test/approvals/", result.ApprovalUrl, StringComparison.Ordinal);
        Assert.NotNull(result.ChallengeId);
        Assert.NotNull(result.ExpiresAtUtc);
        Assert.False(File.Exists(context.Store.GetGrantPath(plan.Id)));
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_MatchingPendingChallenge_ReturnsExistingApprovalUrl()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);

        var first = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);
        var second = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(first.IsApproved);
        Assert.False(second.IsApproved);
        Assert.Equal(ApprovalGateStatus.ApprovalRequired, second.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.ApprovalRequired, second.ReasonCode);
        Assert.Equal(first.ApprovalUrl, second.ApprovalUrl);
        Assert.Single(Directory.EnumerateFiles(context.Store.ChallengesDirectory));
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_ExpiredPendingChallenge_ReturnsNewApprovalUrl()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        var first = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);
        string firstChallengeId = first.ChallengeId!;
        var challenge = await context.Challenges.GetAsync(firstChallengeId, CancellationToken.None);
        await context.Challenges.SaveAsync(
            challenge! with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) },
            CancellationToken.None);

        var second = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(second.IsApproved);
        Assert.Equal(ApprovalGateStatus.ApprovalRequired, second.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.ApprovalRequired, second.ReasonCode);
        Assert.NotEqual(first.ApprovalUrl, second.ApprovalUrl);
        Assert.Equal(2, Directory.EnumerateFiles(context.Store.ChallengesDirectory).Count());
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_NoAuthenticatedUser_ReturnsRefusal()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        SetUnauthenticatedUser(context.HttpContextAccessor);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired, result.ReasonCode);
        Assert.Contains("authenticated OAuth subject", result.Message);
        Assert.Empty(Directory.EnumerateFiles(context.Store.ChallengesDirectory));
    }

    [Fact]
    public async Task ApproveChallengeAsync_SameSubject_WritesGrantOutcomeAndRejectsReuse()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var approved = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);
        var reused = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);
        var challenge = await context.Challenges.GetAsync(challengeId, CancellationToken.None);

        Assert.True(approved.Succeeded);
        Assert.True(File.Exists(context.Store.GetGrantPath(plan.Id)));
        Assert.False(reused.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ChallengeAlreadyTerminal, reused.ReasonCode);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Approved, challenge?.Status);
        Assert.Equal(ApprovalConventions.ChallengeOutcomeStatuses.Approved, challenge?.Outcome?.Status);
    }

    [Fact]
    public async Task ApproveChallengeAsync_NoAuthenticatedUser_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        SetUnauthenticatedUser(context.HttpContextAccessor);

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.AuthenticatedSubjectRequired, result.ReasonCode);
        Assert.False(File.Exists(context.Store.GetGrantPath(plan.Id)));
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
        var plan = await CreatePendingPlanAsync(context.Store, includeDryRun: false);
        var hash = await ApprovalStore.ComputeSha256Async(context.Store.GetPendingPath(plan.Id), CancellationToken.None);
        var challengeId = await CreateStoredChallengeAsync(context, plan.Id, hash);

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.MissingReviewEvidence, result.ReasonCode);
        Assert.False(File.Exists(context.Store.GetGrantPath(plan.Id)));
    }

    [Fact]
    public async Task ApproveChallengeAsync_PendingPlanWithoutDiff_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(
            context.Store,
            includeDiff: false,
            operation: KubernetesAdapterConventions.PlanOperations.Apply);
        var hash = await ApprovalStore.ComputeSha256Async(context.Store.GetPendingPath(plan.Id), CancellationToken.None);
        var challengeId = await CreateStoredChallengeAsync(context, plan.Id, hash);

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.MissingReviewEvidence, result.ReasonCode);
        Assert.False(File.Exists(context.Store.GetGrantPath(plan.Id)));
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_ApprovedHashWithoutChallenge_ReturnsApprovalUrl()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        var hash = await ApprovalStore.ComputeSha256Async(context.Store.GetPendingPath(plan.Id), CancellationToken.None);
        var legacyApprovedPath = LegacyApprovedPath(context.Store, plan.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyApprovedPath)!);
        await File.WriteAllTextAsync(legacyApprovedPath, hash, CancellationToken.None);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.ApprovalRequired, result.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.ApprovalRequired, result.ReasonCode);
        Assert.NotNull(result.ApprovalUrl);
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_GrantReviewDigestMismatch_WritesApplyDeniedAudit()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        var approved = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);
        await ChangePendingPlanReviewEvidenceAsync(context.Store, plan.Id);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.True(approved.Succeeded);
        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.DigestChanged, result.ReasonCode);
        string audit = await File.ReadAllTextAsync(context.Store.AuditPath, CancellationToken.None);
        Assert.Contains($@"""eventName"": ""{ApprovalConventions.AuditEvents.ApplyDenied}""", audit);
        Assert.Contains($"\"planId\": \"{plan.Id}\"", audit);
        Assert.Contains("review digest no longer matches", audit);
    }

    [Fact]
    public async Task ApproveChallengeAsync_DifferentSubject_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        SetUser(context.HttpContextAccessor, "other-user");

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired, result.ReasonCode);
        Assert.False(File.Exists(context.Store.GetGrantPath(plan.Id)));
    }

    [Fact]
    public async Task ApproveChallengeAsync_PendingPlanHashDrift_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        await File.AppendAllTextAsync(context.Store.GetPendingPath(plan.Id), Environment.NewLine, CancellationToken.None);

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PendingPlanChanged, result.ReasonCode);
        Assert.False(File.Exists(context.Store.GetGrantPath(plan.Id)));
    }

    [Fact]
    public async Task ApproveChallengeAsync_ExpiredChallenge_RejectsAndSetsExpiredStatus()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var challenge = await context.Challenges.GetAsync(challengeId, CancellationToken.None);
        var expired = challenge! with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) };
        await context.Challenges.SaveAsync(expired, CancellationToken.None);

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);
        var updated = await context.Challenges.GetAsync(challengeId, CancellationToken.None);

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
        var plan = await CreatePendingPlanAsync(context.Store, includeDryRun: false);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.MissingReviewEvidence, result.ReasonCode);
        Assert.Empty(Directory.EnumerateFiles(context.Store.ChallengesDirectory));
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_PlanWithoutDryRunAndLegacyApprovedHash_ReturnsRefusal()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store, includeDryRun: false);
        var hash = await ApprovalStore.ComputeSha256Async(context.Store.GetPendingPath(plan.Id), CancellationToken.None);
        var legacyApprovedPath = LegacyApprovedPath(context.Store, plan.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyApprovedPath)!);
        await File.WriteAllTextAsync(legacyApprovedPath, hash, CancellationToken.None);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.MissingReviewEvidence, result.ReasonCode);
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_PlanWithoutDiffAndLegacyApprovedHash_ReturnsRefusal()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(
            context.Store,
            includeDiff: false,
            operation: KubernetesAdapterConventions.PlanOperations.Apply);
        var hash = await ApprovalStore.ComputeSha256Async(context.Store.GetPendingPath(plan.Id), CancellationToken.None);
        var legacyApprovedPath = LegacyApprovedPath(context.Store, plan.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyApprovedPath)!);
        await File.WriteAllTextAsync(legacyApprovedPath, hash, CancellationToken.None);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.MissingReviewEvidence, result.ReasonCode);
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_PlanWithoutDiff_ReturnsRefusal()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(
            context.Store,
            includeDiff: false,
            operation: KubernetesAdapterConventions.PlanOperations.Apply);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.MissingReviewEvidence, result.ReasonCode);
        Assert.Empty(Directory.EnumerateFiles(context.Store.ChallengesDirectory));
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_PlanWindowNotStarted_ReturnsRefusal()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store, createdAtUtc: DateTimeOffset.UtcNow.AddHours(1));

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.PlanNotStarted, result.ReasonCode);
        Assert.Empty(Directory.EnumerateFiles(context.Store.ChallengesDirectory));
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_PlanWindowExpired_ReturnsRefusal()
    {
        var context = CreateContext();
        // ValidFromUtc = now-2h, ValidUntilUtc = now-1h (window closed 1 hour ago)
        var plan = await CreatePendingPlanAsync(context.Store, createdAtUtc: DateTimeOffset.UtcNow.AddHours(-2));

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.Equal(ApprovalGateStatus.Refused, result.Status);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.PlanExpired, result.ReasonCode);
        Assert.Empty(Directory.EnumerateFiles(context.Store.ChallengesDirectory));
    }

    [Fact]
    public async Task EnsureApprovedOrCreateChallengeAsync_PlanWindowNearExpiry_CapsChallengeTtl()
    {
        var context = CreateContext();
        // ValidUntilUtc = now+5min; configured TTL = 15min → effective TTL should be ~5min
        var plan = await CreatePendingPlanAsync(context.Store, createdAtUtc: DateTimeOffset.UtcNow.AddMinutes(-55));

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);
        var challenge = await context.Challenges.GetAsync(result.ChallengeId!, CancellationToken.None);

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
        var plan = await CreatePendingPlanAsync(context.Store);

        var result = await context.Service.EnsureApprovedOrCreateChallengeAsync(plan.Id, CancellationToken.None);
        var challenge = await context.Challenges.GetAsync(result.ChallengeId!, CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.NotNull(challenge);
        Assert.True(challenge.ExpiresAtUtc >= DateTimeOffset.UtcNow.AddMinutes(14),
            $"Expected ExpiresAtUtc >= now+14min but was {challenge.ExpiresAtUtc}");
    }

    [Fact]
    public async Task GetApprovalPageAsync_ValidPlan_IncludesDiffModel()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
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
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        var pendingPath = context.Store.GetPendingPath(plan.Id);
        var json = await File.ReadAllTextAsync(pendingPath, CancellationToken.None);
        await File.WriteAllTextAsync(
            pendingPath,
            json.Replace("/spec/replicas", "/spec/template/spec/containers/0/image", StringComparison.Ordinal),
            CancellationToken.None);

        var result = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PendingPlanChanged, result.ReasonCode);
        Assert.False(File.Exists(context.Store.GetGrantPath(plan.Id)));
    }

    [Fact]
    public async Task DenyChallengeAsync_MarksDeniedWithoutApproving()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var result = await context.Service.DenyChallengeAsync(challengeId, CancellationToken.None);
        var challenge = await context.Challenges.GetAsync(challengeId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Denied, challenge?.Status);
        Assert.False(File.Exists(context.Store.GetGrantPath(plan.Id)));
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
        var plan = await CreatePendingPlanAsync(context.Store);
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
        var plan = await CreatePendingPlanAsync(context.Store);
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
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        SetUser(context.HttpContextAccessor, "other-user");

        var result = await context.Service.DenyChallengeAsync(challengeId, CancellationToken.None);
        var challenge = await context.Challenges.GetAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired, result.ReasonCode);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Rejected, challenge?.Status);
        Assert.Equal(ApprovalConventions.ChallengeOutcomeStatuses.Rejected, challenge?.Outcome?.Status);
    }

    [Fact]
    public async Task CancelChallengeAsync_SameSubject_CancelsWithoutGrantAndWritesAudit()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var result = await context.Service.CancelChallengeAsync(challengeId, CancellationToken.None);
        var challenge = await context.Challenges.GetAsync(challengeId, CancellationToken.None);
        string audit = await File.ReadAllTextAsync(context.Store.AuditPath, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Canceled, challenge?.Status);
        Assert.Equal(ApprovalConventions.ChallengeOutcomeStatuses.Canceled, challenge?.Outcome?.Status);
        Assert.Equal(Subject, challenge?.Outcome?.ActorSubject);
        Assert.Null(challenge?.Outcome?.GrantId);
        Assert.False(File.Exists(context.Store.GetGrantPath(plan.Id)));
        Assert.Contains($@"""eventName"": ""{ApprovalConventions.AuditEvents.ApprovalChallengeCanceled}""", audit);
    }

    [Fact]
    public async Task CancelChallengeAsync_AlreadyCanceled_RejectsReuse()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
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
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var challenge = await context.Challenges.GetAsync(challengeId, CancellationToken.None);
        var expiredChallenge = challenge! with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
        await context.Challenges.SaveAsync(expiredChallenge, CancellationToken.None);

        var result = await context.Service.CancelChallengeAsync(challengeId, CancellationToken.None);
        var updated = await context.Challenges.GetAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ChallengeExpired, result.ReasonCode);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Expired, updated?.Status);
    }

    [Fact]
    public async Task CancelChallengeAsync_DifferentSubject_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);
        SetUser(context.HttpContextAccessor, "other-user");

        var result = await context.Service.CancelChallengeAsync(challengeId, CancellationToken.None);
        var challenge = await context.Challenges.GetAsync(challengeId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(McpGatewayConventions.ApprovalReasonCodes.SameSubjectRequired, result.ReasonCode);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Rejected, challenge?.Status);
        Assert.Equal(ApprovalConventions.ChallengeOutcomeStatuses.Rejected, challenge?.Outcome?.Status);
    }

    [Fact]
    public async Task ApproveChallengeAsync_CanceledChallenge_Rejects()
    {
        var context = CreateContext();
        var plan = await CreatePendingPlanAsync(context.Store);
        var challengeId = await CreateChallengeAsync(context, plan.Id);

        var canceled = await context.Service.CancelChallengeAsync(challengeId, CancellationToken.None);
        var approved = await context.Service.ApproveChallengeAsync(challengeId, CancellationToken.None);

        Assert.True(canceled.Succeeded);
        Assert.False(approved.Succeeded);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ChallengeAlreadyTerminal, approved.ReasonCode);
        Assert.False(File.Exists(context.Store.GetGrantPath(plan.Id)));
    }

    private static async Task<KubernetesPlan> CreatePendingPlanAsync(
        ApprovalStore store,
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
        var envelope = KubernetesApprovalAdapter.CreateEnvelope(
            ApprovalStore.NewPlanId(),
            operation,
            createdAtUtc ?? DateTimeOffset.UtcNow,
            new PlanRequester(Subject, "test"),
            payload);
        await store.CreatePlanAsync(envelope, payload.Namespace, CancellationToken.None);

        return KubernetesApprovalAdapter.Materialize(envelope);
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
        var pending = await context.Store.GetPendingPlanAsync(planId, CancellationToken.None);
        var challenge = await context.Challenges.CreateAsync(
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

    private static async Task ChangePendingPlanReviewEvidenceAsync(ApprovalStore store, string planId)
    {
        string pendingPath = store.GetPendingPath(planId);
        string json = await File.ReadAllTextAsync(pendingPath, CancellationToken.None);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Pending plan did not parse as a JSON object.");
        var digest = root["evidenceArtifacts"]?[0]?["digest"]?.AsObject()
            ?? throw new InvalidOperationException("Pending plan did not contain an evidence artifact digest.");
        digest["value"] = "tampered-review-evidence";

        await File.WriteAllTextAsync(pendingPath, root.ToJsonString(), CancellationToken.None);
    }

    private static string LegacyApprovedPath(ApprovalStore store, string planId) =>
        Path.Combine(
            Path.GetDirectoryName(store.PendingDirectory)!,
            "approved",
            planId + ApprovalConventions.Storage.Sha256Extension);

    private static TestContext CreateContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-approval-tests", Guid.NewGuid().ToString("N"));
        var storeOptions = new ApprovalStoreOptions(root);
        var store = new ApprovalStore(storeOptions);
        var challenges = new ApprovalChallengeStore(storeOptions);
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
        var planReviewRenderer = new KubernetesPlanReviewRenderer();

        return new TestContext(
            new GatewayApprovalService(
                store,
                challenges,
                planReviewAdapter,
                planReviewRenderer,
                new SameSubjectAuthorizationCheck(),
                gatewayOptions,
                httpContextAccessor,
                NullNotificationDispatcher.Instance,
                NullLogger<GatewayApprovalService>.Instance),
            store,
            challenges,
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
        ApprovalStore Store,
        ApprovalChallengeStore Challenges,
        HttpContextAccessor HttpContextAccessor,
        IPlanReviewAdapter PlanReviewAdapter);

    private sealed class NullNotificationDispatcher : IApprovalNotificationDispatcher
    {
        public static readonly NullNotificationDispatcher Instance = new();

        public Task NotifyPlanApprovedAsync(string planId, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
