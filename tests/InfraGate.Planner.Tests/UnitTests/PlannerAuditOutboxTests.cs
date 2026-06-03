using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using InfraGate.Planner.Audit;
using Npgsql;
using NSubstitute;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerAuditOutboxTests
{
    [Fact]
    public async Task AppendAsync_PrimaryOverload_WithNullEntry_ThrowsArgumentNullException()
    {
        var core = Substitute.For<IPostgresAuditOutboxCore>();
        var dataSource = NpgsqlDataSource.Create("Host=localhost");
        var outbox = new PlannerAuditOutbox(core, dataSource);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            outbox.AppendAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task AppendAsync_TransactionOverload_WithNullEntry_ThrowsArgumentNullException()
    {
        var core = Substitute.For<IPostgresAuditOutboxCore>();
        var dataSource = NpgsqlDataSource.Create("Host=localhost");
        var outbox = new PlannerAuditOutbox(core, dataSource);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            outbox.AppendAsync(null!, null!, null!, CancellationToken.None));
    }

    [Fact]
    public async Task AppendAsync_TransactionOverload_ExtractsCorrelationColumnsToCanonicalFormat()
    {
        var core = Substitute.For<IPostgresAuditOutboxCore>();
        var dataSource = NpgsqlDataSource.Create("Host=localhost");
        var outbox = new PlannerAuditOutbox(core, dataSource);

        var entry = new PlannerAuditEntry(
            EventName: "test.event",
            ActorSubject: "sub",
            ActorClientId: "client",
            Outcome: "success",
            Reason: "test reason",
            Payload: new Dictionary<string, object> { ["key"] = "value" },
            ProposalId: "prop-123",
            AnomalyId: "anomaly-456",
            PlanId: "plan-789");

        AuditOutboxRow? capturedRow = null;
        core.AppendAsync(
            Arg.Any<string>(),
            Arg.Do<AuditOutboxRow>(row => capturedRow = row),
            Arg.Any<NpgsqlConnection>(),
            Arg.Any<NpgsqlTransaction>(),
            Arg.Any<CancellationToken>())
            .Returns(42L);

        long sequence = await outbox.AppendAsync(entry, null!, null!, CancellationToken.None);

        Assert.Equal(42L, sequence);
        Assert.NotNull(capturedRow);

        Assert.Equal(entry.EventName, capturedRow.EventName);
        Assert.Equal(entry.ActorSubject, capturedRow.ActorSubject);
        Assert.Equal(entry.ActorClientId, capturedRow.ActorClientId);
        Assert.Equal(entry.Outcome, capturedRow.Outcome);
        Assert.Equal(entry.Reason, capturedRow.Reason);

        var cols = capturedRow.CorrelationColumns;
        Assert.NotNull(cols);
        Assert.Equal("prop-123", cols["proposal_id"]);
        Assert.Equal("anomaly-456", cols["anomaly_id"]);
        Assert.Equal("plan-789", cols["plan_id"]);
    }
}
