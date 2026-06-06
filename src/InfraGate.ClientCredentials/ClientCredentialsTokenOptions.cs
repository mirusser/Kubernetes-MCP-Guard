namespace InfraGate.ClientCredentials;

public sealed record class ClientCredentialsTokenOptions
{
    public string Authority { get; init; } = string.Empty;
    public string? MetadataAddress { get; init; }
    public bool RequireHttpsMetadata { get; init; } = true;
    public string ClientId { get; init; } = string.Empty;
    public string? ClientSecret { get; init; }
    public string Scope { get; init; } = string.Empty;
    public bool UseDPoP { get; init; }
    public int RefreshSkewSeconds { get; init; } = ClientCredentialsConventions.DefaultRefreshSkewSeconds;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Authority))
        {
            throw new InvalidOperationException(
                $"Client credentials require Authority to be configured.");
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException(
                $"Client credentials require ClientId to be configured.");
        }

        if (string.IsNullOrWhiteSpace(Scope))
        {
            throw new InvalidOperationException(
                $"Client credentials require Scope to be configured.");
        }
    }
}
