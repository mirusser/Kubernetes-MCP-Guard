using InfraGate.Planner.Handoff;
using InfraGate.Remediation.Contracts;
using Microsoft.Extensions.Logging;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class LoggingRemediationProposalSinkTests
{
    [Fact]
    public async Task PublishAsync_WithProposals_LogsEachProposalAtInformationLevel()
    {
        var logger = new CapturingLogger<LoggingRemediationProposalSink>();
        var sink = new LoggingRemediationProposalSink(logger);
        var batch = new RemediationProposalBatch
        {
            CycleId = "cycle-1",
            EmittedAt = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero),
            Proposals =
            [
                new RemediationProposal
                {
                    PlanId = "plan-1",
                    AnomalyId = "anomaly-1",
                    ProposedAt = new DateTimeOffset(2026, 5, 25, 12, 1, 0, TimeSpan.Zero),
                },
                new RemediationProposal
                {
                    PlanId = "plan-2",
                    AnomalyId = "anomaly-2",
                    ProposedAt = new DateTimeOffset(2026, 5, 25, 12, 2, 0, TimeSpan.Zero),
                },
            ],
        };

        await sink.PublishAsync(batch, CancellationToken.None);

        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Information, entry.Level));
        Assert.Equal("plan-1", logger.Entries[0].Properties["PlanId"]);
        Assert.Equal("anomaly-1", logger.Entries[0].Properties["AnomalyId"]);
        Assert.Equal("cycle-1", logger.Entries[0].Properties["CycleId"]);
        Assert.Equal("plan-2", logger.Entries[1].Properties["PlanId"]);
    }

    [Fact]
    public async Task PublishAsync_EmptyBatch_DoesNotLog()
    {
        var logger = new CapturingLogger<LoggingRemediationProposalSink>();
        var sink = new LoggingRemediationProposalSink(logger);
        var batch = new RemediationProposalBatch
        {
            CycleId = "cycle-1",
            EmittedAt = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero),
            Proposals = [],
        };

        await sink.PublishAsync(batch, CancellationToken.None);

        Assert.Empty(logger.Entries);
    }
}
