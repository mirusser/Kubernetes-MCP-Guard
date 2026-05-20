using InfraGate.McpGateway;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class InfraGateGatewaySettingsTests
{
    [Fact]
    public void BindFromConfiguration_PopulatesAllFields()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Gateway:AspNetCoreUrls"] = "http://0.0.0.0:3001",
                ["InfraGate:Gateway:DownstreamAssembly"] = "/app/server.dll",
                ["InfraGate:Gateway:GuardAuditRoot"] = "/data/guardrails"
            })
            .Build();

        var settings = configuration
            .GetSection("InfraGate:Gateway")
            .Get<InfraGateGatewaySettings>();

        Assert.NotNull(settings);
        Assert.Equal("http://0.0.0.0:3001", settings!.AspNetCoreUrls);
        Assert.Equal("/app/server.dll", settings.DownstreamAssembly);
        Assert.Equal("/data/guardrails", settings.GuardAuditRoot);
    }

    [Fact]
    public void BindFromConfiguration_MissingValues_AreNull()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraGate:Gateway:DownstreamAssembly"] = "/app/server.dll"
            })
            .Build();

        var settings = configuration
            .GetSection("InfraGate:Gateway")
            .Get<InfraGateGatewaySettings>();

        Assert.NotNull(settings);
        Assert.Null(settings!.AspNetCoreUrls);
        Assert.Equal("/app/server.dll", settings.DownstreamAssembly);
        Assert.Null(settings.GuardAuditRoot);
    }
}
