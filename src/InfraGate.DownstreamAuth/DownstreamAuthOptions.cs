using InfraGate.ClientCredentials;

namespace InfraGate.DownstreamAuth;

public sealed class DownstreamAuthOptions
{
    public bool Required { get; init; } = true;
    public string Authority { get; init; } = string.Empty;
    public string? MetadataAddress { get; init; }
    public bool RequireHttpsMetadata { get; init; } = true;
    public string Audience { get; init; } = DownstreamAuthConventions.Defaults.Audience;
    public string Scope { get; init; } = DownstreamAuthConventions.Defaults.Scope;
    public string GatewayClientId { get; init; } = string.Empty;
    public string? GatewayClientSecret { get; init; }

    public ClientCredentialsTokenOptions ToClientCredentials() => new()
    {
        Authority = Authority,
        MetadataAddress = MetadataAddress,
        RequireHttpsMetadata = RequireHttpsMetadata,
        ClientId = GatewayClientId,
        ClientSecret = GatewayClientSecret,
        Scope = Scope,
    };

    public void Validate()
    {
        if (!Required)
        {
            return;
        }

        ValidateSharedFields();

        if (string.IsNullOrWhiteSpace(GatewayClientId))
        {
            throw new InvalidOperationException(
                $"Downstream auth is required but {DownstreamAuthConventions.EnvironmentVariables.GatewayClientId} is not configured.");
        }
    }

    public void ValidateForServer()
    {
        if (!Required)
        {
            return;
        }

        ValidateSharedFields();
    }

    private void ValidateSharedFields()
    {
        if (string.IsNullOrWhiteSpace(Authority))
        {
            throw new InvalidOperationException(
                $"Downstream auth is required but {DownstreamAuthConventions.EnvironmentVariables.Authority} is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException(
                $"Downstream auth is required but {DownstreamAuthConventions.EnvironmentVariables.Audience} is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Scope))
        {
            throw new InvalidOperationException(
                $"Downstream auth is required but {DownstreamAuthConventions.EnvironmentVariables.Scope} is not configured.");
        }
    }
}
