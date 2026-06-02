using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.RuntimeSafety.Tests.UnitTests;

public sealed class RuntimeModeResolverTests
{
    [Fact]
    public void FromEnvironment_ReturnsDevelopment_WhenAllUnset()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment, null),
            (RuntimeSafetyConventions.EnvironmentVariables.AspNetCoreEnvironment, null));

        var mode = RuntimeModeResolver.FromEnvironment();

        Assert.Equal(RuntimeMode.Development, mode);
    }

    [Fact]
    public void FromEnvironment_FallsBackToDotNetEnvironment()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment, RuntimeSafetyConventions.EnvironmentValues.Production),
            (RuntimeSafetyConventions.EnvironmentVariables.AspNetCoreEnvironment, null));

        var mode = RuntimeModeResolver.FromEnvironment();

        Assert.Equal(RuntimeMode.Production, mode);
    }

    [Fact]
    public void FromEnvironment_FallsBackToAspNetCoreEnvironment()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment, null),
            (RuntimeSafetyConventions.EnvironmentVariables.AspNetCoreEnvironment, RuntimeSafetyConventions.EnvironmentValues.Production));

        var mode = RuntimeModeResolver.FromEnvironment();

        Assert.Equal(RuntimeMode.Production, mode);
    }

    [Fact]
    public void FromEnvironment_TreatsNonDevelopmentStandardEnvironmentAsProduction()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment, "Staging"));

        var mode = RuntimeModeResolver.FromEnvironment();

        Assert.Equal(RuntimeMode.Production, mode);
    }

    [Fact]
    public void FromConfiguration_UsesInfraGateRuntimeEnvironment()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment] =
                    RuntimeSafetyConventions.EnvironmentValues.Production
            })
            .Build();

        var mode = RuntimeModeResolver.FromConfiguration(configuration);

        Assert.Equal(RuntimeMode.Production, mode);
    }

    [Fact]
    public void FromConfiguration_PrefersInfraGateRuntimeEnvironmentOverStandardEnvironment()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment] =
                    RuntimeSafetyConventions.EnvironmentValues.Development,
                [RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment] =
                    RuntimeSafetyConventions.EnvironmentValues.Production
            })
            .Build();

        var mode = RuntimeModeResolver.FromConfiguration(configuration);

        Assert.Equal(RuntimeMode.Production, mode);
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
