using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.RuntimeSafety.Tests.UnitTests;

public sealed class InfraGateEnvironmentVariablesConfigurationProviderTests
{
    [Fact]
    public void ResolveMappedKeys_MapsFlatEnvVarToHierarchyKey()
    {
        using var environment = EnvironmentVariableScope.Set(
            ("INFRA_GATE_DOWNSTREAM_ASSEMBLY", "/app/server.dll"));

        var configuration = new ConfigurationBuilder()
            .AddInfraGateEnvironmentVariables(mappings =>
            {
                mappings.Map("INFRA_GATE_DOWNSTREAM_ASSEMBLY", "InfraGate:Gateway:DownstreamAssembly");
            })
            .Build();

        Assert.Equal("/app/server.dll", configuration["InfraGate:Gateway:DownstreamAssembly"]);
    }

    [Fact]
    public void ResolveMappedKeys_SkipsUnsetEnvVar()
    {
        using var environment = EnvironmentVariableScope.Set(
            ("INFRA_GATE_DOWNSTREAM_ASSEMBLY", (string?)null));

        var configuration = new ConfigurationBuilder()
            .AddInfraGateEnvironmentVariables(mappings =>
            {
                mappings.Map("INFRA_GATE_DOWNSTREAM_ASSEMBLY", "InfraGate:Gateway:DownstreamAssembly");
            })
            .Build();

        Assert.Null(configuration["InfraGate:Gateway:DownstreamAssembly"]);
    }

    [Fact]
    public void ResolveMappedKeys_MultipleMappingsAllResolved()
    {
        using var environment = EnvironmentVariableScope.Set(
            ("INFRA_GATE_DOWNSTREAM_ASSEMBLY", "/app/server.dll"),
            ("KUBECONFIG", "/kube/config"));

        var configuration = new ConfigurationBuilder()
            .AddInfraGateEnvironmentVariables(mappings =>
            {
                mappings.Map("INFRA_GATE_DOWNSTREAM_ASSEMBLY", "InfraGate:Gateway:DownstreamAssembly");
                mappings.Map("KUBECONFIG", "InfraGate:Kubernetes:KubeConfig");
            })
            .Build();

        Assert.Equal("/app/server.dll", configuration["InfraGate:Gateway:DownstreamAssembly"]);
        Assert.Equal("/kube/config", configuration["InfraGate:Kubernetes:KubeConfig"]);
    }

    [Fact]
    public void ResolveMappedKeys_EmptyOrWhitespaceEnvVar_Skipped()
    {
        using var environment = EnvironmentVariableScope.Set(
            ("INFRA_GATE_DOWNSTREAM_ASSEMBLY", "   "));

        var configuration = new ConfigurationBuilder()
            .AddInfraGateEnvironmentVariables(mappings =>
            {
                mappings.Map("INFRA_GATE_DOWNSTREAM_ASSEMBLY", "InfraGate:Gateway:DownstreamAssembly");
            })
            .Build();

        Assert.Null(configuration["InfraGate:Gateway:DownstreamAssembly"]);
    }

    [Fact]
    public void ResolveMappedKeys_EnvVarOverridesJsonValue()
    {
        using var environment = EnvironmentVariableScope.Set(
            ("INFRA_GATE_DOWNSTREAM_ASSEMBLY", "/app/env.dll"));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Gateway:DownstreamAssembly"] = "/app/json.dll"
            })
            .AddInfraGateEnvironmentVariables(mappings =>
            {
                mappings.Map("INFRA_GATE_DOWNSTREAM_ASSEMBLY", "InfraGate:Gateway:DownstreamAssembly");
            })
            .AddEnvironmentVariables()
            .Build();

        Assert.Equal("/app/env.dll", configuration["InfraGate:Gateway:DownstreamAssembly"]);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> previousValues;

        private EnvironmentVariableScope(Dictionary<string, string?> previousValues)
        {
            this.previousValues = previousValues;
        }

        public static EnvironmentVariableScope Set(params (string Name, string? Value)[] variables)
        {
            var previousValues = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var variable in variables)
            {
                previousValues[variable.Name] = Environment.GetEnvironmentVariable(variable.Name);
                Environment.SetEnvironmentVariable(variable.Name, variable.Value);
            }

            return new EnvironmentVariableScope(previousValues);
        }

        public void Dispose()
        {
            foreach (var previousValue in previousValues)
            {
                Environment.SetEnvironmentVariable(previousValue.Key, previousValue.Value);
            }
        }
    }
}
