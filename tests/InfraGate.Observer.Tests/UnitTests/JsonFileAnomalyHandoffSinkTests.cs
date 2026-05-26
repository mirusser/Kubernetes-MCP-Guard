namespace InfraGate.Observer.Tests.UnitTests;

public sealed class JsonFileAnomalyHandoffSinkTests
{
    [Fact]
    public async Task PublishAsync_WritesFileAtomicallyAndDeserializesCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"observer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var sink = new JsonFileAnomalyHandoffSink(tempDir, Substitute.For<ILogger<JsonFileAnomalyHandoffSink>>());

            var batch = new AnomalyHandoffBatch
            {
                CycleId = "cycle-001",
                EmittedAt = new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero),
                Reports =
                [
                    new AnomalyReport
                    {
                        AnomalyId = "anomaly-123",
                        CycleId = "cycle-001",
                        DetectedAt = new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero),
                        Kind = AnomalyKind.PodUnhealthy,
                        Target = new ResourceRef { ApiVersion = "v1", Kind = "Pod", Namespace = "default", Name = "crashing-pod" },
                        Severity = Severity.High,
                        Status = AnomalyStatus.Active,
                        Summary = "Pod is crash-looping",
                        Evidence = [],
                        Annotations = new Dictionary<string, string>(),
                    },
                ],
            };

            await sink.PublishAsync(batch, CancellationToken.None);

            var expectedPath = Path.Combine(tempDir, "cycle-001.json");
            Assert.True(File.Exists(expectedPath));

            var json = await File.ReadAllTextAsync(expectedPath);
            var deserialized = JsonSerializer.Deserialize<AnomalyHandoffBatch>(json, JsonFileAnomalyHandoffSink.SerializerOptions);

            Assert.NotNull(deserialized);
            Assert.Equal(batch.CycleId, deserialized.CycleId);
            Assert.Equal(batch.EmittedAt, deserialized.EmittedAt);
            Assert.Equal(batch.Reports.Count, deserialized.Reports.Count);
            Assert.Equal(batch.Reports[0].AnomalyId, deserialized.Reports[0].AnomalyId);
            Assert.Equal(batch.Reports[0].Kind, deserialized.Reports[0].Kind);
            Assert.Equal(batch.Reports[0].Target.Name, deserialized.Reports[0].Target.Name);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PublishAsync_CreatesDirectoryIfMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"observer-test-{Guid.NewGuid():N}");
        var tempDir = Path.Combine(tempRoot, "subdir");

        try
        {
            var sink = new JsonFileAnomalyHandoffSink(tempDir, Substitute.For<ILogger<JsonFileAnomalyHandoffSink>>());
            var batch = new AnomalyHandoffBatch
            {
                CycleId = "cycle-001",
                EmittedAt = DateTimeOffset.UtcNow,
                Reports =
                [
                    new AnomalyReport
                    {
                        AnomalyId = "anomaly-1",
                        CycleId = "cycle-001",
                        DetectedAt = DateTimeOffset.UtcNow,
                        Kind = AnomalyKind.PodUnhealthy,
                        Target = new ResourceRef { ApiVersion = "v1", Kind = "Pod", Namespace = "default", Name = "pod" },
                        Severity = Severity.Low,
                        Status = AnomalyStatus.Active,
                        Summary = "summary",
                        Evidence = [],
                        Annotations = new Dictionary<string, string>(),
                    },
                ],
            };

            await sink.PublishAsync(batch, CancellationToken.None);

            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PublishAsync_DoesNotWriteForEmptyBatch()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"observer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var sink = new JsonFileAnomalyHandoffSink(tempDir, Substitute.For<ILogger<JsonFileAnomalyHandoffSink>>());
            var batch = new AnomalyHandoffBatch
            {
                CycleId = "cycle-001",
                EmittedAt = DateTimeOffset.UtcNow,
                Reports = [],
            };

            await sink.PublishAsync(batch, CancellationToken.None);

            var expectedPath = Path.Combine(tempDir, "cycle-001.json");
            Assert.False(File.Exists(expectedPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
