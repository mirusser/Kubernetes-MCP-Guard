using System.Reflection;
using InfraGate.Executor;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void ExecutorAssembly_DoesNotReference_InfraGateApprovals()
    {
        var executorAssembly = typeof(ExecutorOptions).Assembly;
        var referenced = executorAssembly.GetReferencedAssemblies();

        var approvalsRef = referenced.FirstOrDefault(name =>
            name.Name is not null &&
            name.Name.StartsWith("InfraGate.Approvals", StringComparison.Ordinal));

        Assert.Null(approvalsRef);
    }

    [Fact]
    public void ExecutorAssembly_DoesNotReference_InfraGateObserverContracts()
    {
        var executorAssembly = typeof(ExecutorOptions).Assembly;
        var referenced = executorAssembly.GetReferencedAssemblies();

        var observerContractsRef = referenced.FirstOrDefault(name =>
            name.Name is not null &&
            name.Name.Equals("InfraGate.Observer.Contracts", StringComparison.Ordinal));

        Assert.Null(observerContractsRef);
    }
}
