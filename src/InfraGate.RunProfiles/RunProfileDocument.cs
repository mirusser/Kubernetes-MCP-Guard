namespace InfraGate.RunProfiles;

internal sealed record RunProfileDocument(IReadOnlyList<RunProfile> Profiles)
{
    public RunProfile FindProfile(string name)
    {
        foreach (RunProfile profile in Profiles)
        {
            if (string.Equals(profile.Name, name, StringComparison.Ordinal))
            {
                return profile;
            }
        }

        throw new InvalidOperationException($"Unknown Run Profile: {name}");
    }
}
