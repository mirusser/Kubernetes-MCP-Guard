using Microsoft.Extensions.Configuration;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ExecutorOptionsBindingTests
{
    [Fact]
    public void Bind_FullSection_PopulatesScalarsAndNestedClientCredentials()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Executor:GatewayBaseUrl"] = "http://gateway:3001/mcp",
                ["InfraGate:Executor:ConcurrencyCap"] = "128",
                ["InfraGate:Executor:WatchTimeoutSeconds"] = "120",
                ["InfraGate:Executor:ClientCredentials:Authority"] = "http://keycloak:8080/realms/infra-gate",
                ["InfraGate:Executor:ClientCredentials:ClientId"] = "infra-gate-executor",
                ["InfraGate:Executor:ClientCredentials:ClientSecret"] = "executor-secret",
                ["InfraGate:Executor:ClientCredentials:Scope"] = "mcp:tools.execute",
                ["InfraGate:Executor:ClientCredentials:RequireHttpsMetadata"] = "false",
            })
            .Build();

        var options = configuration.GetSection(ExecutorConventions.SectionName).Get<ExecutorOptions>();

        Assert.NotNull(options);
        Assert.Equal("http://gateway:3001/mcp", options.GatewayBaseUrl);
        Assert.Equal(128, options.ConcurrencyCap);
        Assert.Equal(120, options.WatchTimeoutSeconds);
        Assert.Equal("http://keycloak:8080/realms/infra-gate", options.ClientCredentials.Authority);
        Assert.Equal("infra-gate-executor", options.ClientCredentials.ClientId);
        Assert.Equal("executor-secret", options.ClientCredentials.ClientSecret);
        Assert.Equal("mcp:tools.execute", options.ClientCredentials.Scope);
        Assert.False(options.ClientCredentials.RequireHttpsMetadata);
    }

    [Fact]
    public void Bind_SectionWithoutOptionalKeys_KeepsDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Executor:GatewayBaseUrl"] = "http://gateway:3001/mcp",
            })
            .Build();

        var options = configuration.GetSection(ExecutorConventions.SectionName).Get<ExecutorOptions>();

        Assert.NotNull(options);
        Assert.Equal(ExecutorConventions.DefaultConcurrencyCap, options.ConcurrencyCap);
        Assert.Equal(ExecutorConventions.DefaultWatchTimeoutSeconds, options.WatchTimeoutSeconds);
        Assert.NotNull(options.ClientCredentials);
    }
}
