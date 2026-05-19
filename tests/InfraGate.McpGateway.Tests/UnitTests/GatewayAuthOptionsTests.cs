using InfraGate.McpGateway.Auth;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class GatewayAuthOptionsTests
{
    [Fact]
    public void FromConfiguration_UsesGeneratedAppSettingsValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GatewayAuthConventions.ConfigurationKeys.OAuthAuthority] = "http://issuer/realms/infra-gate",
                [GatewayAuthConventions.ConfigurationKeys.OAuthMetadataAddress] =
                    "http://keycloak/realms/infra-gate/.well-known/openid-configuration",
                [GatewayAuthConventions.ConfigurationKeys.OAuthResource] = "http://gateway/mcp",
                [GatewayAuthConventions.ConfigurationKeys.OAuthScope] = "mcp:tools",
                [GatewayAuthConventions.ConfigurationKeys.OAuthRequireHttpsMetadata] = "false",
                [GatewayAuthConventions.ConfigurationKeys.ApprovalOAuthClientId] = "infra-gate-approval-ui",
                [GatewayAuthConventions.ConfigurationKeys.ApprovalOAuthCallbackPath] = "/approvals/oauth/callback",
                [GatewayAuthConventions.ConfigurationKeys.ApprovalOAuthAuthorizationEndpoint] = "http://issuer/auth",
                [GatewayAuthConventions.ConfigurationKeys.ApprovalOAuthTokenEndpoint] = "http://issuer/token"
            })
            .Build();

        var options = GatewayAuthOptions.FromConfiguration(configuration);

        Assert.Equal("http://issuer/realms/infra-gate", options.OAuthAuthority);
        Assert.Equal("http://keycloak/realms/infra-gate/.well-known/openid-configuration", options.OAuthMetadataAddress);
        Assert.Equal("http://gateway/mcp", options.OAuthResource);
        Assert.Equal("mcp:tools", options.OAuthScope);
        Assert.False(options.OAuthRequireHttpsMetadata);
        Assert.Equal("infra-gate-approval-ui", options.ApprovalOAuthClientId);
        Assert.Equal("/approvals/oauth/callback", options.ApprovalOAuthCallbackPath);
        Assert.Equal("http://issuer/auth", options.ApprovalOAuthAuthorizationEndpoint);
        Assert.Equal("http://issuer/token", options.ApprovalOAuthTokenEndpoint);
    }

    [Fact]
    public void FromConfiguration_BindsFromAuthSectionHierarchy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GatewayAuthConventions.ConfigurationKeys.OAuthAuthority] = "http://json-issuer",
                [GatewayAuthConventions.ConfigurationKeys.OAuthResource] = "https://gateway.example.com/mcp",
                [GatewayAuthConventions.ConfigurationKeys.OAuthScope] = "custom-scope"
            })
            .Build();

        var options = GatewayAuthOptions.FromConfiguration(configuration);

        Assert.Equal("http://json-issuer", options.OAuthAuthority);
        Assert.Equal("https://gateway.example.com/mcp", options.OAuthResource);
        Assert.Equal("custom-scope", options.OAuthScope);
    }
}
