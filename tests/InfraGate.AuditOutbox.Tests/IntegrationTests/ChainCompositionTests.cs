using Dapper;
using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.AuditOutbox.Tests.IntegrationTests;

[Trait("Category", "Postgres")]
public sealed class ChainCompositionTests : IAsyncLifetime
{
    private const string TestSchema = "test_stream";

    private static readonly string FixturesDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private PostgreSqlContainer? container;
    private NpgsqlDataSource? dataSource;
    private IAuditOutboxCore? core;

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder(TestContainersConstants.PostgresImage).Build();
        await container.StartAsync();

        dataSource = NpgsqlDataSource.Create(container.GetConnectionString());

        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            dataSource, TestSchema, FixturesDirectory, CancellationToken.None);

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
    public async Task AppendAsync_FirstRow_PreviousEventHashIsNull()
    {
        var row = BuildRow("first.event");

        await using var connection = await dataSource!.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await core!.AppendAsync(TestSchema, row, connection, tx, CancellationToken.None);
        await tx.CommitAsync();

        await using var queryConn = await dataSource.OpenConnectionAsync();
        var result = await queryConn.QuerySingleAsync<(string? PrevHash, string EventHash)>(
            $"SELECT previous_event_hash, event_hash FROM {TestSchema}.audit_outbox ORDER BY audit_sequence");

        Assert.Null(result.PrevHash);
        Assert.NotEmpty(result.EventHash);
    }

    [Fact]
    public async Task AppendAsync_MultipleRows_ChainIsIntact()
    {
        const int rowCount = 5;

        for (int i = 0; i < rowCount; i++)
        {
            await using var connection = await dataSource!.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await core!.AppendAsync(TestSchema, BuildRow($"event.{i}"), connection, tx, CancellationToken.None);
            await tx.CommitAsync();
        }

        await using var queryConn = await dataSource!.OpenConnectionAsync();
        var rows = (await queryConn.QueryAsync<(long Seq, string? PrevHash, string EventHash)>(
            $"SELECT audit_sequence, previous_event_hash, event_hash FROM {TestSchema}.audit_outbox ORDER BY audit_sequence"))
            .ToArray();

        Assert.Equal(rowCount, rows.Length);
        Assert.Null(rows[0].PrevHash);

        for (int i = 1; i < rows.Length; i++)
        {
            Assert.Equal(rows[i - 1].EventHash, rows[i].PrevHash);
        }
    }

    [Fact]
    public async Task AppendAsync_SecondRow_PreviousEventHashEqualsFirstRowEventHash()
    {
        var firstRow = BuildRow("first.event");
        var secondRow = BuildRow("second.event");

        await using var conn1 = await dataSource!.OpenConnectionAsync();
        await using var tx1 = await conn1.BeginTransactionAsync();
        await core!.AppendAsync(TestSchema, firstRow, conn1, tx1, CancellationToken.None);
        await tx1.CommitAsync();

        await using var conn2 = await dataSource.OpenConnectionAsync();
        await using var tx2 = await conn2.BeginTransactionAsync();
        await core.AppendAsync(TestSchema, secondRow, conn2, tx2, CancellationToken.None);
        await tx2.CommitAsync();

        await using var queryConn = await dataSource.OpenConnectionAsync();
        var rows = (await queryConn.QueryAsync<(string? PrevHash, string EventHash)>(
            $"SELECT previous_event_hash, event_hash FROM {TestSchema}.audit_outbox ORDER BY audit_sequence"))
            .ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Equal(rows[0].EventHash, rows[1].PrevHash);
    }

    [Fact]
    public async Task AppendAsync_SameRowValues_ProducesDifferentHashDueToChaining()
    {
        var row = BuildRow("repeated.event");

        await using var conn1 = await dataSource!.OpenConnectionAsync();
        await using var tx1 = await conn1.BeginTransactionAsync();
        await core!.AppendAsync(TestSchema, row, conn1, tx1, CancellationToken.None);
        await tx1.CommitAsync();

        await using var conn2 = await dataSource.OpenConnectionAsync();
        await using var tx2 = await conn2.BeginTransactionAsync();
        await core.AppendAsync(TestSchema, row, conn2, tx2, CancellationToken.None);
        await tx2.CommitAsync();

        await using var queryConn = await dataSource.OpenConnectionAsync();
        var hashes = (await queryConn.QueryAsync<string>(
            $"SELECT event_hash FROM {TestSchema}.audit_outbox ORDER BY audit_sequence"))
            .ToArray();

        Assert.Equal(2, hashes.Length);
        Assert.NotEqual(hashes[0], hashes[1]);
    }

    [Fact]
    public async Task AppendAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        await using var conn = await dataSource!.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var row = BuildRow("test-cancelled");
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            core!.AppendAsync(TestSchema, row, conn, tx, cts.Token));
    }

    private static AuditOutboxRow BuildRow(string eventName) =>
        new(
            EventName: eventName,
            OccurredAtUtc: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ActorSubject: "service:test",
            ActorClientId: null,
            Outcome: "success",
            Reason: null,
            PayloadJsonText: """{"test": true}""",
            CorrelationColumns: new Dictionary<string, object?> { ["test_entity_id"] = "entity-1" });
}
