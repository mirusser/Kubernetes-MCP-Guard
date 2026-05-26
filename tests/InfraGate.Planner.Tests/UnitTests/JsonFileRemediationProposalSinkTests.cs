using System.Text.Json;
using InfraGate.Planner.Handoff;
using InfraGate.Remediation.Contracts;
using Microsoft.Extensions.Logging;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class JsonFileRemediationProposalSinkTests
{
    [Fact]
    public async Task PublishAsync_WritesFileAtomicallyAndDeserializesCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"planner-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var sink = new JsonFileRemediationProposalSink(
                tempDir, Substitute.For<ILogger<JsonFileRemediationProposalSink>>());
            var batch = CreateBatch("cycle-001");

            await sink.PublishAsync(batch, CancellationToken.None);

            string expectedPath = Path.Combine(tempDir, "cycle-001.json");
            Assert.True(File.Exists(expectedPath));

            string json = await File.ReadAllTextAsync(expectedPath);
            var deserialized = JsonSerializer.Deserialize<RemediationProposalBatch>(
                json, JsonFileRemediationProposalSink.SerializerOptions);

            Assert.NotNull(deserialized);
            Assert.Equal(batch.CycleId, deserialized.CycleId);
            Assert.Single(deserialized.Proposals);
            Assert.Equal("plan-1", deserialized.Proposals[0].PlanId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PublishAsync_CreatesDirectoryIfMissing()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"planner-test-{Guid.NewGuid():N}");
        string tempDir = Path.Combine(tempRoot, "subdir");

        try
        {
            var sink = new JsonFileRemediationProposalSink(
                tempDir, Substitute.For<ILogger<JsonFileRemediationProposalSink>>());

            await sink.PublishAsync(CreateBatch("cycle-001"), CancellationToken.None);

            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PublishAsync_EmptyBatch_DoesNotWriteFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"planner-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var sink = new JsonFileRemediationProposalSink(
                tempDir, Substitute.For<ILogger<JsonFileRemediationProposalSink>>());
            var batch = new RemediationProposalBatch
            {
                CycleId = "cycle-empty",
                EmittedAt = DateTimeOffset.UtcNow,
                Proposals = [],
            };

            await sink.PublishAsync(batch, CancellationToken.None);

            Assert.False(File.Exists(Path.Combine(tempDir, "cycle-empty.json")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PublishAsync_LeavesNoTmpFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"planner-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var sink = new JsonFileRemediationProposalSink(
                tempDir, Substitute.For<ILogger<JsonFileRemediationProposalSink>>());

            await sink.PublishAsync(CreateBatch("cycle-002"), CancellationToken.None);

            Assert.Empty(Directory.GetFiles(tempDir, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static RemediationProposalBatch CreateBatch(string cycleId) => new()
    {
        CycleId = cycleId,
        EmittedAt = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero),
        Proposals = [new RemediationProposal
        {
            PlanId = "plan-1",
            AnomalyId = "anomaly-1",
            ProposedAt = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero),
        }],
    };
}
