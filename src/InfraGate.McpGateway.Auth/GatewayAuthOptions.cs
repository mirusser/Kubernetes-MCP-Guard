namespace InfraGate.McpGateway.Auth;

public sealed record GatewayAuthOptions(
    string? BearerToken,
    string? OAuthAuthority = null,
    string OAuthResource = GatewayAuthConventions.DefaultOAuthResource,
    string OAuthScope = GatewayAuthConventions.DefaultOAuthScope,
    bool OAuthRequireHttpsMetadata = true)
{
    public bool OAuthEnabled => !string.IsNullOrWhiteSpace(OAuthAuthority);

    public bool StaticBearerEnabled => !string.IsNullOrWhiteSpace(BearerToken);

    public static GatewayAuthOptions FromEnvironment()
    {
        var token = Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.BearerToken);
        var oauthAuthority = Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.OAuthAuthority);
        var oauthResource =
            Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.OAuthResource) ??
            GatewayAuthConventions.DefaultOAuthResource;
        var oauthScope =
            Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.OAuthScope) ??
            GatewayAuthConventions.DefaultOAuthScope;
        var requireHttpsMetadata = ParseBooleanEnvironmentVariable(
            Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.OAuthRequireHttpsMetadata),
            defaultValue: true);

        if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(oauthAuthority))
        {
            throw new InvalidOperationException(
                $"{GatewayAuthConventions.EnvironmentVariables.BearerToken} or {GatewayAuthConventions.EnvironmentVariables.OAuthAuthority} is required.");
        }

        return new GatewayAuthOptions(
            token,
            oauthAuthority,
            oauthResource,
            oauthScope,
            requireHttpsMetadata);
    }

    private static bool ParseBooleanEnvironmentVariable(string? value, bool defaultValue)
    {
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : bool.Parse(value);
    }
}
