using InfraGate.AuditOutbox;
using InfraGate.Observer.Audit;
using Npgsql;
using NSubstitute;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserverAuditEventsTests
{
    [Fact]
    public void AnomalyDetected_HasExpectedValue() =>
        Assert.Equal("anomaly.detected", ObserverAuditEvents.AnomalyDetected);

    [Fact]
    public void AnomalySuppressed_HasExpectedValue() =>
        Assert.Equal("anomaly.suppressed", ObserverAuditEvents.AnomalySuppressed);

    [Fact]
    public void AnomalyResolved_HasExpectedValue() =>
        Assert.Equal("anomaly.resolved", ObserverAuditEvents.AnomalyResolved);

    [Fact]
    public void HandoffPublished_HasExpectedValue() =>
        Assert.Equal("handoff.published", ObserverAuditEvents.HandoffPublished);

    [Fact]
    public void HandoffFailed_HasExpectedValue() =>
        Assert.Equal("handoff.failed", ObserverAuditEvents.HandoffFailed);

    [Fact]
    public void AllEventNames_AreDistinct()
    {
        var names = new[]
        {
            ObserverAuditEvents.AnomalyDetected,
            ObserverAuditEvents.AnomalySuppressed,
            ObserverAuditEvents.AnomalyResolved,
            ObserverAuditEvents.HandoffPublished,
            ObserverAuditEvents.HandoffFailed,
        };

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ObserverAuditOutbox_AppendNullEntry_ThrowsArgumentNullException()
    {
        var core = Substitute.For<IAuditOutboxCore>();
        var dataSource = NpgsqlDataSource.Create("Host=localhost");
        var outbox = new ObserverAuditOutbox(core, dataSource);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            outbox.AppendAsync(null!, CancellationToken.None));
    }
}
