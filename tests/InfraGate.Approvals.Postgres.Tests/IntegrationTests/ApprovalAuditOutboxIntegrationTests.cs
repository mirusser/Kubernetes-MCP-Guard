using Dapper;
using InfraGate.Approvals;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.Postgres;
using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.Approvals.Postgres.Tests.IntegrationTests;

[Trait("Category", "Postgres")]
public sealed class ApprovalAuditOutboxIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private NpgsqlDataSource? dataSource;
    private IPostgresAuditOutboxCore? core;
    private IApprovalAuditOutbox? outbox;

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder(TestContainersConstants.PostgresImage).Build();
        await container.StartAsync();

        dataSource = NpgsqlDataSource.Create(container.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);

        var services = new ServiceCollection();
        services.AddPostgresAuditOutbox(dataSource);
        core = services.BuildServiceProvider().GetRequiredService<IPostgresAuditOutboxCore>();
        outbox = new ApprovalAuditOutbox(core, dataSource);
    }

    public async Task DisposeAsync()
    {
        if (dataSource is not null) await dataSource.DisposeAsync();
        if (container is not null) await container.DisposeAsync();
    }

    [Fact]
    public async Task AppendAsync_PrimaryOverload_WithNullEntry_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            outbox!.AppendAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task AppendAsync_TransactionOverload_WithNullEntry_ThrowsArgumentNullException()
    {
        await using var conn = await dataSource!.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ((ITransactionalApprovalAuditOutbox)outbox!).AppendAsync(null!, conn, tx, CancellationToken.None));
    }

    [Fact]
    public async Task AppendAsync_ExtractsCorrelationColumnsToCanonicalFormat_InDatabase()
    {
        var entry = new ApprovalAuditEntry(
            EventName: "test.event",
            ActorSubject: "sub",
            ActorClientId: "client",
            Outcome: "success",
            Reason: "test reason",
            Payload: new Dictionary<string, object> { ["key"] = "value" },
            PlanId: "plan-123",
            ChallengeId: "chal-456",
            GrantId: "grant-789",
            ExecutionAttemptId: "exec-001");

        long sequence = await outbox!.AppendAsync(entry, CancellationToken.None);
        Assert.True(sequence > 0);

        await using var queryConn = await dataSource!.OpenConnectionAsync();
        var row = await queryConn.QuerySingleAsync(
            "SELECT event_name, actor_subject, actor_client_id, outcome, reason, " +
            "plan_id, challenge_id, grant_id, execution_attempt_id " +
            "FROM approvals.audit_outbox WHERE audit_sequence = @sequence",
            new { sequence });

        Assert.Equal(entry.EventName, (string)row.event_name);
        Assert.Equal(entry.ActorSubject, (string)row.actor_subject);
        Assert.Equal(entry.ActorClientId, (string)row.actor_client_id);
        Assert.Equal(entry.Outcome, (string)row.outcome);
        Assert.Equal(entry.Reason, (string)row.reason);
        Assert.Equal(entry.PlanId, (string)row.plan_id);
        Assert.Equal(entry.ChallengeId, (string)row.challenge_id);
        Assert.Equal(entry.GrantId, (string)row.grant_id);
        Assert.Equal(entry.ExecutionAttemptId, (string)row.execution_attempt_id);
    }
}
