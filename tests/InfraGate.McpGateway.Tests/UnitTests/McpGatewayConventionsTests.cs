namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class McpGatewayConventionsTests
{
    [Fact]
    public void Health_LivenessPath_IsSlashHealthz()
    {
        Assert.Equal("/healthz", McpGatewayConventions.Health.LivenessPath);
    }

    [Fact]
    public void Health_ReadinessPath_IsSlashReadyz()
    {
        Assert.Equal("/readyz", McpGatewayConventions.Health.ReadinessPath);
    }
}
