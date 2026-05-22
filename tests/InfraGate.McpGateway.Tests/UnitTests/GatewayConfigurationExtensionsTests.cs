using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class GatewayConfigurationExtensionsTests
{
    [Fact]
    public void AddInfraGateConfiguration_WhenConfigPathNotSet_AddsNoSources()
    {
        var previous = Environment.GetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath);
        try
        {
            Environment.SetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath, null);
            var builder = new ConfigurationBuilder();

            GatewayConfigurationExtensions.AddInfraGateConfiguration(builder, []);

            Assert.Empty(builder.Sources);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath, previous);
        }
    }

    [Fact]
    public void AddInfraGateConfiguration_WhenConfigPathIsWhitespace_AddsNoSources()
    {
        var previous = Environment.GetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath);
        try
        {
            Environment.SetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath, "   ");
            var builder = new ConfigurationBuilder();

            GatewayConfigurationExtensions.AddInfraGateConfiguration(builder, []);

            Assert.Empty(builder.Sources);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath, previous);
        }
    }
}
