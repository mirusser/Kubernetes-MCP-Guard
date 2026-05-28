using System.Text.Json;
using Dapper;
using InfraGate.Approvals;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Postgres;
using InfraGate.Approvals.PreExecution;
using InfraGate.AuditOutbox.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
namespace InfraGate.Approvals.Postgres.Tests.IntegrationTests;
[Trait("Category", "Postgres")]
public sealed class ApprovalPreExecutionGateIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private NpgsqlDataSource? dataSource;
    private IApprovalPersistence? persistence;
    private IApprovalAuditOutbox? outbox;
    private ApprovalPreExecutionGate? gate;

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder("postgres:16.2-alpine").Build();
        await container.StartAsync();

        var services = new ServiceCollection();
        services.AddPostgresApprovalPersistence(container.GetConnectionString());
        
        var provider = services.BuildServiceProvider();
        dataSource = provider.GetRequiredService<NpgsqlDataSource>();
        persistence = provider.GetRequiredService<IApprovalPersistence>();
        outbox = provider.GetRequiredService<IApprovalAuditOutbox>();
        
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        
        gate = new ApprovalPreExecutionGate(persistence, outbox);
    }

    public async Task DisposeAsync()
    {
        if (dataSource is not null) await dataSource.DisposeAsync();
        if (container is not null) await container.DisposeAsync();
    }

    [Fact]
    public async Task EvaluateAsync_GrantedPlan_PublishesGrantValidatedAudit()
    {
        var envelope = CreatePlanEnvelope();
        var created = await persistence!.CreatePlanAsync(envelope, "mcp-nginx-demo", CancellationToken.None);
        var challenge = await persistence.CreateChallengeAsync(
            envelope.Id,
            created.Hash,
            envelope.Requester.Subject,
            envelope.Requester.AuthenticationType,
            TimeSpan.FromMinutes(5),
            envelope.IntentDigest,
            envelope.ReviewDigest,
            CancellationToken.None);
        var grant = await persistence.ApproveChallengeAsync(challenge, envelope, "requester", CancellationToken.None);

        var result = await gate!.EvaluateAsync(
            envelope.Id,
            new PassingDomainPlanExecutor(),
            CancellationToken.None);

        Assert.True(result.IsPassed);

        await using var queryConn = await dataSource!.OpenConnectionAsync();
        var row = await queryConn.QuerySingleOrDefaultAsync<AuditRow>(
            "SELECT event_name, plan_id, grant_id FROM approvals.audit_outbox WHERE event_name = @eventName",
            new { eventName = ApprovalConventions.AuditEvents.PreExecutionGrantValidated });

        Assert.NotNull(row);
        Assert.Equal(ApprovalConventions.AuditEvents.PreExecutionGrantValidated, row.event_name);
        Assert.Equal(envelope.Id, row.plan_id);
        Assert.Equal(grant.Id, row.grant_id);
    }

    [Fact]
    public async Task EvaluateAsync_MissingPendingPlan_ReturnsReasonCode()
    {
        var planId = ApprovalIds.NewPlanId();

        var result = await gate!.EvaluateAsync(
            planId,
            new PassingDomainPlanExecutor(),
            CancellationToken.None);

        Assert.False(result.IsPassed);
        Assert.Equal(ApprovalConventions.ResultReasonCodes.PlanNotPending, result.ReasonCode);
    }

    private static PlanEnvelope CreatePlanEnvelope()
    {
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var planId = ApprovalIds.NewPlanId();
        var payload = JsonSerializer.SerializeToElement(new { name = "demo", replicas = "2" });
        var intentDigest = ApprovalDigest.ComputeSha256("dummy.intent.v1", new { operation = "scale", name = "demo", replicas = "2" });
        
        var envelope = new PlanEnvelope(
            planId,
            ApprovalConventions.Profiles.MutationApproval,
            "kubernetes",
            "scale",
            createdAt,
            createdAt,
            createdAt.AddHours(1),
            new PlanRequester("requester", "test"),
            ApprovalPolicy.SameSubject(),
            ExecutionReusePolicy.SingleExecution(),
            FreshnessPolicy.Empty,
            new ReviewSurfaceContext(ApprovalConventions.ReviewSurfaces.GatewayBrowser, "dummy-review-v1"),
            [],
            intentDigest,
            ApprovalDigest.ComputeSha256("test.review.v1", new { planId }),
            payload);

        return envelope with { ReviewDigest = PlanEnvelopeFactory.ComputeReviewDigest(envelope) };
    }

    private sealed class PassingDomainPlanExecutor : IDomainPlanExecutor
    {
        public Task<DomainPlanExecutionResult> CheckPreExecutionAsync(PlanEnvelope envelope, CancellationToken ct) =>
            Task.FromResult(DomainPlanExecutionResult.Success("Pre-execution checks passed.", "demo"));

        public Task<DomainPlanExecutionResult> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct) =>
            Task.FromResult(DomainPlanExecutionResult.Success("Executed.", "demo"));
    }

    private sealed record class AuditRow(string event_name, string plan_id, string grant_id);
}
