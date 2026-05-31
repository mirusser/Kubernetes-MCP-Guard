using System.Reflection;
using InfraGate.Observer.Audit;

namespace InfraGate.Observer.Tests.UnitTests.Architecture;

public sealed class ProjectReferenceAssertionsTests
{
    private static readonly Assembly ObserverAssembly = typeof(ObserverAuditEvents).Assembly;

    [Fact]
    public void ObserverAssembly_References_InfraGateAuditOutbox()
    {
        var referenced = ObserverAssembly.GetReferencedAssemblies();

        var match = referenced.FirstOrDefault(name =>
            name.Name is not null &&
            name.Name.Equals("InfraGate.AuditOutbox", StringComparison.Ordinal));

        Assert.NotNull(match);
    }

    [Fact]
    public void ObserverAssembly_References_InfraGateAuditOutboxPostgres()
    {
        var referenced = ObserverAssembly.GetReferencedAssemblies();

        var match = referenced.FirstOrDefault(name =>
            name.Name is not null &&
            name.Name.Equals("InfraGate.AuditOutbox.Postgres", StringComparison.Ordinal));

        Assert.NotNull(match);
    }

    [Fact]
    public void ObserverAssembly_DoesNotReference_InfraGateApprovals()
    {
        var referenced = ObserverAssembly.GetReferencedAssemblies();

        var match = referenced.FirstOrDefault(name =>
            name.Name is not null &&
            name.Name.Equals("InfraGate.Approvals", StringComparison.Ordinal));

        Assert.Null(match);
    }

    [Fact]
    public void ObserverAssembly_DoesNotReference_InfraGateApprovalsPostgres()
    {
        var referenced = ObserverAssembly.GetReferencedAssemblies();

        var match = referenced.FirstOrDefault(name =>
            name.Name is not null &&
            name.Name.Equals("InfraGate.Approvals.Postgres", StringComparison.Ordinal));

        Assert.Null(match);
    }
}
