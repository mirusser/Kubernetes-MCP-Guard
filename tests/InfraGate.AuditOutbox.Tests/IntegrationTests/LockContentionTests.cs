using Dapper;
using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.AuditOutbox.Tests.IntegrationTests;

[Trait("Category", "Postgres")]
public sealed class LockContentionTests : IAsyncLifetime
{
    private const string PostgresImage = "postgres:17-alpine";
    private const string TestSchema = "test_stream";

    private static readonly string FixturesDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private PostgreSqlContainer? container;
    private NpgsqlDataSource? dataSource;
    private IAuditOutboxCore? core;

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage).Build();
        await container.StartAsync();

        dataSource = NpgsqlDataSource.Create(container.GetConnectionString());

        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            dataSource, TestSchema, FixturesDirectory, CancellationToken.None);

        // Also create a second schema for the cross-stream contention test.
        await using var setupConn = await dataSource.OpenConnectionAsync();
        await setupConn.ExecuteAsync("""
            CREATE SCHEMA IF NOT EXISTS test_stream_b;
            CREATE TABLE test_stream_b.audit_outbox (
                audit_sequence     bigint generated always as identity primary key,
                event_name         text        not null,
                occurred_at_utc    timestamptz not null,
                actor_subject      text,
                actor_client_id    text,
                outcome            text,
                reason             text,
                previous_event_hash text,
                event_hash         text        not null,
                payload_json_text  text        not null,
                published_at_utc   timestamptz,
                publish_attempts   int         not null default 0,
                last_publish_error text,
                test_entity_id     text
            )
            """);

        var services = new ServiceCollection();
        services.AddPostgresAuditOutbox(dataSource);
        var sp = services.BuildServiceProvider();
        core = sp.GetRequiredService<IAuditOutboxCore>();
    }

    public async Task DisposeAsync()
    {
        if (dataSource is not null)
        {
            await dataSource.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task AppendAsync_ParallelWritesToSameStream_ChainIsIntact()
    {
        const int concurrency = 5;

        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            await using var connection = await dataSource!.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await core!.AppendAsync(TestSchema, BuildRow($"parallel.event.{i}"), connection, tx, CancellationToken.None);
            await tx.CommitAsync();
        });

        await Task.WhenAll(tasks);

        await using var queryConn = await dataSource!.OpenConnectionAsync();
        var rows = (await queryConn.QueryAsync<(string? PrevHash, string EventHash)>(
            $"SELECT previous_event_hash, event_hash FROM {TestSchema}.audit_outbox ORDER BY audit_sequence"))
            .ToArray();

        Assert.Equal(concurrency, rows.Length);
        Assert.Null(rows[0].PrevHash);

        for (int i = 1; i < rows.Length; i++)
        {
            Assert.Equal(rows[i - 1].EventHash, rows[i].PrevHash);
        }
    }

    [Fact]
    public async Task AppendAsync_ParallelWritesToDifferentStreams_BothSucceedWithIntactChains()
    {
        var taskA = Task.Run(async () =>
        {
            await using var connection = await dataSource!.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await core!.AppendAsync(TestSchema, BuildRow("stream-a.event"), connection, tx, CancellationToken.None);
            await tx.CommitAsync();
        });

        var taskB = Task.Run(async () =>
        {
            await using var connection = await dataSource!.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await core!.AppendAsync("test_stream_b", BuildRow("stream-b.event"), connection, tx, CancellationToken.None);
            await tx.CommitAsync();
        });

        await Task.WhenAll(taskA, taskB);

        await using var queryConn = await dataSource!.OpenConnectionAsync();

        int countA = await queryConn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TestSchema}.audit_outbox");
        int countB = await queryConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM test_stream_b.audit_outbox");

        Assert.Equal(1, countA);
        Assert.Equal(1, countB);

        string? prevHashA = await queryConn.ExecuteScalarAsync<string?>(
            $"SELECT previous_event_hash FROM {TestSchema}.audit_outbox LIMIT 1");
        string? prevHashB = await queryConn.ExecuteScalarAsync<string?>(
            "SELECT previous_event_hash FROM test_stream_b.audit_outbox LIMIT 1");

        Assert.Null(prevHashA);
        Assert.Null(prevHashB);
    }

    private static AuditOutboxRow BuildRow(string eventName) =>
        new(
            EventName: eventName,
            OccurredAtUtc: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ActorSubject: "service:test",
            ActorClientId: null,
            Outcome: null,
            Reason: null,
            PayloadJsonText: "{}",
            CorrelationColumns: new Dictionary<string, object?> { ["test_entity_id"] = "entity-1" });
}
