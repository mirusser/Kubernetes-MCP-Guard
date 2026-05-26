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
    public void WallClockCapSeconds_Is120()
    {
        Assert.Equal(120, AnomalyObserverConventions.WallClockCapSeconds);
    }

    [Fact]
    public void MaxToolIterations_Is8()
    {
        Assert.Equal(8, AnomalyObserverConventions.MaxToolIterations);
    }

    [Fact]
    public void MinWallClockCapSeconds_Is10()
    {
        Assert.Equal(10, AnomalyObserverConventions.MinWallClockCapSeconds);
    }

    [Fact]
    public void MaxWallClockCapSeconds_Is300()
    {
        Assert.Equal(300, AnomalyObserverConventions.MaxWallClockCapSeconds);
    }

    [Fact]
    public void MinMaxToolIterations_Is1()
    {
        Assert.Equal(1, AnomalyObserverConventions.MinMaxToolIterations);
    }

    [Fact]
    public void MaxMaxToolIterations_Is20()
    {
        Assert.Equal(20, AnomalyObserverConventions.MaxMaxToolIterations);
    }

    [Fact]
    public void DefaultDedupeSuppressionWindow_Is5()
    {
        Assert.Equal(5, AnomalyObserverConventions.DefaultDedupeSuppressionWindow);
    }

    [Fact]
    public void MinDedupeSuppressionWindow_Is1()
    {
        Assert.Equal(1, AnomalyObserverConventions.MinDedupeSuppressionWindow);
    }

    [Fact]
    public void MaxDedupeSuppressionWindow_Is30()
    {
        Assert.Equal(30, AnomalyObserverConventions.MaxDedupeSuppressionWindow);
    }

    [Fact]
    public void DefaultDedupeResolutionThreshold_Is2()
    {
        Assert.Equal(2, AnomalyObserverConventions.DefaultDedupeResolutionThreshold);
    }

    [Fact]
    public void MinDedupeResolutionThreshold_Is1()
    {
        Assert.Equal(1, AnomalyObserverConventions.MinDedupeResolutionThreshold);
    }

    [Fact]
    public void MaxDedupeResolutionThreshold_Is10()
    {
        Assert.Equal(10, AnomalyObserverConventions.MaxDedupeResolutionThreshold);
    }

    [Fact]
    public void DefaultLlmModel_IsClaudeSonnet46()
    {
        Assert.Equal("claude-sonnet-4-6", AnomalyObserverConventions.DefaultLlmModel);
    }

    [Fact]
    public void ComputeAnomalyId_WithResourceRef_IsDeterministic()
    {
        var target = new ResourceRef { ApiVersion = "v1", Kind = "Pod", Namespace = "default", Name = "my-pod" };

        var id1 = AnomalyObserverConventions.ComputeAnomalyId(AnomalyKind.PodUnhealthy, target);
        var id2 = AnomalyObserverConventions.ComputeAnomalyId(AnomalyKind.PodUnhealthy, target);

        Assert.Equal(id1, id2);
    }

    [Fact]
    public void ComputeAnomalyId_DifferentInputs_ProduceDifferentIds()
    {
        var targetA = new ResourceRef { ApiVersion = "v1", Kind = "Pod", Namespace = "default", Name = "pod-a" };
        var targetB = new ResourceRef { ApiVersion = "v1", Kind = "Pod", Namespace = "default", Name = "pod-b" };

        var idA = AnomalyObserverConventions.ComputeAnomalyId(AnomalyKind.PodUnhealthy, targetA);
        var idB = AnomalyObserverConventions.ComputeAnomalyId(AnomalyKind.PodUnhealthy, targetB);

        Assert.NotEqual(idA, idB);
    }

    [Fact]
    public void ComputeAnomalyId_Returns12CharacterLowercaseHex()
    {
        var target = new ResourceRef { ApiVersion = "v1", Kind = "Pod", Namespace = "default", Name = "my-pod" };

        var id = AnomalyObserverConventions.ComputeAnomalyId(AnomalyKind.PodUnhealthy, target);

        Assert.Equal(12, id.Length);
        Assert.Matches("^[0-9a-f]{12}$", id);
    }

    [Fact]
    public void ComputeAnomalyId_NullResourceRef_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AnomalyObserverConventions.ComputeAnomalyId(AnomalyKind.PodUnhealthy, null!));
    }

    [Fact]
    public void ComputeAnomalyId_WithExplicitComponents_IsDeterministic()
    {
        var id1 = AnomalyObserverConventions.ComputeAnomalyId(AnomalyKind.PodUnhealthy, "v1", "Pod", "default", "my-pod");
        var id2 = AnomalyObserverConventions.ComputeAnomalyId(AnomalyKind.PodUnhealthy, "v1", "Pod", "default", "my-pod");

        Assert.Equal(id1, id2);
    }

    [Fact]
    public void ComputeAnomalyId_Overloads_AreConsistent()
    {
        var target = new ResourceRef { ApiVersion = "apps/v1", Kind = "Deployment", Namespace = "prod", Name = "web" };

        var fromRef = AnomalyObserverConventions.ComputeAnomalyId(AnomalyKind.DeploymentUnavailable, target);
        var fromComponents = AnomalyObserverConventions.ComputeAnomalyId(AnomalyKind.DeploymentUnavailable, "apps/v1", "Deployment", "prod", "web");

        Assert.Equal(fromRef, fromComponents);
    }
}
