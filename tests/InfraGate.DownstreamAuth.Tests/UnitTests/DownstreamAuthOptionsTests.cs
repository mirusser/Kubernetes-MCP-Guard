using InfraGate.DownstreamAuth;
using Microsoft.Extensions.Configuration;

namespace InfraGate.DownstreamAuth.Tests.UnitTests;

public sealed class DownstreamAuthOptionsTests
{
    private static DownstreamAuthOptions AllRequiredFields() => new()
    {
        Required = true,
        Authority = "https://auth.example.com",
        Audience = "urn:infra-gate:mcp-server",
        Scope = "mcp:downstream",
        GatewayClientId = "infra-gate-gateway-service",
    };

    [Fact]
    public void Validate_WhenNotRequired_Succeeds()
    {
        var options = new DownstreamAuthOptions { Required = false };

        Exception? exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_WhenRequiredAndAllFieldsPresent_Succeeds()
    {
        var options = AllRequiredFields();

        Exception? exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_WhenRequiredAndMissingAuthority_Throws()
    {
        var options = new DownstreamAuthOptions
        {
            Required = true,
            Authority = string.Empty,
            Audience = "urn:infra-gate:mcp-server",
            Scope = "mcp:downstream",
            GatewayClientId = "infra-gate-gateway-service",
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.Authority, exception.Message);
    }

    [Fact]
    public void Validate_WhenRequiredAndMissingAudience_Throws()
    {
        var options = new DownstreamAuthOptions
        {
            Required = true,
            Authority = "https://auth.example.com",
            Audience = string.Empty,
            Scope = "mcp:downstream",
            GatewayClientId = "infra-gate-gateway-service",
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.Audience, exception.Message);
    }

    [Fact]
    public void Validate_WhenRequiredAndMissingScope_Throws()
    {
        var options = new DownstreamAuthOptions
        {
            Required = true,
            Authority = "https://auth.example.com",
            Audience = "urn:infra-gate:mcp-server",
            Scope = string.Empty,
            GatewayClientId = "infra-gate-gateway-service",
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.Scope, exception.Message);
    }

    [Fact]
    public void Validate_WhenRequiredAndMissingGatewayClientId_Throws()
    {
        var options = new DownstreamAuthOptions
        {
            Required = true,
            Authority = "https://auth.example.com",
            Audience = "urn:infra-gate:mcp-server",
            Scope = "mcp:downstream",
            GatewayClientId = string.Empty,
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.GatewayClientId, exception.Message);
    }

    [Fact]
    public void Validate_WhenRequiredAndGatewayClientSecretIsNull_Succeeds()
    {
        var options = new DownstreamAuthOptions
        {
            Required = true,
            Authority = "https://auth.example.com",
            Audience = "urn:infra-gate:mcp-server",
            Scope = "mcp:downstream",
            GatewayClientId = "infra-gate-gateway-service",
            GatewayClientSecret = null,
        };

        Exception? exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateForServer_WhenNotRequired_Succeeds()
    {
        var options = new DownstreamAuthOptions { Required = false };

        Exception? exception = Record.Exception(() => options.ValidateForServer());

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateForServer_WhenRequiredAndServerFieldsPresent_Succeeds()
    {
        var options = new DownstreamAuthOptions
        {
            Required = true,
            Authority = "https://auth.example.com",
            Audience = "urn:infra-gate:mcp-server",
            Scope = "mcp:downstream",
        };

        Exception? exception = Record.Exception(() => options.ValidateForServer());

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateForServer_WhenRequiredAndMissingGatewayClientId_Succeeds()
    {
        var options = new DownstreamAuthOptions
        {
            Required = true,
            Authority = "https://auth.example.com",
            Audience = "urn:infra-gate:mcp-server",
            Scope = "mcp:downstream",
            GatewayClientId = string.Empty,
        };

        Exception? exception = Record.Exception(() => options.ValidateForServer());

        Assert.Null(exception);
    }

    [Fact]
    public void Bind_FullSection_PopulatesAllFields()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DownstreamAuthConventions.ConfigurationKeys.Required] = "true",
                [DownstreamAuthConventions.ConfigurationKeys.Authority] = "https://auth.example.com",
                [DownstreamAuthConventions.ConfigurationKeys.MetadataAddress] = "https://auth.example.com/.well-known/openid-configuration",
                [DownstreamAuthConventions.ConfigurationKeys.RequireHttpsMetadata] = "false",
                [DownstreamAuthConventions.ConfigurationKeys.Audience] = DownstreamAuthConventions.Defaults.Audience,
                [DownstreamAuthConventions.ConfigurationKeys.Scope] = DownstreamAuthConventions.Defaults.Scope,
                [DownstreamAuthConventions.ConfigurationKeys.GatewayClientId] = "infra-gate-gateway-service",
                [DownstreamAuthConventions.ConfigurationKeys.GatewayClientSecret] = "supersecret",
            })
            .Build();

        var options = configuration
            .GetSection("InfraGate:DownstreamAuth")
            .Get<DownstreamAuthOptions>();

        Assert.NotNull(options);
        Assert.True(options.Required);
        Assert.Equal("https://auth.example.com", options.Authority);
        Assert.Equal("https://auth.example.com/.well-known/openid-configuration", options.MetadataAddress);
        Assert.False(options.RequireHttpsMetadata);
        Assert.Equal(DownstreamAuthConventions.Defaults.Audience, options.Audience);
        Assert.Equal(DownstreamAuthConventions.Defaults.Scope, options.Scope);
        Assert.Equal("infra-gate-gateway-service", options.GatewayClientId);
        Assert.Equal("supersecret", options.GatewayClientSecret);
    }

    [Fact]
    public void Bind_AbsentSection_UsesSecureDefaults()
    {
        var options = new ConfigurationBuilder()
            .Build()
            .GetSection("InfraGate:DownstreamAuth")
            .Get<DownstreamAuthOptions>() ?? new DownstreamAuthOptions();

        Assert.True(options.Required);
        Assert.Equal(DownstreamAuthConventions.Defaults.Audience, options.Audience);
        Assert.Equal(DownstreamAuthConventions.Defaults.Scope, options.Scope);
        Assert.True(options.RequireHttpsMetadata);
        Assert.Null(options.MetadataAddress);
        Assert.Null(options.GatewayClientSecret);
    }
}
