using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ConfigurationExtensionsTests
{
    [Fact]
    public void AddInfraGateConfiguration_WhenConfigPathNotSet_AddsEnvironmentAndCommandLineSources()
    {
        var previous = Environment.GetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath);
        try
        {
            Environment.SetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath, null);
            var builder = new ConfigurationBuilder();

            ConfigurationExtensions.AddInfraGateConfiguration(builder, []);

            Assert.Collection(
                builder.Sources,
                source => Assert.IsType<EnvironmentVariablesConfigurationSource>(source),
                source => Assert.IsType<CommandLineConfigurationSource>(source));
        }
        finally
        {
            Environment.SetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath, previous);
        }
    }

    [Fact]
    public void AddInfraGateConfiguration_WhenConfigPathIsWhitespace_AddsEnvironmentAndCommandLineSources()
    {
        var previous = Environment.GetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath);
        try
        {
            Environment.SetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath, "   ");
            var builder = new ConfigurationBuilder();

            ConfigurationExtensions.AddInfraGateConfiguration(builder, []);

            Assert.Collection(
                builder.Sources,
                source => Assert.IsType<EnvironmentVariablesConfigurationSource>(source),
                source => Assert.IsType<CommandLineConfigurationSource>(source));
        }
        finally
        {
            Environment.SetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath, previous);
        }
    }
}
