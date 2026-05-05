namespace InfraGate.McpGateway.Auth;

public sealed record GatewayAuthOptions(
    string OAuthAuthority,
    string OAuthResource = GatewayAuthConventions.DefaultOAuthResource,
    string OAuthScope = GatewayAuthConventions.DefaultOAuthScope,
    bool OAuthRequireHttpsMetadata = true,
    string? OAuthMetadataAddress = null,
    string ApprovalOAuthClientId = GatewayAuthConventions.DefaultApprovalOAuthClientId,
    string? ApprovalOAuthAuthorizationEndpoint = null,
    string? ApprovalOAuthTokenEndpoint = null,
    string ApprovalOAuthCallbackPath = GatewayAuthConventions.Approvals.DefaultCallbackPath)
{
    public string ApprovalAuthorizationEndpoint =>
        string.IsNullOrWhiteSpace(ApprovalOAuthAuthorizationEndpoint)
            ? TrimTrailingSlash(OAuthAuthority) + GatewayAuthConventions.Approvals.AuthorizePath
            : ApprovalOAuthAuthorizationEndpoint;

    public string ApprovalTokenEndpoint =>
        string.IsNullOrWhiteSpace(ApprovalOAuthTokenEndpoint)
            ? TrimTrailingSlash(OAuthAuthority) + GatewayAuthConventions.Approvals.TokenPath
            : ApprovalOAuthTokenEndpoint;

    public static GatewayAuthOptions FromEnvironment()
    {
        var oauthAuthority = Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.OAuthAuthority);
        var oauthMetadataAddress = Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.OAuthMetadataAddress);
        var oauthResource =
            Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.OAuthResource) ??
            GatewayAuthConventions.DefaultOAuthResource;
        var oauthScope =
            Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.OAuthScope) ??
            GatewayAuthConventions.DefaultOAuthScope;
        var requireHttpsMetadata = ParseBooleanEnvironmentVariable(
            Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.OAuthRequireHttpsMetadata),
            defaultValue: true);
        var approvalClientId =
            Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.ApprovalOAuthClientId) ??
            GatewayAuthConventions.DefaultApprovalOAuthClientId;
        var approvalAuthorizationEndpoint =
            Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.ApprovalOAuthAuthorizationEndpoint);
        var approvalTokenEndpoint =
            Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.ApprovalOAuthTokenEndpoint);
        var approvalCallbackPath =
            Environment.GetEnvironmentVariable(GatewayAuthConventions.EnvironmentVariables.ApprovalOAuthCallbackPath) ??
            GatewayAuthConventions.Approvals.DefaultCallbackPath;

        if (string.IsNullOrWhiteSpace(oauthAuthority))
        {
            throw new InvalidOperationException(
                $"{GatewayAuthConventions.EnvironmentVariables.OAuthAuthority} is required.");
        }

        return new GatewayAuthOptions(
            oauthAuthority,
            oauthResource,
            oauthScope,
            requireHttpsMetadata,
            oauthMetadataAddress,
            approvalClientId,
            approvalAuthorizationEndpoint,
            approvalTokenEndpoint,
            approvalCallbackPath);
    }

    private static bool ParseBooleanEnvironmentVariable(string? value, bool defaultValue)
    {
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : bool.Parse(value);
    }

    private static string TrimTrailingSlash(string value) => value.TrimEnd('/');
}
