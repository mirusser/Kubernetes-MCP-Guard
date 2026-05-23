using System.Text.Json;
using InfraGate.Observer.Contracts;

namespace InfraGate.Observer.Contracts.Tests.UnitTests;

public sealed class AnomalyReportTests
{
    [Fact]
    public void AnomalyReport_HasRequiredFields()
    {
        var now = DateTimeOffset.UtcNow;
        var report = new AnomalyReport(
            AnomalyId: "anomaly-123",
            CycleId: Guid.NewGuid(),
            DetectedAt: now,
            Kind: AnomalyKind.PodUnhealthy,
            Target: new ResourceRef("v1", "Pod", "default", "my-pod"),
            Severity: Severity.High,
            Status: AnomalyStatus.Active,
            Summary: "Pod is in CrashLoopBackOff",
            Evidence: [],
            Suggested: null,
            Annotations: new Dictionary<string, string>());

        Assert.Equal("anomaly-123", report.AnomalyId);
        Assert.Equal(AnomalyKind.PodUnhealthy, report.Kind);
        Assert.Equal(Severity.High, report.Severity);
        Assert.Equal(AnomalyStatus.Active, report.Status);
    }

    [Fact]
    public void AnomalyReport_SerializesAndDeserializesViaSystemTextJson()
    {
        var report = new AnomalyReport(
            AnomalyId: "anomaly-123",
            CycleId: Guid.Empty,
            DetectedAt: DateTimeOffset.MinValue,
            Kind: AnomalyKind.DeploymentUnavailable,
            Target: new ResourceRef("apps/v1", "Deployment", "default", "my-deploy"),
            Severity: Severity.Medium,
            Status: AnomalyStatus.Resolved,
            Summary: "Deployment has 0 ready replicas",
            Evidence:
            [
                new EvidenceItem("status", "json", """{"availableReplicas":0}""")
            ],
            Suggested: new RemediationHint("Check deployment rollout status"),
            Annotations: new Dictionary<string, string> { { "key", "value" } });

        var json = JsonSerializer.Serialize(report);
        var deserialized = JsonSerializer.Deserialize<AnomalyReport>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(report.AnomalyId, deserialized.AnomalyId);
        Assert.Equal(report.Kind, deserialized.Kind);
        Assert.Equal(report.Severity, deserialized.Severity);
        Assert.Equal(report.Target, deserialized.Target);
        Assert.Equal(report.Suggested?.Text, deserialized.Suggested?.Text);
    }

    [Fact]
    public void AnomalyReport_EvidenceIsReadOnly()
    {
        var report = new AnomalyReport(
            AnomalyId: "id",
            CycleId: Guid.NewGuid(),
            DetectedAt: DateTimeOffset.UtcNow,
            Kind: AnomalyKind.WarningEvent,
            Target: new ResourceRef("v1", "Event", "default", "evt-1"),
            Severity: Severity.Low,
            Status: AnomalyStatus.Active,
            Summary: "Warning event detected",
            Evidence: [new EvidenceItem("events", "json", "[...]")],
            Suggested: null,
            Annotations: new Dictionary<string, string>());

        Assert.IsAssignableFrom<IReadOnlyList<EvidenceItem>>(report.Evidence);
    }

    [Fact]
    public void AnomalyReport_AnnotationsIsReadOnly()
    {
        var report = new AnomalyReport(
            AnomalyId: "id",
            CycleId: Guid.NewGuid(),
            DetectedAt: DateTimeOffset.UtcNow,
            Kind: AnomalyKind.WarningEvent,
            Target: new ResourceRef("v1", "Event", "default", "evt-1"),
            Severity: Severity.Low,
            Status: AnomalyStatus.Active,
            Summary: "Warning event detected",
            Evidence: [],
            Suggested: null,
            Annotations: new Dictionary<string, string>());

        Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(report.Annotations);
    }
}

public sealed class AnomalyHandoffBatchTests
{
    [Fact]
    public void Batch_HasCycleIdAndReports()
    {
        var cycleId = Guid.NewGuid();
        var batch = new AnomalyHandoffBatch(
            CycleId: cycleId,
            Reports:
            [
                new AnomalyReport(
                    AnomalyId: "a1",
                    CycleId: cycleId,
                    DetectedAt: DateTimeOffset.UtcNow,
                    Kind: AnomalyKind.PodUnhealthy,
                    Target: new ResourceRef("v1", "Pod", "default", "p1"),
                    Severity: Severity.High,
                    Status: AnomalyStatus.Active,
                    Summary: "test",
                    Evidence: [],
                    Suggested: null,
                    Annotations: new Dictionary<string, string>())
            ]);

        Assert.Equal(cycleId, batch.CycleId);
        Assert.Single(batch.Reports);
    }

    [Fact]
    public void Batch_ReportsIsReadOnly()
    {
        var batch = new AnomalyHandoffBatch(Guid.NewGuid(), []);

        Assert.IsAssignableFrom<IReadOnlyList<AnomalyReport>>(batch.Reports);
    }
}

public sealed class EnumTests
{
    [Fact]
    public void AnomalyKind_HasFourValues()
    {
        var values = Enum.GetValues<AnomalyKind>();
        Assert.Equal(4, values.Length);
        Assert.Contains(AnomalyKind.PodUnhealthy, values);
        Assert.Contains(AnomalyKind.DeploymentUnavailable, values);
        Assert.Contains(AnomalyKind.ServiceNoEndpoints, values);
        Assert.Contains(AnomalyKind.WarningEvent, values);
    }

    [Fact]
    public void AnomalyStatus_HasActiveAndResolvedOnly()
    {
        var values = Enum.GetValues<AnomalyStatus>();
        Assert.Equal(2, values.Length);
        Assert.Contains(AnomalyStatus.Active, values);
        Assert.Contains(AnomalyStatus.Resolved, values);
    }

    [Fact]
    public void Severity_HasThreeLevels()
    {
        var values = Enum.GetValues<Severity>();
        Assert.Equal(3, values.Length);
        Assert.Contains(Severity.High, values);
        Assert.Contains(Severity.Medium, values);
        Assert.Contains(Severity.Low, values);
    }
}

public sealed class ResourceRefTests
{
    [Fact]
    public void ResourceRef_Equality()
    {
        var a = new ResourceRef("v1", "Pod", "default", "my-pod");
        var b = new ResourceRef("v1", "Pod", "default", "my-pod");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ResourceRef_Inequality()
    {
        var a = new ResourceRef("v1", "Pod", "default", "my-pod");
        var b = new ResourceRef("apps/v1", "Deployment", "default", "my-deploy");

        Assert.NotEqual(a, b);
    }
}

public sealed class EvidenceItemTests
{
    [Fact]
    public void EvidenceItem_HasRequiredFields()
    {
        var item = new EvidenceItem("status", "json", "{}");

        Assert.Equal("status", item.Category);
        Assert.Equal("json", item.Format);
        Assert.Equal("{}", item.Content);
    }

    [Fact]
    public void EvidenceItem_SerializesAndDeserializes()
    {
        var item = new EvidenceItem("events", "json", "[{\"reason\":\"BackOff\"}]");
        var json = JsonSerializer.Serialize(item);
        var deserialized = JsonSerializer.Deserialize<EvidenceItem>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(item.Category, deserialized.Category);
        Assert.Equal(item.Content, deserialized.Content);
    }
}

public sealed class IAnomalyHandoffSinkTests
{
    [Fact]
    public async Task PublishAsync_CanBeImplemented()
    {
        var sink = new FakeSink();
        var batch = new AnomalyHandoffBatch(Guid.NewGuid(), []);

        await sink.PublishAsync(batch, CancellationToken.None);

        Assert.True(sink.WasCalled);
    }

    private sealed class FakeSink : IAnomalyHandoffSink
    {
        public bool WasCalled { get; private set; }

        public Task PublishAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }
}
