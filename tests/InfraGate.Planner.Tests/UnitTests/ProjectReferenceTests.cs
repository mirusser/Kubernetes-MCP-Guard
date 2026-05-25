using System.Reflection;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void PlannerAssembly_DoesNotReference_InfraGateApprovals()
    {
        var plannerAssembly = typeof(PlannerOptions).Assembly;
        var referenced = plannerAssembly.GetReferencedAssemblies();

        var approvalsRef = referenced.FirstOrDefault(name =>
            name.Name is not null &&
            name.Name.StartsWith("InfraGate.Approvals", StringComparison.Ordinal));

        Assert.Null(approvalsRef);
    }
}
