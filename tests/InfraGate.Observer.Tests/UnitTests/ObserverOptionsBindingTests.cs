using Microsoft.Extensions.Configuration;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserverOptionsBindingTests
{
    [Fact]
    public void Bind_FullSection_PopulatesScalarsAndNestedClientCredentials()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Observer:GatewayBaseUrl"] = "http://gateway:3001/mcp",
                ["InfraGate:Observer:CycleIntervalSeconds"] = "120",
                ["InfraGate:Observer:ClientCredentials:ClientId"] = "infra-gate-observer",
                ["InfraGate:Observer:ClientCredentials:ClientSecret"] = "s3cr3t",
                ["InfraGate:Observer:ClientCredentials:Authority"] = "https://idp.example.com/realms/test",
                ["InfraGate:Observer:ClientCredentials:Scope"] = "mcp:tools.readonly",
                ["InfraGate:Observer:ClientCredentials:RequireHttpsMetadata"] = "false"
            })
            .Build();

        var options = configuration
            .GetSection(ObserverOptions.SectionName)
            .Get<ObserverOptions>();

        Assert.NotNull(options);
        Assert.Equal("http://gateway:3001/mcp", options!.GatewayBaseUrl);
        Assert.Equal(120, options.CycleIntervalSeconds);
        Assert.NotNull(options.ClientCredentials);
        Assert.Equal("infra-gate-observer", options.ClientCredentials.ClientId);
        Assert.Equal("s3cr3t", options.ClientCredentials.ClientSecret);
        Assert.Equal("https://idp.example.com/realms/test", options.ClientCredentials.Authority);
        Assert.Equal("mcp:tools.readonly", options.ClientCredentials.Scope);
        Assert.False(options.ClientCredentials.RequireHttpsMetadata);
    }

    [Fact]
    public void Bind_FromEnvironmentVariables_BindsScalarsAndNestedClientCredentials()
    {
        try
        {
            Environment.SetEnvironmentVariable("InfraGate__Observer__GatewayBaseUrl", "http://gateway:3001/mcp");
            Environment.SetEnvironmentVariable("InfraGate__Observer__CycleIntervalSeconds", "120");
            Environment.SetEnvironmentVariable("InfraGate__Observer__ClientCredentials__ClientId", "infra-gate-observer");
            Environment.SetEnvironmentVariable("InfraGate__Observer__ClientCredentials__ClientSecret", "s3cr3t");
            Environment.SetEnvironmentVariable("InfraGate__Observer__ClientCredentials__Authority", "https://idp.example.com/realms/test");
            Environment.SetEnvironmentVariable("InfraGate__Observer__ClientCredentials__Scope", "mcp:tools.readonly");
            Environment.SetEnvironmentVariable("InfraGate__Observer__ClientCredentials__RequireHttpsMetadata", "false");

            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var options = configuration
                .GetSection(ObserverOptions.SectionName)
                .Get<ObserverOptions>();

            Assert.NotNull(options);
            Assert.Equal("http://gateway:3001/mcp", options!.GatewayBaseUrl);
            Assert.Equal(120, options.CycleIntervalSeconds);
            Assert.NotNull(options.ClientCredentials);
            Assert.Equal("infra-gate-observer", options.ClientCredentials.ClientId);
            Assert.Equal("s3cr3t", options.ClientCredentials.ClientSecret);
            Assert.Equal("https://idp.example.com/realms/test", options.ClientCredentials.Authority);
            Assert.Equal("mcp:tools.readonly", options.ClientCredentials.Scope);
            Assert.False(options.ClientCredentials.RequireHttpsMetadata);
        }
        finally
        {
            Environment.SetEnvironmentVariable("InfraGate__Observer__GatewayBaseUrl", null);
            Environment.SetEnvironmentVariable("InfraGate__Observer__CycleIntervalSeconds", null);
            Environment.SetEnvironmentVariable("InfraGate__Observer__ClientCredentials__ClientId", null);
            Environment.SetEnvironmentVariable("InfraGate__Observer__ClientCredentials__ClientSecret", null);
            Environment.SetEnvironmentVariable("InfraGate__Observer__ClientCredentials__Authority", null);
            Environment.SetEnvironmentVariable("InfraGate__Observer__ClientCredentials__Scope", null);
            Environment.SetEnvironmentVariable("InfraGate__Observer__ClientCredentials__RequireHttpsMetadata", null);
        }
    }

    [Fact]
    public void Bind_SectionWithoutOptionalKeys_KeepsDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Observer:GatewayBaseUrl"] = "http://gateway:3001/mcp"
            })
            .Build();

        var options = configuration
            .GetSection(ObserverOptions.SectionName)
            .Get<ObserverOptions>();

        Assert.NotNull(options);
        Assert.Equal("http://gateway:3001/mcp", options!.GatewayBaseUrl);
        Assert.Equal(AnomalyObserverConventions.DefaultCadenceSeconds, options.CycleIntervalSeconds);
        Assert.NotNull(options.ClientCredentials);
    }
}
