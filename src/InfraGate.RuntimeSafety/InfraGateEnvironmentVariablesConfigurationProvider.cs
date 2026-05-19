using Microsoft.Extensions.Configuration;

namespace InfraGate.RuntimeSafety;

internal sealed class InfraGateEnvironmentVariablesConfigurationProvider(
    InfraGateEnvVarMappings mappings)
    : ConfigurationProvider
{
    public override void Load()
    {
        Data.Clear();

        foreach (var (envVar, configKey) in GetAllMappedPairs())
        {
            string? value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                Data[configKey] = value;
            }
        }
    }

    private IEnumerable<(string EnvVar, string ConfigKey)> GetAllMappedPairs()
    {
        foreach (var (envVar, configKey) in mappings.GetAllEntries())
        {
            yield return (envVar, configKey);
        }
    }
}
