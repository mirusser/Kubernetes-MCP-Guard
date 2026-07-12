using InfraGate.McpGateway.Auth;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class InfraGateAuthSettingsTests
{
    [Fact]
    public void BindFromConfiguration_PopulatesAllFields()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Auth:OAuthAuthority"] = "https://issuer.example.com",
                ["InfraGate:Auth:OAuthMetadataAddress"] = "https://issuer.example.com/.well-known/openid-configuration",
                ["InfraGate:Auth:OAuthResource"] = "https://gateway.example.com/mcp",
                ["InfraGate:Auth:OAuthScope"] = "mcp:tools",
                ["InfraGate:Auth:OAuthRequireHttpsMetadata"] = "true",
                ["InfraGate:Auth:ApprovalOAuthClientId"] = "approval-client",
                ["InfraGate:Auth:ApprovalOAuthCallbackPath"] = "/approvals/oauth/callback",
                ["InfraGate:Auth:ApprovalOAuthAuthorizationEndpoint"] = "https://issuer.example.com/authorize",
                ["InfraGate:Auth:ApprovalOAuthTokenEndpoint"] = "https://issuer.example.com/token",
                ["InfraGate:Auth:TokenIntrospectionEnabled"] = "true",
                ["InfraGate:Auth:TokenIntrospectionEndpoint"] = "https://issuer.example.com/introspect",
                ["InfraGate:Auth:TokenIntrospectionClientId"] = "gateway-resource-server",
                ["InfraGate:Auth:TokenIntrospectionClientSecret"] = "secret-placeholder",
                ["InfraGate:Auth:TokenIntrospectionCacheSeconds"] = "15",
                ["InfraGate:Auth:MaxAcceptedAccessTokenLifetimeSeconds"] = "300"
            })
            .Build();

        var settings = configuration
            .GetSection("InfraGate:Auth")
            .Get<InfraGateAuthSettings>();

        Assert.NotNull(settings);
        Assert.Equal("https://issuer.example.com", settings!.OAuthAuthority);
        Assert.Equal("https://gateway.example.com/mcp", settings.OAuthResource);
        Assert.True(settings.OAuthRequireHttpsMetadata);
        Assert.Equal("mcp:tools", settings.OAuthScope);
        Assert.Equal("approval-client", settings.ApprovalOAuthClientId);
        Assert.True(settings.TokenIntrospectionEnabled);
        Assert.Equal("https://issuer.example.com/introspect", settings.TokenIntrospectionEndpoint);
        Assert.Equal("gateway-resource-server", settings.TokenIntrospectionClientId);
        Assert.Equal("secret-placeholder", settings.TokenIntrospectionClientSecret);
        Assert.Equal(15, settings.TokenIntrospectionCacheSeconds);
        Assert.Equal(300, settings.MaxAcceptedAccessTokenLifetimeSeconds);
    }

    [Fact]
    public void BindFromConfiguration_PartialSection_OnlyPopulatesProvidedKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Auth:OAuthAuthority"] = "https://issuer.example.com",
                ["InfraGate:Auth:OAuthScope"] = "mcp:tools"
            })
            .Build();

        var settings = configuration
            .GetSection("InfraGate:Auth")
            .Get<InfraGateAuthSettings>();

        Assert.NotNull(settings);
        Assert.Equal("https://issuer.example.com", settings!.OAuthAuthority);
        Assert.Equal("mcp:tools", settings.OAuthScope);
        Assert.Null(settings.OAuthRequireHttpsMetadata);
    }
}
