namespace InfraGate.RuntimeSafety;

public sealed class InfraGateEnvVarMappings
{
    private readonly Dictionary<string, string> mappings = new(StringComparer.Ordinal);

    public void Map(string environmentVariable, string configurationKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(environmentVariable);
        ArgumentException.ThrowIfNullOrEmpty(configurationKey);
        mappings[environmentVariable] = configurationKey;
    }

    public string? GetConfigurationKey(string environmentVariable) =>
        mappings.TryGetValue(environmentVariable, out string? configKey) ? configKey : null;

    internal IEnumerable<KeyValuePair<string, string>> GetAllEntries() => mappings;
}
