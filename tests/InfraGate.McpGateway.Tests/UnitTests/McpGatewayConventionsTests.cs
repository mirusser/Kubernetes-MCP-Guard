using InfraGate.RuntimeSafety;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class McpGatewayConventionsTests
{
    [Fact]
    public void RegisterInfraGateEnvVarMappings_NullMappings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            McpGatewayConventions.RegisterInfraGateEnvVarMappings(null!));
    }

    [Theory]
    [InlineData("ASPNETCORE_URLS", "InfraGate:Gateway:AspNetCoreUrls")]
    [InlineData("INFRA_GATE_DOWNSTREAM_ASSEMBLY", "InfraGate:Gateway:DownstreamAssembly")]
    [InlineData("INFRA_GATE_DOWNSTREAM_PROJECT", "InfraGate:Gateway:DownstreamProject")]
    [InlineData("INFRA_GATE_GUARD_AUDIT_ROOT", "InfraGate:Gateway:GuardAuditRoot")]
    [InlineData("INFRA_GATE_APPROVAL_BASE_URL", "InfraGate:Approval:BaseUrl")]
    [InlineData("INFRA_GATE_APPROVAL_CHALLENGE_TTL_SECONDS", "InfraGate:Approval:ChallengeTtlSeconds")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_REQUIRED", "InfraGate:DownstreamAuth:Required")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_AUTHORITY", "InfraGate:DownstreamAuth:Authority")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_METADATA_ADDRESS", "InfraGate:DownstreamAuth:MetadataAddress")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_REQUIRE_HTTPS_METADATA", "InfraGate:DownstreamAuth:RequireHttpsMetadata")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_AUDIENCE", "InfraGate:DownstreamAuth:Audience")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_SCOPE", "InfraGate:DownstreamAuth:Scope")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_GATEWAY_CLIENT_ID", "InfraGate:DownstreamAuth:GatewayClientId")]
    [InlineData("INFRA_GATE_DOWNSTREAM_AUTH_GATEWAY_CLIENT_SECRET", "InfraGate:DownstreamAuth:GatewayClientSecret")]
    public void RegisterInfraGateEnvVarMappings_RegistersExpectedMapping(string envVar, string configKey)
    {
        var mappings = new InfraGateEnvVarMappings();

        McpGatewayConventions.RegisterInfraGateEnvVarMappings(mappings);

        Assert.Equal(configKey, mappings.GetConfigurationKey(envVar));
    }
}
