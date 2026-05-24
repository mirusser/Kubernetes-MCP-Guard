using InfraGate.Observer.Classification;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class SeverityClassifierTests
{
    private readonly ISeverityClassifier classifier = new SeverityClassifier();

    private static ResourceRef SampleTarget(string kind = "Pod", string name = "test-pod", string ns = "default")
    {
        return new ResourceRef
        {
            ApiVersion = "v1",
            Kind = kind,
            Namespace = ns,
            Name = name,
        };
    }

    // ── Service ──────────────────────────────────────────────

    [Fact]
    public void Classify_ServiceZeroEndpoints_High()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.ServiceNoEndpoints,
            Target = SampleTarget("Service", "my-svc"),
            EndpointCount = 0,
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.High, severity);
        Assert.Equal(SeverityRules.RuleNames.ServiceZeroEndpoints, rule);
    }

    [Fact]
    public void Classify_ServiceWithEndpoints_DefaultLow()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.ServiceNoEndpoints,
            Target = SampleTarget("Service", "my-svc"),
            EndpointCount = 3,
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Low, severity);
        Assert.Equal(SeverityRules.RuleNames.DefaultRule, rule);
    }

    // ── Deployment ───────────────────────────────────────────

    [Fact]
    public void Classify_DeploymentTotalUnavailable_High()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.DeploymentUnavailable,
            Target = SampleTarget("Deployment", "nginx"),
            SpecReplicas = 3,
            AvailableReplicas = 0,
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.High, severity);
        Assert.Equal(SeverityRules.RuleNames.DeploymentTotalUnavailable, rule);
    }

    [Fact]
    public void Classify_DeploymentPartiallyUnavailable_Medium()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.DeploymentUnavailable,
            Target = SampleTarget("Deployment", "nginx"),
            SpecReplicas = 3,
            AvailableReplicas = 1,
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Medium, severity);
        Assert.Equal(SeverityRules.RuleNames.DeploymentPartiallyUnavailable, rule);
    }

    [Fact]
    public void Classify_DeploymentAllAvailable_DefaultLow()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.DeploymentUnavailable,
            Target = SampleTarget("Deployment", "nginx"),
            SpecReplicas = 3,
            AvailableReplicas = 3,
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Low, severity);
        Assert.Equal(SeverityRules.RuleNames.DefaultRule, rule);
    }

    [Fact]
    public void Classify_DeploymentZeroSpec_DefaultLow()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.DeploymentUnavailable,
            Target = SampleTarget("Deployment", "nginx"),
            SpecReplicas = 0,
            AvailableReplicas = 0,
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Low, severity);
        Assert.Equal(SeverityRules.RuleNames.DefaultRule, rule);
    }

    // ── Pod ──────────────────────────────────────────────────

    [Theory]
    [InlineData("CrashLoopBackOff")]
    [InlineData("ImagePullBackOff")]
    public void Classify_PodAllPodsCriticalCondition_High(string condition)
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.PodUnhealthy,
            Target = SampleTarget(),
            PodCondition = condition,
            IsAllPodsAffected = true,
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.High, severity);
        Assert.Equal(SeverityRules.RuleNames.PodAllPodsCriticalCondition, rule);
    }

    [Theory]
    [InlineData("CrashLoopBackOff")]
    [InlineData("ImagePullBackOff")]
    [InlineData("OOMKilled")]
    [InlineData("ErrImagePull")]
    public void Classify_PodSingleCriticalConditionWithHealthySiblings_Medium(string condition)
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.PodUnhealthy,
            Target = SampleTarget(),
            PodCondition = condition,
            HasHealthySiblings = true,
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Medium, severity);
        Assert.Equal(SeverityRules.RuleNames.PodSingleCriticalCondition, rule);
    }

    [Fact]
    public void Classify_PodSingleCriticalConditionNoSiblings_DefaultLow()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.PodUnhealthy,
            Target = SampleTarget(),
            PodCondition = "OOMKilled",
            HasHealthySiblings = false,
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Low, severity);
        Assert.Equal(SeverityRules.RuleNames.DefaultRule, rule);
    }

    [Fact]
    public void Classify_PodSingleRestart_ReturnsLow()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.PodUnhealthy,
            Target = SampleTarget(),
            RestartCountSinceLastCycle = 1,
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Low, severity);
        Assert.Equal(SeverityRules.RuleNames.PodSingleRestart, rule);
    }

    [Fact]
    public void Classify_PodPendingWithinGrace_ReturnsLow()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.PodUnhealthy,
            Target = SampleTarget(),
            IsPending = true,
            PendingDuration = TimeSpan.FromMinutes(2),
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Low, severity);
        Assert.Equal(SeverityRules.RuleNames.PodPendingWithinGrace, rule);
    }

    [Fact]
    public void Classify_PodPendingNullDuration_ReturnsLow()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.PodUnhealthy,
            Target = SampleTarget(),
            IsPending = true,
            PendingDuration = null,
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Low, severity);
        Assert.Equal(SeverityRules.RuleNames.PodPendingWithinGrace, rule);
    }

    [Fact]
    public void Classify_PodPendingBeyondGrace_DefaultLow()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.PodUnhealthy,
            Target = SampleTarget(),
            IsPending = true,
            PendingDuration = TimeSpan.FromMinutes(10),
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Low, severity);
        Assert.Equal(SeverityRules.RuleNames.DefaultRule, rule);
    }

    [Fact]
    public void Classify_PodNoSignificantIssue_DefaultLow()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.PodUnhealthy,
            Target = SampleTarget(),
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Low, severity);
        Assert.Equal(SeverityRules.RuleNames.DefaultRule, rule);
    }

    // ── Warning Events ───────────────────────────────────────

    [Fact]
    public void Classify_SustainedWarningEvent_Medium()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.WarningEvent,
            Target = SampleTarget(),
            IsSustained = true,
            EventType = "BackOff",
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Medium, severity);
        Assert.Equal(SeverityRules.RuleNames.WarningSustained, rule);
    }

    [Fact]
    public void Classify_OneOffWarningEvent_Low()
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.WarningEvent,
            Target = SampleTarget(),
            IsSustained = false,
            EventType = "FailedScheduling",
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.Low, severity);
        Assert.Equal(SeverityRules.RuleNames.WarningOneOff, rule);
    }

    // ── AllPodsAffected wins over single-pod for High ────────

    [Theory]
    [InlineData("CrashLoopBackOff")]
    [InlineData("ImagePullBackOff")]
    public void Classify_AllPodsAffectedWinsOverHealthySiblings(string condition)
    {
        var evidence = new AnomalyEvidence
        {
            Kind = AnomalyKind.PodUnhealthy,
            Target = SampleTarget(),
            PodCondition = condition,
            IsAllPodsAffected = true,
            HasHealthySiblings = true,
        };

        var (severity, rule) = classifier.Classify(evidence);

        Assert.Equal(Severity.High, severity);
    }

    // ── Edge Cases ───────────────────────────────────────────

    [Fact]
    public void Classify_NullEvidence_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => classifier.Classify(null!));
    }

    // ── Coverage: verify all RuleNames constants are used ────

    [Fact]
    public void AllRuleNames_AreReachable()
    {
        var ruleNames = new[]
        {
            SeverityRules.RuleNames.ServiceZeroEndpoints,
            SeverityRules.RuleNames.DeploymentTotalUnavailable,
            SeverityRules.RuleNames.DeploymentPartiallyUnavailable,
            SeverityRules.RuleNames.PodAllPodsCriticalCondition,
            SeverityRules.RuleNames.PodSingleCriticalCondition,
            SeverityRules.RuleNames.PodSingleRestart,
            SeverityRules.RuleNames.PodPendingWithinGrace,
            SeverityRules.RuleNames.WarningSustained,
            SeverityRules.RuleNames.WarningOneOff,
            SeverityRules.RuleNames.DefaultRule,
        };

        Assert.Equal(10, ruleNames.Distinct().Count());
    }
}
