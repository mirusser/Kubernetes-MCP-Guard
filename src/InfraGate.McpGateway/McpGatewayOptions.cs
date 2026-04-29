namespace InfraGate.McpGateway;

public sealed record McpGatewayOptions(
    string? BearerToken,
    string DownstreamProject,
    string GuardAuditRoot,
    string WorkingDirectory,
    string? OAuthAuthority = null,
    string OAuthResource = McpGatewayConventions.DefaultOAuthResource,
    string OAuthScope = McpGatewayConventions.DefaultOAuthScope,
    bool OAuthRequireHttpsMetadata = true)
{
    public const string DefaultUrl = McpGatewayConventions.DefaultUrl;

    public bool OAuthEnabled => !string.IsNullOrWhiteSpace(OAuthAuthority);

    public bool StaticBearerEnabled => !string.IsNullOrWhiteSpace(BearerToken);

    public static McpGatewayOptions FromEnvironment()
    {
        var token = Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.BearerToken);
        var oauthAuthority = Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.OAuthAuthority);

        var workingDirectory = Directory.GetCurrentDirectory();
        var downstreamProject =
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.DownstreamProject) ??
            Path.Combine(
                workingDirectory,
                McpGatewayConventions.Paths.SourceDirectory,
                McpGatewayConventions.Paths.DefaultDownstreamProjectDirectory,
                McpGatewayConventions.Paths.DefaultDownstreamProjectFileName);
        var auditRoot =
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.GuardAuditRoot) ??
            Path.Combine(workingDirectory, McpGatewayConventions.Paths.DefaultGuardAuditRootDirectory);
        var oauthResource =
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.OAuthResource) ??
            McpGatewayConventions.DefaultOAuthResource;
        var oauthScope =
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.OAuthScope) ??
            McpGatewayConventions.DefaultOAuthScope;
        var requireHttpsMetadata = ParseBooleanEnvironmentVariable(
            Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.OAuthRequireHttpsMetadata),
            defaultValue: true);

        if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(oauthAuthority))
        {
            throw new InvalidOperationException(
                $"{McpGatewayConventions.EnvironmentVariables.BearerToken} or {McpGatewayConventions.EnvironmentVariables.OAuthAuthority} is required.");
        }

        return new McpGatewayOptions(
            token,
            downstreamProject,
            auditRoot,
            workingDirectory,
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
