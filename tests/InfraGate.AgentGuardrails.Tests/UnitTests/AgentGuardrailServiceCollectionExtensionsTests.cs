using Microsoft.Extensions.DependencyInjection;

namespace InfraGate.AgentGuardrails.Tests.UnitTests;

public sealed class AgentGuardrailServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAgentGuardrails_RegistersMetricsAsSingleton()
    {
        var services = new ServiceCollection().AddAgentGuardrails();

        var descriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(AgentGuardrailMetrics));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddAgentGuardrails_ReturnsSameInstance_ForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddAgentGuardrails();

        Assert.Same(services, result);
    }
}