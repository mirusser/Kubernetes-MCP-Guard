using System.Reflection;
using InfraGate.Planner.Audit;

namespace InfraGate.Planner.Tests.UnitTests.Architecture;

public sealed class ProjectReferenceAssertionsTests
{
    private static readonly Assembly PlannerAssembly = typeof(PlannerAuditEvents).Assembly;

    [Fact]
    public void PlannerAssembly_References_InfraGateAuditOutbox()
    {
        var referenced = PlannerAssembly.GetReferencedAssemblies();

        var match = referenced.FirstOrDefault(name =>
            name.Name is not null &&
            name.Name.Equals("InfraGate.AuditOutbox", StringComparison.Ordinal));

        Assert.NotNull(match);
    }

    [Fact]
    public void PlannerAssembly_References_InfraGateAuditOutboxPostgres()
    {
        var referenced = PlannerAssembly.GetReferencedAssemblies();

        var match = referenced.FirstOrDefault(name =>
            name.Name is not null &&
            name.Name.Equals("InfraGate.AuditOutbox.Postgres", StringComparison.Ordinal));

        Assert.NotNull(match);
    }

    [Fact]
    public void PlannerAssembly_DoesNotReference_InfraGateApprovals()
    {
        var referenced = PlannerAssembly.GetReferencedAssemblies();

        var match = referenced.FirstOrDefault(name =>
            name.Name is not null &&
            name.Name.Equals("InfraGate.Approvals", StringComparison.Ordinal));

        Assert.Null(match);
    }

    [Fact]
    public void PlannerAssembly_DoesNotReference_InfraGateApprovalsPostgres()
    {
        var referenced = PlannerAssembly.GetReferencedAssemblies();

        var match = referenced.FirstOrDefault(name =>
            name.Name is not null &&
            name.Name.Equals("InfraGate.Approvals.Postgres", StringComparison.Ordinal));

        Assert.Null(match);
    }
}
