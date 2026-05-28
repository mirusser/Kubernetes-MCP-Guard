using Dapper;
using InfraGate.AuditOutbox.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.AuditOutbox.Tests.IntegrationTests;

/// <summary>
/// Verifies the cross-stream forensic query: rows inserted into approvals, observer, and planner
/// streams can be reconstructed as a unified timeline joined by anomaly_id and plan_id.
/// </summary>
[Trait("Category", "Postgres")]
public sealed class CrossStreamForensicTests : IAsyncLifetime
{
    private static readonly string ApprovalsFixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "CrossStream", "Approvals");

    private static readonly string ObserverMigrationsDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "CrossStream", "Observer");

    private static readonly string PlannerMigrationsDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "CrossStream", "Planner");

    private const string AnomalyId = "anomaly-aaa111";
    private const string PlanId = "plan-bbb222";
    private const string CycleId = "cycle-ccc333";

    private PostgreSqlContainer? container;
    private NpgsqlDataSource? dataSource;
    private IAuditOutboxCore? core;

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder(TestContainersConstants.PostgresImage).Build();
        await container.StartAsync();

        dataSource = NpgsqlDataSource.Create(container.GetConnectionString());

        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            dataSource, AuditOutboxConventions.Streams.Approvals, ApprovalsFixturesDir, CancellationToken.None);
        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            dataSource, AuditOutboxConventions.Streams.Observer, ObserverMigrationsDir, CancellationToken.None);
        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            dataSource, AuditOutboxConventions.Streams.Planner, PlannerMigrationsDir, CancellationToken.None);

        var services = new ServiceCollection();
        services.AddPostgresAuditOutbox(dataSource);
        core = services.BuildServiceProvider().GetRequiredService<IAuditOutboxCore>();
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
    public async Task ForensicQuery_FullTimeline_ReconstructsInChronologicalOrder()
    {
        // Seed a representative observer → planner → approvals audit timeline.
        // Each row uses a fixed occurred_at_utc so the ORDER BY is deterministic.
        var t0 = new DateTimeOffset(2025, 6, 1, 10, 0, 0, TimeSpan.Zero);

        await AppendRowAsync(AuditOutboxConventions.Streams.Observer, BuildObserverRow(
            "anomaly.detected", t0, anomalyId: AnomalyId, cycleId: CycleId));

        await AppendRowAsync(AuditOutboxConventions.Streams.Planner, BuildPlannerRow(
            "handoff.received", t0.AddSeconds(1), anomalyId: AnomalyId));

        await AppendRowAsync(AuditOutboxConventions.Streams.Planner, BuildPlannerRow(
            "propose_plan.succeeded", t0.AddSeconds(2), anomalyId: AnomalyId, planId: PlanId));

        await AppendRowAsync(AuditOutboxConventions.Streams.Approvals, BuildApprovalsRow(
            "plan.created", t0.AddSeconds(3), planId: PlanId));

        await AppendRowAsync(AuditOutboxConventions.Streams.Approvals, BuildApprovalsRow(
            "challenge.created", t0.AddSeconds(4), planId: PlanId));

        await using var queryConn = await dataSource!.OpenConnectionAsync();
        var timeline = (await queryConn.QueryAsync<TimelineRow>(ForensicSql,
            new { AnomalyId, PlanId })).ToArray();

        Assert.Equal(5, timeline.Length);

        Assert.Equal("observer", timeline[0].Stream);
        Assert.Equal("anomaly.detected", timeline[0].EventName);

        Assert.Equal("planner", timeline[1].Stream);
        Assert.Equal("handoff.received", timeline[1].EventName);

        Assert.Equal("planner", timeline[2].Stream);
        Assert.Equal("propose_plan.succeeded", timeline[2].EventName);

        Assert.Equal("approvals", timeline[3].Stream);
        Assert.Equal("plan.created", timeline[3].EventName);

        Assert.Equal("approvals", timeline[4].Stream);
        Assert.Equal("challenge.created", timeline[4].EventName);
    }

    [Fact]
    public async Task ForensicQuery_EachStream_HasIndependentHashChain()
    {
        var t0 = new DateTimeOffset(2025, 6, 1, 11, 0, 0, TimeSpan.Zero);

        await AppendRowAsync(AuditOutboxConventions.Streams.Observer, BuildObserverRow(
            "anomaly.detected", t0, anomalyId: AnomalyId, cycleId: CycleId));
        await AppendRowAsync(AuditOutboxConventions.Streams.Observer, BuildObserverRow(
            "handoff.published", t0.AddSeconds(1), anomalyId: AnomalyId, cycleId: CycleId));

        await AppendRowAsync(AuditOutboxConventions.Streams.Planner, BuildPlannerRow(
            "handoff.received", t0.AddSeconds(2), anomalyId: AnomalyId));

        await using var queryConn = await dataSource!.OpenConnectionAsync();

        var observerRows = (await queryConn.QueryAsync<(string? PrevHash, string EventHash)>(
            "SELECT previous_event_hash, event_hash FROM observer.audit_outbox ORDER BY audit_sequence"))
            .ToArray();

        var plannerRows = (await queryConn.QueryAsync<(string? PrevHash, string EventHash)>(
            "SELECT previous_event_hash, event_hash FROM planner.audit_outbox ORDER BY audit_sequence"))
            .ToArray();

        // Observer chain is independent: row 1 has NULL prev_hash, row 2 chains to row 1.
        Assert.Null(observerRows[0].PrevHash);
        Assert.Equal(observerRows[0].EventHash, observerRows[1].PrevHash);

        // Planner chain starts fresh: its first row has NULL prev_hash, unrelated to observer.
        Assert.Null(plannerRows[0].PrevHash);
    }

    private async Task AppendRowAsync(string schema, AuditOutboxRow row)
    {
        await using var conn = await dataSource!.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await core!.AppendAsync(schema, row, conn, tx, CancellationToken.None);
        await tx.CommitAsync();
    }

    private static AuditOutboxRow BuildObserverRow(
        string eventName,
        DateTimeOffset occurredAt,
        string? anomalyId = null,
        string? cycleId = null) =>
        new(
            EventName: eventName,
            OccurredAtUtc: occurredAt,
            ActorSubject: "service:observer",
            ActorClientId: null,
            Outcome: null,
            Reason: null,
            PayloadJsonText: """{"source":"observer"}""",
            CorrelationColumns: new Dictionary<string, object?>
            {
                ["cycle_id"] = cycleId,
                ["anomaly_id"] = anomalyId,
                ["dedupe_key"] = anomalyId,
            });

    private static AuditOutboxRow BuildPlannerRow(
        string eventName,
        DateTimeOffset occurredAt,
        string? anomalyId = null,
        string? planId = null) =>
        new(
            EventName: eventName,
            OccurredAtUtc: occurredAt,
            ActorSubject: "service:planner",
            ActorClientId: null,
            Outcome: null,
            Reason: null,
            PayloadJsonText: """{"source":"planner"}""",
            CorrelationColumns: new Dictionary<string, object?>
            {
                ["proposal_id"] = null,
                ["anomaly_id"] = anomalyId,
                ["plan_id"] = planId,
            });

    private static AuditOutboxRow BuildApprovalsRow(
        string eventName,
        DateTimeOffset occurredAt,
        string? planId = null) =>
        new(
            EventName: eventName,
            OccurredAtUtc: occurredAt,
            ActorSubject: "service:gateway",
            ActorClientId: null,
            Outcome: null,
            Reason: null,
            PayloadJsonText: """{"source":"approvals"}""",
            CorrelationColumns: new Dictionary<string, object?>
            {
                ["plan_id"] = planId,
                ["challenge_id"] = null,
                ["grant_id"] = null,
                ["execution_attempt_id"] = null,
            });

    // Canonical cross-stream forensic query joining all three streams by anomaly_id / plan_id.
    // SQL aliases use PascalCase so Dapper's case-insensitive property mapping resolves cleanly.
    // occurred_at_utc is included for ORDER BY; it is also mapped by TimelineRow for completeness.
    // This query shape is documented in src/InfraGate.AuditOutbox.Postgres/README.md.
    private const string ForensicSql = """
        SELECT 'observer' AS Stream, event_name AS EventName, occurred_at_utc AS OccurredAtUtc
        FROM observer.audit_outbox
        WHERE anomaly_id = @AnomalyId
        UNION ALL
        SELECT 'planner', event_name, occurred_at_utc
        FROM planner.audit_outbox
        WHERE anomaly_id = @AnomalyId OR plan_id = @PlanId
        UNION ALL
        SELECT 'approvals', event_name, occurred_at_utc
        FROM approvals.audit_outbox
        WHERE plan_id = @PlanId
        ORDER BY OccurredAtUtc
        """;

    private sealed class TimelineRow
    {
        public string Stream { get; set; } = "";
        public string EventName { get; set; } = "";
        public DateTime OccurredAtUtc { get; set; }
    }
}
