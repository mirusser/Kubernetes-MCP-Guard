using InfraGate.McpGateway.Auth;
using InfraGate.RuntimeSafety;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class GatewayAuthConventionsTests
{
    [Fact]
    public void RegisterInfraGateEnvVarMappings_NullMappings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GatewayAuthConventions.RegisterInfraGateEnvVarMappings(null!));
    }

    [Theory]
    [InlineData("INFRA_GATE_OAUTH_AUTHORITY", "InfraGate:Auth:OAuthAuthority")]
    [InlineData("INFRA_GATE_OAUTH_METADATA_ADDRESS", "InfraGate:Auth:OAuthMetadataAddress")]
    [InlineData("INFRA_GATE_OAUTH_RESOURCE", "InfraGate:Auth:OAuthResource")]
    [InlineData("INFRA_GATE_OAUTH_SCOPE", "InfraGate:Auth:OAuthScope")]
    [InlineData("INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA", "InfraGate:Auth:OAuthRequireHttpsMetadata")]
    [InlineData("INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID", "InfraGate:Auth:ApprovalOAuthClientId")]
    [InlineData("INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH", "InfraGate:Auth:ApprovalOAuthCallbackPath")]
    [InlineData("INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT", "InfraGate:Auth:ApprovalOAuthAuthorizationEndpoint")]
    [InlineData("INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT", "InfraGate:Auth:ApprovalOAuthTokenEndpoint")]
    public void RegisterInfraGateEnvVarMappings_RegistersExpectedMapping(string envVar, string configKey)
    {
        var mappings = new InfraGateEnvVarMappings();

        GatewayAuthConventions.RegisterInfraGateEnvVarMappings(mappings);

        Assert.Equal(configKey, mappings.GetConfigurationKey(envVar));
    }
}
