using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.Challenge;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.Postgres;
using InfraGate.AuditOutbox.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.Approvals.Postgres.Tests.IntegrationTests;

[Trait("Category", "Postgres")]
public sealed class PostgresApprovalPersistenceTests : IAsyncLifetime
{

    private const string PlanId = "plan-postgres-1";
    private const string NamespaceName = "mcp-nginx-demo";

    private PostgreSqlContainer? container;

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder(TestContainersConstants.PostgresImage)
            .Build();

        await container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreatePlanAsync_StoredPlan_CanReadPendingPlan()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var envelope = CreateEnvelope();

        var created = await persistence.CreatePlanAsync(envelope, NamespaceName, CancellationToken.None);
        var pending = await persistence.GetPendingPlanAsync(envelope.Id, CancellationToken.None);

        Assert.True(pending.IsPending);
        Assert.Equal(envelope.Id, pending.Envelope?.Id);
        Assert.Equal(created.Hash, pending.Hash);
        Assert.False(string.IsNullOrWhiteSpace(created.Hash));
    }

    [Fact]
    public async Task CreatePlanAsync_OperatorApprovalPolicy_RoundTripsPendingPlan()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var envelope = CreateEnvelope(ApprovalPolicy.OperatorApproval("kubernetes-operators"));

        await persistence.CreatePlanAsync(envelope, NamespaceName, CancellationToken.None);
        var pending = await persistence.GetPendingPlanAsync(envelope.Id, CancellationToken.None);

        Assert.True(pending.IsPending);
        Assert.Equal(envelope.ApprovalPolicy, pending.Envelope?.ApprovalPolicy);
        Assert.Equal(
            "kubernetes-operators",
            pending.Envelope?.ApprovalPolicy.Parameters?[ApprovalConventions.ApprovalPolicyParameters.OperatorGroup]);
    }

    [Fact]
    public async Task ApproveChallengeAsync_PendingChallenge_IssuesGrantAndReturnsGrantedPlan()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var envelope = CreateEnvelope();
        var created = await persistence.CreatePlanAsync(envelope, NamespaceName, CancellationToken.None);
        var challenge = await persistence.CreateChallengeAsync(
            envelope.Id,
            created.Hash,
            envelope.Requester.Subject,
            envelope.Requester.AuthenticationType,
            TimeSpan.FromMinutes(5),
            envelope.IntentDigest,
            envelope.ReviewDigest,
            CancellationToken.None);

        var pending = await persistence.FindPendingChallengeAsync(
            envelope.Id,
            created.Hash,
            envelope.Requester.Subject,
            envelope.IntentDigest,
            envelope.ReviewDigest,
            CancellationToken.None);
        var grant = await persistence.ApproveChallengeAsync(
            challenge,
            envelope,
            envelope.Requester.Subject,
            CancellationToken.None);
        var granted = await persistence.GetGrantedPlanAsync(envelope.Id, CancellationToken.None);
        var approvedChallenge = await persistence.GetChallengeAsync(challenge.Id, CancellationToken.None);

        Assert.NotNull(pending);
        Assert.True(granted.IsGranted, granted.Message);
        Assert.Equal(grant.Id, granted.Grant?.Id);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Approved, approvedChallenge?.Status);
        Assert.Equal(challenge.Id, approvedChallenge?.Outcome?.ChallengeId);
        Assert.Equal(grant.Id, approvedChallenge?.Outcome?.GrantId);
    }

    [Fact]
    public async Task ApproveChallengeAsync_OperatorApprovalPolicy_RoundTripsGrantPolicy()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var envelope = CreateEnvelope(ApprovalPolicy.OperatorApproval("kubernetes-operators"));
        var created = await persistence.CreatePlanAsync(envelope, NamespaceName, CancellationToken.None);
        var challenge = await persistence.CreateChallengeAsync(
            envelope.Id,
            created.Hash,
            envelope.Requester.Subject,
            envelope.Requester.AuthenticationType,
            TimeSpan.FromMinutes(5),
            envelope.IntentDigest,
            envelope.ReviewDigest,
            CancellationToken.None);

        var grant = await persistence.ApproveChallengeAsync(
            challenge,
            envelope,
            "operator-user",
            CancellationToken.None);
        var granted = await persistence.GetGrantedPlanAsync(envelope.Id, CancellationToken.None);

        Assert.True(granted.IsGranted, granted.Message);
        Assert.Equal(envelope.ApprovalPolicy, grant.ApprovalPolicy);
        Assert.Equal(envelope.ApprovalPolicy, granted.Grant?.ApprovalPolicy);
    }

    [Fact]
    public async Task GetPlanStatusAsync_NonExistentPlanId_ReturnsNotFound()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);

        var status = await persistence.GetPlanStatusAsync("nonexistent-plan-id", CancellationToken.None);

        Assert.Equal(PlanStatus.NotFound, status.Status);
    }

    [Fact]
    public async Task GetPlanStatusAsync_PlanWithoutGrant_ReturnsApprovalRequired()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var envelope = CreateEnvelope();
        await persistence.CreatePlanAsync(envelope, NamespaceName, CancellationToken.None);

        var status = await persistence.GetPlanStatusAsync(envelope.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.ApprovalRequired, status.Status);
    }

    [Fact]
    public async Task GetPlanStatusAsync_PlanWithGrantNotApplied_ReturnsApproved()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var (envelope, _) = await CreateApprovedPlanAsync(persistence);

        var status = await persistence.GetPlanStatusAsync(envelope.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.Approved, status.Status);
    }

    [Fact]
    public async Task GetPlanStatusAsync_ExpiredGrant_ReturnsExpired()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var envelope = CreateEnvelope() with { ValidUntilUtc = DateTimeOffset.UtcNow.AddSeconds(-1) };
        var created = await persistence.CreatePlanAsync(envelope, NamespaceName, CancellationToken.None);
        var challenge = await persistence.CreateChallengeAsync(
            envelope.Id,
            created.Hash,
            envelope.Requester.Subject,
            envelope.Requester.AuthenticationType,
            TimeSpan.FromMinutes(5),
            envelope.IntentDigest,
            envelope.ReviewDigest,
            CancellationToken.None);
        await persistence.ApproveChallengeAsync(
            challenge,
            envelope,
            envelope.Requester.Subject,
            CancellationToken.None);

        var status = await persistence.GetPlanStatusAsync(envelope.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.Expired, status.Status);
    }

    [Fact]
    public async Task GetGrantedPlanAsync_NonExistentPlan_ReturnsMissingGrant()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);

        var result = await persistence.GetGrantedPlanAsync("nonexistent-plan-id", CancellationToken.None);

        Assert.False(result.IsGranted);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanNotPending, result.ReasonCode);
    }

    [Fact]
    public async Task GetGrantedPlanAsync_PlanWithoutGrant_ReturnsMissingGrant()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var envelope = CreateEnvelope();
        await persistence.CreatePlanAsync(envelope, NamespaceName, CancellationToken.None);

        var result = await persistence.GetGrantedPlanAsync(envelope.Id, CancellationToken.None);

        Assert.False(result.IsGranted);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanNotApproved, result.ReasonCode);
    }

    [Fact]
    public async Task GetGrantedPlanAsync_AlreadyApplied_ReturnsDenied()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var (envelope, grant) = await CreateApprovedPlanAsync(persistence);
        var attempt = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);
        await persistence.RecordExecutionSucceededAsync(
            attempt.Attempt!,
            grant,
            "mcp-nginx-demo",
            "Execution succeeded.",
            new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.PlanApplied,
                new InfraGate.Approvals.AuditPayloads.PlanAppliedPayload(
                    envelope.Id,
                    envelope.Operation,
                    "mcp-nginx-demo",
                    envelope.ReviewDigest.Value),
                PlanId: envelope.Id,
                GrantId: grant.Id),
            CancellationToken.None);

        var result = await persistence.GetGrantedPlanAsync(envelope.Id, CancellationToken.None);

        Assert.False(result.IsGranted);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanAlreadyApplied, result.ReasonCode);
    }

    [Fact]
    public async Task RecordChallengeOutcomeAsync_EmptyChallengeId_UsesChallengeId()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var envelope = CreateEnvelope();
        var created = await persistence.CreatePlanAsync(envelope, NamespaceName, CancellationToken.None);
        var challenge = await persistence.CreateChallengeAsync(
            envelope.Id,
            created.Hash,
            envelope.Requester.Subject,
            envelope.Requester.AuthenticationType,
            TimeSpan.FromMinutes(5),
            envelope.IntentDigest,
            envelope.ReviewDigest,
            CancellationToken.None);
        var decidedAt = DateTimeOffset.UtcNow;
        var outcome = new ChallengeOutcome(
            ApprovalConventions.ChallengeOutcomeStatuses.Denied,
            "operator-user",
            decidedAt,
            "Not authorized.",
            null);
        var entry = new ApprovalAuditEntry(
            ApprovalConventions.AuditEvents.ApprovalChallengeDenied,
            new InfraGate.Approvals.AuditPayloads.ApprovalChallengeDeniedPayload(
                challenge.Id,
                challenge.PlanId,
                challenge.PendingPlanHash,
                challenge.RequesterSubject,
                "operator-user",
                decidedAt),
            PlanId: challenge.PlanId,
            ChallengeId: challenge.Id,
            ActorSubject: "operator-user",
            Outcome: ApprovalConventions.ChallengeOutcomeStatuses.Denied);

        var updated = await persistence.RecordChallengeOutcomeAsync(challenge, outcome, entry, CancellationToken.None);

        Assert.Equal(ApprovalConventions.ChallengeOutcomeStatuses.Denied, updated.Status);
        Assert.Equal("operator-user", updated.ApproverSubject);
        Assert.NotNull(updated.DecidedAtUtc);
        Assert.NotNull(updated.Outcome);
        Assert.Equal(challenge.Id, updated.Outcome!.ChallengeId);
    }

    [Fact]
    public async Task RecordChallengeOutcomeAsync_PreSetChallengeId_UsesProvidedId()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var envelope = CreateEnvelope();
        var created = await persistence.CreatePlanAsync(envelope, NamespaceName, CancellationToken.None);
        var challenge = await persistence.CreateChallengeAsync(
            envelope.Id,
            created.Hash,
            envelope.Requester.Subject,
            envelope.Requester.AuthenticationType,
            TimeSpan.FromMinutes(5),
            envelope.IntentDigest,
            envelope.ReviewDigest,
            CancellationToken.None);
        var decidedAt2 = DateTimeOffset.UtcNow;
        var outcome2 = new ChallengeOutcome(
            ApprovalIds.NewChallengeOutcomeId(),
            challenge.Id,
            ApprovalConventions.ChallengeOutcomeStatuses.Denied,
            "operator-user",
            decidedAt2,
            "Not authorized.",
            null);
        var entry2 = new ApprovalAuditEntry(
            ApprovalConventions.AuditEvents.ApprovalChallengeDenied,
            new InfraGate.Approvals.AuditPayloads.ApprovalChallengeDeniedPayload(
                challenge.Id,
                challenge.PlanId,
                challenge.PendingPlanHash,
                challenge.RequesterSubject,
                "operator-user",
                decidedAt2),
            PlanId: challenge.PlanId,
            ChallengeId: challenge.Id,
            ActorSubject: "operator-user",
            Outcome: ApprovalConventions.ChallengeOutcomeStatuses.Denied);

        var updated2 = await persistence.RecordChallengeOutcomeAsync(challenge, outcome2, entry2, CancellationToken.None);

        Assert.Equal(outcome2.Id, updated2.Outcome!.Id);
        Assert.Equal(challenge.Id, updated2.Outcome!.ChallengeId);
    }

    [Fact]
    public async Task FindPendingChallengeAsync_NonMatchingSubject_ReturnsNull()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var envelope = CreateEnvelope();
        var created = await persistence.CreatePlanAsync(envelope, NamespaceName, CancellationToken.None);
        await persistence.CreateChallengeAsync(
            envelope.Id,
            created.Hash,
            envelope.Requester.Subject,
            envelope.Requester.AuthenticationType,
            TimeSpan.FromMinutes(5),
            envelope.IntentDigest,
            envelope.ReviewDigest,
            CancellationToken.None);

        var result = await persistence.FindPendingChallengeAsync(
            envelope.Id,
            created.Hash,
            "different-subject",
            envelope.IntentDigest,
            envelope.ReviewDigest,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetChallengeAsync_NonExistentChallenge_ReturnsNull()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);

        var result = await persistence.GetChallengeAsync("challenge-nonexistent", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPendingPlanAsync_AlreadyApplied_ReturnsDenied()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var (envelope, grant) = await CreateApprovedPlanAsync(persistence);
        var attempt = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);
        await persistence.RecordExecutionSucceededAsync(
            attempt.Attempt!,
            grant,
            "mcp-nginx-demo",
            "Execution succeeded.",
            new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.PlanApplied,
                new InfraGate.Approvals.AuditPayloads.PlanAppliedPayload(
                    envelope.Id,
                    envelope.Operation,
                    "mcp-nginx-demo",
                    envelope.ReviewDigest.Value),
                PlanId: envelope.Id,
                GrantId: grant.Id),
            CancellationToken.None);

        var result = await persistence.GetPendingPlanAsync(envelope.Id, CancellationToken.None);

        Assert.False(result.IsPending);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanAlreadyApplied, result.ReasonCode);
    }

    [Fact]
    public async Task BeginExecutionAttemptAsync_AlreadyApplied_ReturnsRefused()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var (envelope, grant) = await CreateApprovedPlanAsync(persistence);
        var attempt = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);
        await persistence.RecordExecutionSucceededAsync(
            attempt.Attempt!,
            grant,
            "mcp-nginx-demo",
            "Execution succeeded.",
            new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.PlanApplied,
                new InfraGate.Approvals.AuditPayloads.PlanAppliedPayload(
                    envelope.Id,
                    envelope.Operation,
                    "mcp-nginx-demo",
                    envelope.ReviewDigest.Value),
                PlanId: envelope.Id,
                GrantId: grant.Id),
            CancellationToken.None);

        var retry = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);

        Assert.False(retry.IsStarted);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanAlreadyApplied, retry.ReasonCode);
    }

    [Fact]
    public async Task ApprovalAccessCodeStore_GeneratedCode_IsSingleUse()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var accessCodes = new PostgresApprovalAccessCodeStore(dataSource);
        var envelope = CreateEnvelope();
        var created = await persistence.CreatePlanAsync(envelope, NamespaceName, CancellationToken.None);
        var challenge = await persistence.CreateChallengeAsync(
            envelope.Id,
            created.Hash,
            envelope.Requester.Subject,
            envelope.Requester.AuthenticationType,
            TimeSpan.FromMinutes(5),
            envelope.IntentDigest,
            envelope.ReviewDigest,
            CancellationToken.None);
        var code = await accessCodes.GenerateAsync(challenge.Id, TimeSpan.FromMinutes(5), CancellationToken.None);

        var first = await accessCodes.ConsumeAsync(code.Code, CancellationToken.None);
        var second = await accessCodes.ConsumeAsync(code.Code, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.Equal(challenge.Id, first.ChallengeId);
        Assert.False(second.Succeeded);
        Assert.Equal(ApprovalConventions.AccessCodes.ConsumeResultReasonCodes.Consumed, second.ReasonCode);
    }

    [Fact]
    public async Task BeginExecutionAttemptAsync_ActiveClaimForPlan_FailsClosed()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var (envelope, grant) = await CreateApprovedPlanAsync(persistence);

        var first = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);
        var second = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);

        Assert.True(first.IsStarted, first.Message);
        Assert.NotNull(first.Attempt);
        Assert.False(second.IsStarted);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ExecutionClaimActive, second.ReasonCode);
    }

    [Fact]
    public async Task RecordExecutionFailedAsync_TerminalOutcome_AllowsRetry()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var (envelope, grant) = await CreateApprovedPlanAsync(persistence);
        var first = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);

        await persistence.RecordExecutionFailedAsync(
            first.Attempt!,
            "Domain execution failed.",
            "test.failure",
            new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.ApplyFailed,
                new InfraGate.Approvals.AuditPayloads.ApplyFailedPayload(
                    envelope.Id,
                    envelope.Operation,
                    "Domain execution failed."),
                PlanId: envelope.Id),
            CancellationToken.None);
        var retry = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);

        Assert.True(retry.IsStarted, retry.Message);
        Assert.NotEqual(first.Attempt!.Id, retry.Attempt?.Id);
    }

    [Fact]
    public async Task RecordExecutionBlockedAsync_TerminalOutcome_RequiresNewPlan()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var (envelope, grant) = await CreateApprovedPlanAsync(persistence);
        var first = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);

        await persistence.RecordExecutionBlockedAsync(
            first.Attempt!,
            "Pre-execution gate blocked mutation.",
            "test.blocked",
            new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.ApplyDenied,
                new InfraGate.Approvals.AuditPayloads.ApplyDeniedPayload(
                    envelope.Id,
                    "Pre-execution gate blocked mutation."),
                PlanId: envelope.Id),
            CancellationToken.None);
        var retry = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);

        Assert.False(retry.IsStarted);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.ExecutionBlocked, retry.ReasonCode);
    }

    [Fact]
    public async Task RecordExecutionSucceededAsync_TerminalOutcome_MarksPlanApplied()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = CreatePersistence(dataSource);
        var (envelope, grant) = await CreateApprovedPlanAsync(persistence);
        var attempt = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);

        await persistence.RecordExecutionSucceededAsync(
            attempt.Attempt!,
            grant,
            "mcp-nginx-demo",
            "Execution succeeded.",
            new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.PlanApplied,
                new InfraGate.Approvals.AuditPayloads.PlanAppliedPayload(
                    envelope.Id,
                    envelope.Operation,
                    "mcp-nginx-demo",
                    envelope.ReviewDigest.Value),
                PlanId: envelope.Id,
                GrantId: grant.Id),
            CancellationToken.None);
        var status = await persistence.GetPlanStatusAsync(envelope.Id, CancellationToken.None);
        var granted = await persistence.GetGrantedPlanAsync(envelope.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.Applied, status.Status);
        Assert.False(granted.IsGranted);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanAlreadyApplied, granted.ReasonCode);
    }

    private static async Task<(PlanEnvelope Envelope, ApprovalGrant Grant)> CreateApprovedPlanAsync(
        IApprovalPersistence persistence)
    {
        var envelope = CreateEnvelope();
        var created = await persistence.CreatePlanAsync(envelope, NamespaceName, CancellationToken.None);
        var challenge = await persistence.CreateChallengeAsync(
            envelope.Id,
            created.Hash,
            envelope.Requester.Subject,
            envelope.Requester.AuthenticationType,
            TimeSpan.FromMinutes(5),
            envelope.IntentDigest,
            envelope.ReviewDigest,
            CancellationToken.None);
        var grant = await persistence.ApproveChallengeAsync(
            challenge,
            envelope,
            envelope.Requester.Subject,
            CancellationToken.None);

        return (envelope, grant);
    }

    private static PlanEnvelope CreateEnvelope(ApprovalPolicy? approvalPolicy = null)
    {
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var payload = JsonSerializer.SerializeToElement(new
        {
            Namespace = NamespaceName,
            Name = "demo",
            Replicas = 2
        });
        var intentDigest = ApprovalDigest.ComputeSha256(
            "test.intent.v1",
            new
            {
                Namespace = NamespaceName,
                Name = "demo",
                Replicas = 2
            });

        var envelope = new PlanEnvelope(
            PlanId,
            ApprovalConventions.Profiles.MutationApproval,
            "kubernetes",
            "scale",
            createdAt,
            createdAt,
            createdAt.AddHours(1),
            new PlanRequester("requester", "oauth-jwt"),
            approvalPolicy ?? ApprovalPolicy.SameSubject(),
            ExecutionReusePolicy.SingleExecution(),
            FreshnessPolicy.Empty,
            new ReviewSurfaceContext("gateway-browser", "test-renderer"),
            [],
            intentDigest,
            ApprovalDigest.ComputeSha256("test.review.v1", new { PlanId }),
            payload);

        return envelope with { ReviewDigest = PlanEnvelopeFactory.ComputeReviewDigest(envelope) };
    }

    private IApprovalPersistence CreatePersistence(NpgsqlDataSource dataSource)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dataSource);
        services.AddPostgresAuditOutbox(dataSource);
        services.AddSingleton<ITransactionalApprovalAuditOutbox, ApprovalAuditOutbox>();
        services.AddSingleton<IApprovalAuditOutbox>(sp => sp.GetRequiredService<ITransactionalApprovalAuditOutbox>());
        services.AddSingleton<IApprovalPersistence, PostgresApprovalPersistence>();
        return services.BuildServiceProvider().GetRequiredService<IApprovalPersistence>();
    }
}
