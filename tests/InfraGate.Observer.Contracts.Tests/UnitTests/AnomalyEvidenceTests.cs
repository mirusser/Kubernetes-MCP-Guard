using System.Text.Json;

namespace InfraGate.Observer.Contracts.Tests.UnitTests;

public sealed class AnomalyEvidenceTests
{
    [Fact]
    public void AnomalyEvidence_HasRequiredFields()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.PodUnhealthy,
            Target = new ResourceRef { ApiVersion = "v1", Kind = "Pod", Namespace = "default", Name = "my-pod" },
        };

        Assert.Equal(AnomalyKind.PodUnhealthy, evidence.Kind);
        Assert.Equal("my-pod", evidence.Target.Name);
    }

    [Fact]
    public void AnomalyEvidence_OptionalProperties_DefaultToNull()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.DeploymentUnavailable,
            Target = new ResourceRef { ApiVersion = "apps/v1", Kind = "Deployment", Namespace = "default", Name = "my-deploy" },
        };

        Assert.Null(evidence.PodCondition);
        Assert.Null(evidence.RestartCount);
        Assert.Null(evidence.RestartCountSinceLastCycle);
        Assert.False(evidence.IsPending);
        Assert.Null(evidence.PendingDuration);
        Assert.Null(evidence.SpecReplicas);
        Assert.Null(evidence.AvailableReplicas);
        Assert.False(evidence.IsAllPodsAffected);
        Assert.False(evidence.HasHealthySiblings);
        Assert.Null(evidence.EndpointCount);
        Assert.Null(evidence.EventType);
        Assert.Equal(0, evidence.WarningCount);
        Assert.False(evidence.IsSustained);
    }

    [Fact]
    public void AnomalyEvidence_RecordEquality()
    {
        var target = new ResourceRef { ApiVersion = "v1", Kind = "Pod", Namespace = "default", Name = "my-pod" };

        var a = new AnomalyEvidence { Kind = AnomalyKind.PodUnhealthy, Target = target };
        var b = new AnomalyEvidence { Kind = AnomalyKind.PodUnhealthy, Target = target };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void AnomalyEvidence_RecordInequality()
    {
        var target = new ResourceRef { ApiVersion = "v1", Kind = "Pod", Namespace = "default", Name = "my-pod" };

        var a = new AnomalyEvidence { Kind = AnomalyKind.PodUnhealthy, Target = target };
        var b = new AnomalyEvidence { Kind = AnomalyKind.DeploymentUnavailable, Target = target };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AnomalyEvidence_SerializesAndDeserializes()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.WarningEvent,
            Target = new ResourceRef { ApiVersion = "v1", Kind = "Event", Namespace = "default", Name = "evt-1" },
            EventType = "Warning",
            WarningCount = 5,
            IsSustained = true,
        };

        var json = JsonSerializer.Serialize(evidence);
        var deserialized = JsonSerializer.Deserialize<AnomalyEvidence>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(evidence.Kind, deserialized.Kind);
        Assert.Equal(evidence.Target, deserialized.Target);
        Assert.Equal("Warning", deserialized.EventType);
        Assert.Equal(5, deserialized.WarningCount);
        Assert.True(deserialized.IsSustained);
    }
}
