using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.Postgres;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.Approvals.Postgres.Tests.IntegrationTests;

[Trait("Category", "Postgres")]
public sealed class PostgresApprovalPersistenceTests : IAsyncLifetime
{
    private const string PostgresImage = "postgres:17-alpine";
    private const string PlanId = "plan-postgres-1";
    private const string NamespaceName = "mcp-nginx-demo";

    private PostgreSqlContainer? container;

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage)
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
        var persistence = new PostgresApprovalPersistence(dataSource);
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
        var persistence = new PostgresApprovalPersistence(dataSource);
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
        var persistence = new PostgresApprovalPersistence(dataSource);
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
        var persistence = new PostgresApprovalPersistence(dataSource);
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
    public async Task ApprovalAccessCodeStore_GeneratedCode_IsSingleUse()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var persistence = new PostgresApprovalPersistence(dataSource);
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
        var persistence = new PostgresApprovalPersistence(dataSource);
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
        var persistence = new PostgresApprovalPersistence(dataSource);
        var (envelope, grant) = await CreateApprovedPlanAsync(persistence);
        var first = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);

        await persistence.RecordExecutionFailedAsync(
            first.Attempt!,
            "Domain execution failed.",
            "test.failure",
            new PlanAudit(
                ApprovalConventions.AuditEvents.ApplyFailed,
                new InfraGate.Approvals.AuditPayloads.ApplyFailedPayload(
                    envelope.Id,
                    envelope.Operation,
                    "Domain execution failed.")),
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
        var persistence = new PostgresApprovalPersistence(dataSource);
        var (envelope, grant) = await CreateApprovedPlanAsync(persistence);
        var first = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);

        await persistence.RecordExecutionBlockedAsync(
            first.Attempt!,
            "Pre-execution gate blocked mutation.",
            "test.blocked",
            new PlanAudit(
                ApprovalConventions.AuditEvents.ApplyDenied,
                new InfraGate.Approvals.AuditPayloads.ApplyDeniedPayload(
                    envelope.Id,
                    "Pre-execution gate blocked mutation.")),
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
        var persistence = new PostgresApprovalPersistence(dataSource);
        var (envelope, grant) = await CreateApprovedPlanAsync(persistence);
        var attempt = await persistence.BeginExecutionAttemptAsync(envelope.Id, grant, CancellationToken.None);

        await persistence.RecordExecutionSucceededAsync(
            attempt.Attempt!,
            grant,
            "mcp-nginx-demo",
            "Execution succeeded.",
            new PlanAudit(
                ApprovalConventions.AuditEvents.PlanApplied,
                new InfraGate.Approvals.AuditPayloads.PlanAppliedPayload(
                    envelope.Id,
                    envelope.Operation,
                    "mcp-nginx-demo",
                    envelope.ReviewDigest.Value)),
            CancellationToken.None);
        var status = await persistence.GetPlanStatusAsync(envelope.Id, CancellationToken.None);
        var granted = await persistence.GetGrantedPlanAsync(envelope.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.Applied, status.Status);
        Assert.False(granted.IsGranted);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanAlreadyApplied, granted.ReasonCode);
    }

    private static async Task<(PlanEnvelope Envelope, ApprovalGrant Grant)> CreateApprovedPlanAsync(
        PostgresApprovalPersistence persistence)
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
}
