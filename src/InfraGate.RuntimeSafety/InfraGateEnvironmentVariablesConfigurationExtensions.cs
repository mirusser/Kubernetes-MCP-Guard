using Microsoft.Extensions.Configuration;

namespace InfraGate.RuntimeSafety;

public static class InfraGateEnvironmentVariablesConfigurationExtensions
{
    public static IConfigurationBuilder AddInfraGateEnvironmentVariables(
        this IConfigurationBuilder builder,
        Action<InfraGateEnvVarMappings> configureMappings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureMappings);

        var mappings = new InfraGateEnvVarMappings();
        configureMappings(mappings);

        return builder.Add(new InfraGateEnvironmentVariablesConfigurationSource(mappings));
    }
}
