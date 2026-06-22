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
                ["InfraGate:Gateway:DownstreamAssemblyHash"] = "a3e5f8c9d2b1e4076f5a3c8e1d0b9a2c7f4e6d5b8c3a1f0e9d7b6c5a4f3e2d1b0",
                ["InfraGate:Gateway:GuardAuditRoot"] = "/data/guardrails"
            })
            .Build();

        var settings = configuration
            .GetSection("InfraGate:Gateway")
            .Get<InfraGateGatewaySettings>();

        Assert.NotNull(settings);
        Assert.Equal("http://0.0.0.0:3001", settings!.AspNetCoreUrls);
        Assert.Equal("/app/server.dll", settings.DownstreamAssembly);
        Assert.Equal("a3e5f8c9d2b1e4076f5a3c8e1d0b9a2c7f4e6d5b8c3a1f0e9d7b6c5a4f3e2d1b0", settings.DownstreamAssemblyHash);
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
