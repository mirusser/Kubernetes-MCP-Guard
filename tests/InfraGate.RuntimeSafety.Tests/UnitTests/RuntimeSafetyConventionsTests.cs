namespace InfraGate.RuntimeSafety.Tests.UnitTests;

public sealed class RuntimeSafetyConventionsTests
{
    [Fact]
    public void RegisterInfraGateEnvVarMappings_NullMappings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RuntimeSafetyConventions.RegisterInfraGateEnvVarMappings(null!));
    }

    [Fact]
    public void RegisterInfraGateEnvVarMappings_MapsInfraGateEnvironmentToConfigKey()
    {
        var mappings = new InfraGateEnvVarMappings();

        RuntimeSafetyConventions.RegisterInfraGateEnvVarMappings(mappings);

        Assert.Equal(
            RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment,
            mappings.GetConfigurationKey(RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment));
    }

    [Fact]
    public void RegisterInfraGateEnvVarMappings_UnregisteredEnvVar_ReturnsNull()
    {
        var mappings = new InfraGateEnvVarMappings();

        RuntimeSafetyConventions.RegisterInfraGateEnvVarMappings(mappings);

        Assert.Null(mappings.GetConfigurationKey("UNREGISTERED_VAR"));
    }
}
