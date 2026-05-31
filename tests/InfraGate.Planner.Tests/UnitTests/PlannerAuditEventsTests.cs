using InfraGate.AuditOutbox;
using InfraGate.Planner.Audit;
using Npgsql;
using NSubstitute;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerAuditEventsTests
{
    [Fact]
    public void HandoffReceived_HasExpectedValue() =>
        Assert.Equal("handoff.received", PlannerAuditEvents.HandoffReceived);

    [Fact]
    public void ProposalSkipped_HasExpectedValue() =>
        Assert.Equal("proposal.skipped", PlannerAuditEvents.ProposalSkipped);

    [Fact]
    public void ProposePlanSucceeded_HasExpectedValue() =>
        Assert.Equal("propose_plan.succeeded", PlannerAuditEvents.ProposePlanSucceeded);

    [Fact]
    public void ProposePlanFailed_HasExpectedValue() =>
        Assert.Equal("propose_plan.failed", PlannerAuditEvents.ProposePlanFailed);

    [Fact]
    public void AllEventNames_AreDistinct()
    {
        var names = new[]
        {
            PlannerAuditEvents.HandoffReceived,
            PlannerAuditEvents.ProposalSkipped,
            PlannerAuditEvents.ProposePlanSucceeded,
            PlannerAuditEvents.ProposePlanFailed,
        };

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }
}
