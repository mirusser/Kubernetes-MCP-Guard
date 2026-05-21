using Microsoft.Extensions.Configuration;

namespace InfraGate.McpGateway.Auth;

public sealed record class GatewayAuthOptions(
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

    public static GatewayAuthOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var authSettings = configuration
            .GetSection("InfraGate:Auth")
            .Get<InfraGateAuthSettings>();

        string oauthAuthority = authSettings?.OAuthAuthority ??
            throw new InvalidOperationException(
                $"{GatewayAuthConventions.EnvironmentVariables.OAuthAuthority} is required.");
        string oauthResource = authSettings?.OAuthResource ??
            GatewayAuthConventions.DefaultOAuthResource;
        string oauthScope = authSettings?.OAuthScope ??
            GatewayAuthConventions.DefaultOAuthScope;
        bool requireHttpsMetadata = authSettings?.OAuthRequireHttpsMetadata ?? true;
        string? oauthMetadataAddress = authSettings?.OAuthMetadataAddress;
        string approvalClientId = authSettings?.ApprovalOAuthClientId ??
            GatewayAuthConventions.DefaultApprovalOAuthClientId;
        string? approvalAuthorizationEndpoint = authSettings?.ApprovalOAuthAuthorizationEndpoint;
        string? approvalTokenEndpoint = authSettings?.ApprovalOAuthTokenEndpoint;
        string approvalCallbackPath = authSettings?.ApprovalOAuthCallbackPath ??
            GatewayAuthConventions.Approvals.DefaultCallbackPath;

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
