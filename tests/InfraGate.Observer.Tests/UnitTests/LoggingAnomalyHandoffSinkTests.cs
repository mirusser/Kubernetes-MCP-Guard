using InfraGate.Observer.Handoff;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class LoggingAnomalyHandoffSinkTests
{
    [Fact]
    public async Task PublishAsync_WithReports_LogsEachReportAtInformationLevel()
    {
        var logger = new CapturingLogger<LoggingAnomalyHandoffSink>();
        var sink = new LoggingAnomalyHandoffSink(logger);

        var batch = new AnomalyHandoffBatch
        {
            CycleId = "cycle-001",
            EmittedAt = DateTimeOffset.UtcNow,
            Reports =
            [
                new AnomalyReport
                {
                    AnomalyId = "anomaly-123",
                    CycleId = "cycle-001",
                    DetectedAt = DateTimeOffset.UtcNow,
                    Kind = AnomalyKind.PodUnhealthy,
                    Target = new ResourceRef { ApiVersion = "v1", Kind = "Pod", Namespace = "default", Name = "crashing-pod" },
                    Severity = Severity.High,
                    Status = AnomalyStatus.Active,
                    Summary = "Pod is crash-looping",
                    Evidence = [],
                    Annotations = new Dictionary<string, string>(),
                },
                new AnomalyReport
                {
                    AnomalyId = "anomaly-456",
                    CycleId = "cycle-001",
                    DetectedAt = DateTimeOffset.UtcNow,
                    Kind = AnomalyKind.DeploymentUnavailable,
                    Target = new ResourceRef { ApiVersion = "apps/v1", Kind = "Deployment", Namespace = "default", Name = "nginx" },
                    Severity = Severity.Medium,
                    Status = AnomalyStatus.Active,
                    Summary = "Deployment has no ready pods",
                    Evidence = [],
                    Annotations = new Dictionary<string, string>(),
                },
            ],
        };

        await sink.PublishAsync(batch, CancellationToken.None);

        Assert.Equal(2, logger.Entries.Count);

        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Information, entry.Level));

        // Entry 0: PodUnhealthy / High
        var props0 = logger.Entries[0].Properties;
        Assert.Equal("cycle-001", props0["CycleId"]);
        Assert.Equal("anomaly-123", props0["AnomalyId"]);
        Assert.Equal("PodUnhealthy", props0["Kind"]);
        Assert.Equal("High", props0["Severity"]);
        Assert.Equal("Active", props0["Status"]);
        Assert.Equal("Pod/crashing-pod", props0["Target"]);
        Assert.Equal("Pod is crash-looping", props0["Summary"]);

        // Entry 1: DeploymentUnavailable / Medium
        var props1 = logger.Entries[1].Properties;
        Assert.Equal("cycle-001", props1["CycleId"]);
        Assert.Equal("anomaly-456", props1["AnomalyId"]);
        Assert.Equal("DeploymentUnavailable", props1["Kind"]);
        Assert.Equal("Medium", props1["Severity"]);
        Assert.Equal("Active", props1["Status"]);
        Assert.Equal("Deployment/nginx", props1["Target"]);
        Assert.Equal("Deployment has no ready pods", props1["Summary"]);
    }

    [Fact]
    public async Task PublishAsync_EmptyBatch_DoesNotLog()
    {
        var logger = new CapturingLogger<LoggingAnomalyHandoffSink>();
        var sink = new LoggingAnomalyHandoffSink(logger);

        var batch = new AnomalyHandoffBatch
        {
            CycleId = "cycle-001",
            EmittedAt = DateTimeOffset.UtcNow,
            Reports = [],
        };

        await sink.PublishAsync(batch, CancellationToken.None);

        Assert.Empty(logger.Entries);
    }
}
