namespace InfraGate.RuntimeSafety;

public sealed class InfraGateEnvVarMappings
{
    private readonly Dictionary<string, string> mappings = new(StringComparer.Ordinal);
    private readonly HashSet<string> listMappings = new(StringComparer.Ordinal);

    public void Map(string environmentVariable, string configurationKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(environmentVariable);
        ArgumentException.ThrowIfNullOrEmpty(configurationKey);
        mappings[environmentVariable] = configurationKey;
    }

    /// <summary>
    /// Maps a comma-separated environment variable to an indexed configuration key sequence
    /// so that ASP.NET Core's configuration binder populates IReadOnlyList&lt;string&gt; properties.
    /// E.g. "ns1,ns2" → configKey:0=ns1, configKey:1=ns2.
    /// </summary>
    public void MapList(string environmentVariable, string configurationKey)
    {
        Map(environmentVariable, configurationKey);
        listMappings.Add(environmentVariable);
    }

    public string? GetConfigurationKey(string environmentVariable) =>
        mappings.GetValueOrDefault(environmentVariable);

    internal bool IsList(string environmentVariable) => listMappings.Contains(environmentVariable);

    internal IEnumerable<KeyValuePair<string, string>> GetAllEntries() => mappings;
}
