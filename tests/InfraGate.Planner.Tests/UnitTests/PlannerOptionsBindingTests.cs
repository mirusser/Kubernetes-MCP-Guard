using Microsoft.Extensions.Configuration;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerOptionsBindingTests
{
    [Fact]
    public void Bind_FullSection_PopulatesScalarsAndNestedClientCredentials()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Planner:GatewayBaseUrl"] = "http://gateway:3001/mcp",
                ["InfraGate:Planner:AnomalyWallClockCapSeconds"] = "60",
                ["InfraGate:Planner:ClientCredentials:ClientId"] = "infra-gate-planner",
                ["InfraGate:Planner:ClientCredentials:ClientSecret"] = "s3cr3t",
                ["InfraGate:Planner:ClientCredentials:Authority"] = "https://idp.example.com/realms/test",
                ["InfraGate:Planner:ClientCredentials:Scope"] = "mcp:tools.propose mcp:tools.readonly",
                ["InfraGate:Planner:ClientCredentials:RequireHttpsMetadata"] = "false"
            })
            .Build();

        var options = configuration
            .GetSection(PlannerOptions.SectionName)
            .Get<PlannerOptions>();

        Assert.NotNull(options);
        Assert.Equal("http://gateway:3001/mcp", options!.GatewayBaseUrl);
        Assert.Equal(60, options.AnomalyWallClockCapSeconds);
        Assert.NotNull(options.ClientCredentials);
        Assert.Equal("infra-gate-planner", options.ClientCredentials.ClientId);
        Assert.Equal("s3cr3t", options.ClientCredentials.ClientSecret);
        Assert.Equal("https://idp.example.com/realms/test", options.ClientCredentials.Authority);
        Assert.Equal("mcp:tools.propose mcp:tools.readonly", options.ClientCredentials.Scope);
        Assert.False(options.ClientCredentials.RequireHttpsMetadata);
    }

    [Fact]
    public void Bind_SectionWithoutOptionalKeys_KeepsDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Planner:GatewayBaseUrl"] = "http://gateway:3001/mcp",
                ["InfraGate:Planner:LlmApiKey"] = "key"
            })
            .Build();

        var options = configuration
            .GetSection(PlannerOptions.SectionName)
            .Get<PlannerOptions>();

        Assert.NotNull(options);
        Assert.Equal(PlannerConventions.DefaultAnomalyWallClockCapSeconds, options!.AnomalyWallClockCapSeconds);
        Assert.NotNull(options.ClientCredentials);
    }
}
