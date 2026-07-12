using InfraGate.ApprovalUi;
using InfraGate.AuditOutbox;
using InfraGate.McpGateway.Audit;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class AuditTimelineAssemblerTests
{
    private static readonly DateTimeOffset T0 = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BuildTimelineAsync_KnownPlan_CombinesAndOrdersStreams()
    {
        var reader = new FakeAuditStreamReader();
        reader.SeedPlan(AuditOutboxConventions.Streams.Approvals, "plan-1",
        [
            new AuditStreamRow(1, CreateRow("challenge.created", T0.AddMinutes(10), outcome: null,
                correlation: new Dictionary<string, object?> { ["challenge_id"] = "ch-1" })),
            new AuditStreamRow(2, CreateRow("challenge.approved", T0.AddMinutes(20), outcome: "approved",
                correlation: new Dictionary<string, object?> { ["challenge_id"] = "ch-1", ["grant_id"] = "grant-1" })),
        ]);
        reader.SeedPlan(AuditOutboxConventions.Streams.Planner, "plan-1",
        [
            new AuditStreamRow(1, CreateRow("propose_plan.succeeded", T0.AddMinutes(5), outcome: "success",
                correlation: new Dictionary<string, object?> { ["anomaly_id"] = "anomaly-1", ["proposal_id"] = "prop-1" })),
        ]);
        reader.SeedAnomaly(AuditOutboxConventions.Streams.Observer, "anomaly-1",
        [
            new AuditStreamRow(1, CreateRow("anomaly.detected", T0, outcome: "detected",
                correlation: new Dictionary<string, object?> { ["cycle_id"] = "cycle-1" })),
        ]);

        var assembler = new AuditTimelineAssembler(reader);

        AuditTimelinePageData timeline = await assembler.BuildTimelineAsync("plan-1", CancellationToken.None);

        Assert.Equal("plan-1", timeline.PlanId);
        Assert.Equal("anomaly-1", timeline.AnomalyId);
        Assert.Equal(4, timeline.Entries.Count);
        Assert.Equal(AuditOutboxConventions.Streams.Observer, timeline.Entries[0].Stream);
        Assert.Equal(AuditOutboxConventions.Streams.Planner, timeline.Entries[1].Stream);
        Assert.Equal(AuditOutboxConventions.Streams.Approvals, timeline.Entries[2].Stream);
        Assert.Equal(AuditOutboxConventions.Streams.Approvals, timeline.Entries[3].Stream);
    }

    [Fact]
    public async Task BuildTimelineAsync_KnownPlan_ExtractsWhitelistedDisplayFields()
    {
        var reader = new FakeAuditStreamReader();
        reader.SeedPlan(AuditOutboxConventions.Streams.Planner, "plan-1",
        [
            new AuditStreamRow(1, CreateRow("propose_plan.succeeded", T0, outcome: "success",
                payload: """{"operation":"scale","namespace":"mcp-ns","message":"proposed","secret":"shh"}""",
                correlation: new Dictionary<string, object?> { ["anomaly_id"] = "anomaly-1" })),
        ]);
        reader.SeedAnomaly(AuditOutboxConventions.Streams.Observer, "anomaly-1", []);

        var assembler = new AuditTimelineAssembler(reader);

        AuditTimelinePageData timeline = await assembler.BuildTimelineAsync("plan-1", CancellationToken.None);

        Assert.Single(timeline.Entries);
        AuditTimelineEntry entry = timeline.Entries[0];
        Assert.Equal("scale", entry.DisplayFields["operation"]);
        Assert.Equal("mcp-ns", entry.DisplayFields["namespace"]);
        Assert.Equal("proposed", entry.DisplayFields["message"]);
        Assert.False(entry.DisplayFields.ContainsKey("secret"));
    }

    [Fact]
    public async Task BuildTimelineAsync_KnownPlan_DistinguishesOutcomes()
    {
        var reader = new FakeAuditStreamReader();
        reader.SeedPlan(AuditOutboxConventions.Streams.Approvals, "plan-1",
        [
            new AuditStreamRow(1, CreateRow("execution.blocked", T0, outcome: "blocked", reason: "policy.denied")),
        ]);
        reader.SeedPlan(AuditOutboxConventions.Streams.Planner, "plan-1", []);

        var assembler = new AuditTimelineAssembler(reader);

        AuditTimelinePageData timeline = await assembler.BuildTimelineAsync("plan-1", CancellationToken.None);

        Assert.Single(timeline.Entries);
        Assert.Equal("blocked", timeline.Entries[0].Outcome);
        Assert.Equal("policy.denied", timeline.Entries[0].Reason);
    }

    [Fact]
    public async Task BuildTimelineAsync_UnknownPlan_ReturnsEmptyEntries()
    {
        var reader = new FakeAuditStreamReader();
        var assembler = new AuditTimelineAssembler(reader);

        AuditTimelinePageData timeline = await assembler.BuildTimelineAsync("plan-missing", CancellationToken.None);

        Assert.Equal("plan-missing", timeline.PlanId);
        Assert.Null(timeline.AnomalyId);
        Assert.Empty(timeline.Entries);
    }

    [Fact]
    public async Task BuildTimelineAsync_PlanWithNoAnomaly_DoesNotQueryObserver()
    {
        var reader = new FakeAuditStreamReader();
        reader.SeedPlan(AuditOutboxConventions.Streams.Approvals, "plan-1",
        [
            new AuditStreamRow(1, CreateRow("challenge.created", T0)),
        ]);
        reader.SeedPlan(AuditOutboxConventions.Streams.Planner, "plan-1", []);

        var assembler = new AuditTimelineAssembler(reader);

        AuditTimelinePageData timeline = await assembler.BuildTimelineAsync("plan-1", CancellationToken.None);

        Assert.Single(timeline.Entries);
        Assert.Null(timeline.AnomalyId);
        Assert.Empty(reader.AnomalyQueries);
    }

    [Fact]
    public async Task BuildTimelineAsync_MultipleEventsSameTimestamp_SortsByStream()
    {
        var reader = new FakeAuditStreamReader();
        reader.SeedPlan(AuditOutboxConventions.Streams.Approvals, "plan-1",
        [
            new AuditStreamRow(1, CreateRow("challenge.created", T0)),
        ]);
        reader.SeedPlan(AuditOutboxConventions.Streams.Planner, "plan-1",
        [
            new AuditStreamRow(1, CreateRow("propose_plan.succeeded", T0,
                correlation: new Dictionary<string, object?> { ["anomaly_id"] = "anomaly-1" })),
        ]);
        reader.SeedAnomaly(AuditOutboxConventions.Streams.Observer, "anomaly-1", []);

        var assembler = new AuditTimelineAssembler(reader);

        AuditTimelinePageData timeline = await assembler.BuildTimelineAsync("plan-1", CancellationToken.None);

        Assert.Equal(2, timeline.Entries.Count);
        Assert.Equal(AuditOutboxConventions.Streams.Approvals, timeline.Entries[0].Stream);
        Assert.Equal(AuditOutboxConventions.Streams.Planner, timeline.Entries[1].Stream);
    }

    [Fact]
    public async Task BuildTimelineAsync_InvalidPlanId_ThrowsArgumentException()
    {
        var reader = new FakeAuditStreamReader();
        var assembler = new AuditTimelineAssembler(reader);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => assembler.BuildTimelineAsync("   ", CancellationToken.None));

        Assert.Equal("planId", ex.ParamName);
    }

    private static AuditOutboxRow CreateRow(
        string eventName,
        DateTimeOffset occurredAt,
        string? outcome = "success",
        string? reason = null,
        string payload = """{"test": true}""",
        IReadOnlyDictionary<string, object?>? correlation = null) =>
        new(
            eventName,
            occurredAt,
            ActorSubject: "service:test",
            ActorClientId: null,
            outcome,
            reason,
            payload,
            correlation ?? new Dictionary<string, object?>(StringComparer.Ordinal));

    private sealed class FakeAuditStreamReader : IAuditStreamReader
    {
        private readonly Dictionary<(string Stream, string PlanId), IReadOnlyList<AuditStreamRow>> planIndex = new();
        private readonly Dictionary<(string Stream, string AnomalyId), IReadOnlyList<AuditStreamRow>> anomalyIndex = new();

        public IReadOnlyList<(string Stream, string AnomalyId)> AnomalyQueries { get; } =
            new List<(string Stream, string AnomalyId)>();

        public void SeedPlan(string stream, string planId, IReadOnlyList<AuditStreamRow> rows) =>
            planIndex[(stream, planId)] = rows;

        public void SeedAnomaly(string stream, string anomalyId, IReadOnlyList<AuditStreamRow> rows) =>
            anomalyIndex[(stream, anomalyId)] = rows;

        public Task<IReadOnlyList<AuditStreamRow>> ReadByPlanIdAsync(
            string streamSchema,
            string planId,
            CancellationToken cancellationToken)
        {
            if (planIndex.TryGetValue((streamSchema, planId), out IReadOnlyList<AuditStreamRow>? rows))
            {
                return Task.FromResult(rows);
            }

            return Task.FromResult<IReadOnlyList<AuditStreamRow>>([]);
        }

        public Task<IReadOnlyList<AuditStreamRow>> ReadByAnomalyIdAsync(
            string streamSchema,
            string anomalyId,
            CancellationToken cancellationToken)
        {
            ((List<(string Stream, string AnomalyId)>)AnomalyQueries).Add((streamSchema, anomalyId));

            if (anomalyIndex.TryGetValue((streamSchema, anomalyId), out IReadOnlyList<AuditStreamRow>? rows))
            {
                return Task.FromResult(rows);
            }

            return Task.FromResult<IReadOnlyList<AuditStreamRow>>([]);
        }
    }
}
