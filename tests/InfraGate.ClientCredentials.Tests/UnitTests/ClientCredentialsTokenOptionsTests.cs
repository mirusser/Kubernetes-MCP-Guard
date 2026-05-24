using InfraGate.ClientCredentials;

namespace InfraGate.ClientCredentials.Tests.UnitTests;

public sealed class ClientCredentialsTokenOptionsTests
{
    [Fact]
    public void Validate_WhenRequiredFieldsPresent_Succeeds()
    {
        var options = new ClientCredentialsTokenOptions
        {
            Authority = "https://auth.example.com",
            ClientId = "my-client",
            Scope = "mcp:tools.readonly"
        };

        Exception? exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_WhenMissingAuthority_Throws()
    {
        var options = new ClientCredentialsTokenOptions
        {
            Authority = string.Empty,
            ClientId = "my-client",
            Scope = "mcp:tools.readonly"
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("Authority", exception.Message);
    }

    [Fact]
    public void Validate_WhenMissingClientId_Throws()
    {
        var options = new ClientCredentialsTokenOptions
        {
            Authority = "https://auth.example.com",
            ClientId = string.Empty,
            Scope = "mcp:tools.readonly"
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("ClientId", exception.Message);
    }

    [Fact]
    public void Validate_WhenMissingScope_Throws()
    {
        var options = new ClientCredentialsTokenOptions
        {
            Authority = "https://auth.example.com",
            ClientId = "my-client",
            Scope = string.Empty
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("Scope", exception.Message);
    }

    [Fact]
    public void ClientSecret_NullByDefault_DoesNotThrow()
    {
        var options = new ClientCredentialsTokenOptions
        {
            Authority = "https://auth.example.com",
            ClientId = "my-client",
            Scope = "mcp:tools.readonly",
            ClientSecret = null
        };

        Exception? exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void UsesDefaultRefreshSkewOf30Seconds()
    {
        var options = new ClientCredentialsTokenOptions();

        Assert.Equal(30, options.RefreshSkewSeconds);
    }

    [Fact]
    public void UsesDefaultRequireHttpsMetadataOfTrue()
    {
        var options = new ClientCredentialsTokenOptions();

        Assert.True(options.RequireHttpsMetadata);
    }
}
