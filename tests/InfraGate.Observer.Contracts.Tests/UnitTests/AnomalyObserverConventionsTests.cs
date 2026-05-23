using InfraGate.Observer.Contracts;

namespace InfraGate.Observer.Contracts.Tests.UnitTests;

public sealed class AnomalyObserverConventionsTests
{
    [Fact]
    public void DefaultCadenceSeconds_Is60()
    {
        Assert.Equal(60, AnomalyObserverConventions.DefaultCadenceSeconds);
    }

    [Fact]
    public void MinCadenceSeconds_Is10()
    {
        Assert.Equal(10, AnomalyObserverConventions.MinCadenceSeconds);
    }

    [Fact]
    public void MaxCadenceSeconds_Is3600()
    {
        Assert.Equal(3600, AnomalyObserverConventions.MaxCadenceSeconds);
    }

    [Fact]
    public void WallClockCapSeconds_Is20()
    {
        Assert.Equal(20, AnomalyObserverConventions.WallClockCapSeconds);
    }

    [Fact]
    public void MaxToolIterations_Is8()
    {
        Assert.Equal(8, AnomalyObserverConventions.MaxToolIterations);
    }
}
