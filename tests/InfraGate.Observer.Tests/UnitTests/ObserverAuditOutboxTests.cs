using InfraGate.AuditOutbox;
using InfraGate.Observer.Audit;
using Npgsql;
using NSubstitute;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserverAuditOutboxTests
{
    [Fact]
    public async Task AppendAsync_PrimaryOverload_WithNullEntry_ThrowsArgumentNullException()
    {
        var core = Substitute.For<IAuditOutboxCore>();
        var dataSource = NpgsqlDataSource.Create("Host=localhost");
        var outbox = new ObserverAuditOutbox(core, dataSource);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            outbox.AppendAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task AppendAsync_TransactionOverload_WithNullEntry_ThrowsArgumentNullException()
    {
        var core = Substitute.For<IAuditOutboxCore>();
        var dataSource = NpgsqlDataSource.Create("Host=localhost");
        var outbox = new ObserverAuditOutbox(core, dataSource);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            outbox.AppendAsync(null!, null!, null!, CancellationToken.None));
    }

    [Fact]
    public async Task AppendAsync_TransactionOverload_ExtractsCorrelationColumnsToCanonicalFormat()
    {
        var core = Substitute.For<IAuditOutboxCore>();
        var dataSource = NpgsqlDataSource.Create("Host=localhost");
        var outbox = new ObserverAuditOutbox(core, dataSource);

        var entry = new ObserverAuditEntry(
            EventName: "test.event",
            ActorSubject: "sub",
            ActorClientId: "client",
            Outcome: "success",
            Reason: "test reason",
            Payload: new Dictionary<string, object> { ["key"] = "value" },
            CycleId: "cycle-123",
            AnomalyId: "anomaly-456",
            DedupeKey: "dedupe-789");

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
        Assert.Equal("cycle-123", cols["cycle_id"]);
        Assert.Equal("anomaly-456", cols["anomaly_id"]);
        Assert.Equal("dedupe-789", cols["dedupe_key"]);
    }
}
