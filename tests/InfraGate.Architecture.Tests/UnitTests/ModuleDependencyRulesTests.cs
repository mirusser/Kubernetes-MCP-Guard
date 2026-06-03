using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchArchitecture = ArchUnitNET.Domain.Architecture;
using SystemAssembly = System.Reflection.Assembly;

namespace InfraGate.Architecture.Tests.UnitTests;

#pragma warning disable CA1825, MA0005 // false positive: collection expressions with TheoryData<T> are not zero-length array allocations

public sealed class ModuleDependencyRulesTests
{
    private static readonly ArchArchitecture ArchitectureModel = new ArchLoader()
        .LoadAssemblies(
            Load(ArchitectureAssemblies.Approvals),
            Load(ArchitectureAssemblies.ApprovalsPostgres),
            Load(ArchitectureAssemblies.AuditOutbox),
            Load(ArchitectureAssemblies.AuditOutboxPostgres),
            Load(ArchitectureAssemblies.KubernetesAdapter),
            Load(ArchitectureAssemblies.McpGateway),
            Load(ArchitectureAssemblies.McpServer),
            Load(ArchitectureAssemblies.Observer),
            Load(ArchitectureAssemblies.Planner),
            Load(ArchitectureAssemblies.Executor),
            Load(ArchitectureAssemblies.Npgsql))
        .Build();

    public static TheoryData<string> GenericApprovalCoreForbiddenModules() =>
    [
        ArchitectureAssemblies.ApprovalsPostgres,
        ArchitectureAssemblies.KubernetesAdapter,
        ArchitectureAssemblies.McpGateway,
        ArchitectureAssemblies.McpServer,
        ArchitectureAssemblies.Observer,
        ArchitectureAssemblies.Planner,
        ArchitectureAssemblies.Executor,
        ArchitectureAssemblies.Npgsql,
    ];

    public static TheoryData<string> GenericAuditOutboxForbiddenModules() =>
    [
        ArchitectureAssemblies.Approvals,
        ArchitectureAssemblies.ApprovalsPostgres,
        ArchitectureAssemblies.AuditOutboxPostgres,
        ArchitectureAssemblies.KubernetesAdapter,
        ArchitectureAssemblies.McpGateway,
        ArchitectureAssemblies.McpServer,
        ArchitectureAssemblies.Observer,
        ArchitectureAssemblies.Planner,
        ArchitectureAssemblies.Executor,
    ];

    public static TheoryData<string> AgentAuditStreamModules() =>
    [
        ArchitectureAssemblies.Observer,
        ArchitectureAssemblies.Planner,
    ];

    [Theory]
    [MemberData(nameof(GenericApprovalCoreForbiddenModules))]
    public void GenericApprovalCore_RuntimeOrAdapterModule_HasNoDependency(string forbiddenAssembly)
    {
        Types()
            .That()
            .ResideInAssembly(ArchitectureAssemblies.Approvals)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(forbiddenAssembly))
            .Because("the Generic Approval Core must stay independent of runtime modules and domain adapters")
            .WithoutRequiringPositiveResults()
            .Check(ArchitectureModel);
    }

    [Theory]
    [MemberData(nameof(GenericAuditOutboxForbiddenModules))]
    public void GenericAuditOutbox_DomainOrRuntimeModule_HasNoDependency(string forbiddenAssembly)
    {
        Types()
            .That()
            .ResideInAssembly(ArchitectureAssemblies.AuditOutbox)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(forbiddenAssembly))
            .Because("the generic Audit Stream engine must not know approval, adapter, or runtime modules")
            .WithoutRequiringPositiveResults()
            .Check(ArchitectureModel);
    }

    [Theory]
    [MemberData(nameof(AgentAuditStreamModules))]
    public void AgentAuditStream_ApprovalAuthorityModule_HasNoDependency(string agentAssembly)
    {
        Types()
            .That()
            .ResideInAssembly(agentAssembly)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(ArchitectureAssemblies.Approvals))
            .Because("Observer and Planner audit streams are independent of the Approval Authority's Audit Spine")
            .WithoutRequiringPositiveResults()
            .Check(ArchitectureModel);
    }

    [Fact]
    public void KubernetesAdapter_GenericApprovalCore_DependsInOneDirection()
    {
        Types()
            .That()
            .ResideInAssembly(ArchitectureAssemblies.Approvals)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(ArchitectureAssemblies.KubernetesAdapter))
            .Because("the Generic Approval Core must not depend back on a Domain Adapter")
            .WithoutRequiringPositiveResults()
            .Check(ArchitectureModel);
    }

    private static SystemAssembly Load(string assemblyName) => SystemAssembly.Load(assemblyName);

    private static class ArchitectureAssemblies
    {
        public const string Approvals = "InfraGate.Approvals";
        public const string ApprovalsPostgres = "InfraGate.Approvals.Postgres";
        public const string AuditOutbox = "InfraGate.AuditOutbox";
        public const string AuditOutboxPostgres = "InfraGate.AuditOutbox.Postgres";
        public const string KubernetesAdapter = "InfraGate.KubernetesAdapter";
        public const string McpGateway = "InfraGate.McpGateway";
        public const string McpServer = "InfraGate.McpServer";
        public const string Observer = "InfraGate.Observer";
        public const string Planner = "InfraGate.Planner";
        public const string Executor = "InfraGate.Executor";
        public const string Npgsql = "Npgsql";
    }
}
