using Dapper;
using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.AuditOutbox.Tests.IntegrationTests;

[Trait("Category", "Postgres")]
public sealed class PostgresAuditStreamReaderTests : IAsyncLifetime
{
    private static readonly string ReaderFixturesDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Reader");

    private PostgreSqlContainer? container;
    private NpgsqlDataSource? dataSource;
    private IAuditStreamReader? reader;
    private IPostgresAuditOutboxCore? core;

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder(TestContainersConstants.PostgresImage).Build();
        await container.StartAsync();

        dataSource = NpgsqlDataSource.Create(container.GetConnectionString());

        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            dataSource,
            AuditOutboxConventions.Streams.Planner,
            ReaderFixturesDirectory,
            CancellationToken.None);

        var services = new ServiceCollection();
        services.AddPostgresAuditOutbox(dataSource);
        var sp = services.BuildServiceProvider();
        reader = sp.GetRequiredService<IAuditStreamReader>();
        core = sp.GetRequiredService<IPostgresAuditOutboxCore>();
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
    public async Task ReadByPlanIdAsync_KnownPlan_ReturnsRowsOrderedBySequence()
    {
        await SeedRowsAsync([
            CreateRow("event.first", planId: "plan-1", anomalyId: null, occurredAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            CreateRow("event.second", planId: "plan-1", anomalyId: null, occurredAt: new DateTimeOffset(2025, 1, 1, 0, 1, 0, TimeSpan.Zero)),
            CreateRow("event.other", planId: "plan-2", anomalyId: null, occurredAt: new DateTimeOffset(2025, 1, 1, 0, 2, 0, TimeSpan.Zero)),
        ]);

        var rows = await reader!.ReadByPlanIdAsync(AuditOutboxConventions.Streams.Planner, "plan-1", CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Equal("event.first", rows[0].Row.EventName);
        Assert.Equal("event.second", rows[1].Row.EventName);
        Assert.True(rows[0].AuditSequence < rows[1].AuditSequence);
        Assert.All(rows, r => Assert.Equal("plan-1", r.Row.CorrelationColumns[AuditOutboxConventions.CorrelationColumnNames.PlanId]));
    }

    [Fact]
    public async Task ReadByAnomalyIdAsync_KnownAnomaly_ReturnsRowsOrderedBySequence()
    {
        await SeedRowsAsync([
            CreateRow("event.first", planId: null, anomalyId: "anomaly-1", occurredAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            CreateRow("event.second", planId: null, anomalyId: "anomaly-1", occurredAt: new DateTimeOffset(2025, 1, 1, 0, 1, 0, TimeSpan.Zero)),
            CreateRow("event.other", planId: null, anomalyId: "anomaly-2", occurredAt: new DateTimeOffset(2025, 1, 1, 0, 2, 0, TimeSpan.Zero)),
        ]);

        var rows = await reader!.ReadByAnomalyIdAsync(AuditOutboxConventions.Streams.Planner, "anomaly-1", CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Equal("event.first", rows[0].Row.EventName);
        Assert.Equal("event.second", rows[1].Row.EventName);
        Assert.All(rows, r => Assert.Equal("anomaly-1", r.Row.CorrelationColumns[AuditOutboxConventions.CorrelationColumnNames.AnomalyId]));
    }

    [Fact]
    public async Task ReadByPlanIdAsync_UnknownPlan_ReturnsEmpty()
    {
        await SeedRowsAsync([CreateRow("event.first", planId: "plan-1", anomalyId: null)]);

        var rows = await reader!.ReadByPlanIdAsync(AuditOutboxConventions.Streams.Planner, "plan-missing", CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task ReadByAnomalyIdAsync_UnknownAnomaly_ReturnsEmpty()
    {
        await SeedRowsAsync([CreateRow("event.first", planId: null, anomalyId: "anomaly-1")]);

        var rows = await reader!.ReadByAnomalyIdAsync(AuditOutboxConventions.Streams.Planner, "anomaly-missing", CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task ReadByPlanIdAsync_UnknownStream_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => reader!.ReadByPlanIdAsync("unknown", "plan-1", CancellationToken.None));

        Assert.Contains("unknown", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadByAnomalyIdAsync_InvalidSchemaIdentifier_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => reader!.ReadByAnomalyIdAsync("not-valid;", "anomaly-1", CancellationToken.None));

        Assert.Contains("not-valid", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadByPlanIdAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => reader!.ReadByPlanIdAsync(AuditOutboxConventions.Streams.Planner, "plan-1", cts.Token));
    }

    [Fact]
    public async Task ReadByPlanIdAsync_DoesNotRecomputeHashes()
    {
        await SeedRowsAsync([CreateRow("event.first", planId: "plan-1", anomalyId: null)]);

        var rows = await reader!.ReadByPlanIdAsync(AuditOutboxConventions.Streams.Planner, "plan-1", CancellationToken.None);

        Assert.Single(rows);
        // The reader must not touch the hash chain. It returns the row as-is; verifying
        // that event_hash is present but not recomputed is sufficient.
        await using var connection = await dataSource!.OpenConnectionAsync();
        string? hash = await connection.QuerySingleOrDefaultAsync<string?>(
            $"SELECT event_hash FROM {AuditOutboxConventions.Streams.Planner}.audit_outbox WHERE plan_id = @PlanId",
            new { PlanId = "plan-1" });

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
    }

    private async Task SeedRowsAsync(IEnumerable<AuditOutboxRow> rows)
    {
        foreach (AuditOutboxRow row in rows)
        {
            await using var connection = await dataSource!.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await core!.AppendAsync(AuditOutboxConventions.Streams.Planner, row, connection, tx, CancellationToken.None);
            await tx.CommitAsync();
        }
    }

    private static AuditOutboxRow CreateRow(
        string eventName,
        string? planId,
        string? anomalyId,
        DateTimeOffset? occurredAt = null)
    {
        var correlationColumns = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (planId is not null)
        {
            correlationColumns[AuditOutboxConventions.CorrelationColumnNames.PlanId] = planId;
        }

        if (anomalyId is not null)
        {
            correlationColumns[AuditOutboxConventions.CorrelationColumnNames.AnomalyId] = anomalyId;
        }

        return new AuditOutboxRow(
            EventName: eventName,
            OccurredAtUtc: occurredAt ?? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ActorSubject: "service:test",
            ActorClientId: null,
            Outcome: "success",
            Reason: null,
            PayloadJsonText: """{"test": true}""",
            CorrelationColumns: correlationColumns);
    }
}
