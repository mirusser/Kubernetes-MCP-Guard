using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.RuntimeSafety.Tests.UnitTests;

public sealed class InfraGateRuntimeSettingsTests
{
    [Fact]
    public void BindFromConfiguration_PopulatesEnvironment()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment] = RuntimeSafetyConventions.EnvironmentValues.Production
            })
            .Build();

        var settings = configuration
            .GetSection("InfraGate:Runtime")
            .Get<InfraGateRuntimeSettings>();

        Assert.NotNull(settings);
        Assert.Equal(RuntimeSafetyConventions.EnvironmentValues.Production, settings!.Environment);
    }

    [Fact]
    public void BindFromConfiguration_MissingSection_ReturnsNull()
    {
        var configuration = new ConfigurationBuilder().Build();

        var settings = configuration
            .GetSection("InfraGate:Runtime")
            .Get<InfraGateRuntimeSettings>();

        Assert.Null(settings);
    }
}
