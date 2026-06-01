using InfraGate.Executor.Handoff;
using Microsoft.Extensions.Options;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ExecutorConcurrencyGateTests
{
    [Fact]
    public void TryAcquire_AtCapacity_ReturnsFalseUntilReleased()
    {
        using var gate = new ExecutorConcurrencyGate(
            Options.Create(new ExecutorOptions { GatewayBaseUrl = "http://localhost", ConcurrencyCap = 1 }));

        Assert.True(gate.TryAcquire());
        Assert.False(gate.TryAcquire());

        gate.Release();

        Assert.True(gate.TryAcquire());
    }
}
