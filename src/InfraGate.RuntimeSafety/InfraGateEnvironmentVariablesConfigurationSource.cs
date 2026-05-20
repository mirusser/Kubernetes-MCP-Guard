using Microsoft.Extensions.Configuration;

namespace InfraGate.RuntimeSafety;

internal sealed class InfraGateEnvironmentVariablesConfigurationSource(
    InfraGateEnvVarMappings mappings)
    : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return new InfraGateEnvironmentVariablesConfigurationProvider(mappings);
    }
}
