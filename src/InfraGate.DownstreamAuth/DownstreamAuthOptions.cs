namespace InfraGate.DownstreamAuth;

public sealed record DownstreamAuthOptions
{
    public bool Required { get; init; }
    public string Authority { get; init; } = string.Empty;
    public string? MetadataAddress { get; init; }
    public bool RequireHttpsMetadata { get; init; } = true;
    public string Audience { get; init; } = DownstreamAuthConventions.Defaults.Audience;
    public string Scope { get; init; } = DownstreamAuthConventions.Defaults.Scope;
    public string GatewayClientId { get; init; } = string.Empty;
    public string? GatewayClientSecret { get; init; }

    public static DownstreamAuthOptions FromEnvironment()
    {
        return new DownstreamAuthOptions
        {
            Required = string.Equals(
                Environment.GetEnvironmentVariable(DownstreamAuthConventions.EnvironmentVariables.Required),
                "true",
                StringComparison.OrdinalIgnoreCase),
            Authority = Environment.GetEnvironmentVariable(DownstreamAuthConventions.EnvironmentVariables.Authority) ?? string.Empty,
            MetadataAddress = Environment.GetEnvironmentVariable(DownstreamAuthConventions.EnvironmentVariables.MetadataAddress),
            RequireHttpsMetadata = !string.Equals(
                Environment.GetEnvironmentVariable(DownstreamAuthConventions.EnvironmentVariables.RequireHttpsMetadata),
                "false",
                StringComparison.OrdinalIgnoreCase),
            Audience = Environment.GetEnvironmentVariable(DownstreamAuthConventions.EnvironmentVariables.Audience)
                ?? DownstreamAuthConventions.Defaults.Audience,
            Scope = Environment.GetEnvironmentVariable(DownstreamAuthConventions.EnvironmentVariables.Scope)
                ?? DownstreamAuthConventions.Defaults.Scope,
            GatewayClientId = Environment.GetEnvironmentVariable(DownstreamAuthConventions.EnvironmentVariables.GatewayClientId) ?? string.Empty,
            GatewayClientSecret = Environment.GetEnvironmentVariable(DownstreamAuthConventions.EnvironmentVariables.GatewayClientSecret),
        };
    }

    public void Validate()
    {
        if (!Required)
        {
            return;
        }

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
