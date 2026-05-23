using static InfraGate.Observer.Classification.SeverityRules;

namespace InfraGate.Observer.Classification;

internal sealed class SeverityClassifier : ISeverityClassifier
{
    public (Severity Severity, string MatchedRule) Classify(AnomalyEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return evidence.Kind switch
        {
            AnomalyKind.ServiceNoEndpoints => ClassifyService(evidence),
            AnomalyKind.DeploymentUnavailable => ClassifyDeployment(evidence),
            AnomalyKind.PodUnhealthy => ClassifyPod(evidence),
            AnomalyKind.WarningEvent => ClassifyEvent(evidence),
            _ => Default,
        };
    }

    private static (Severity, string) ClassifyService(AnomalyEvidence e)
    {
        if (e.EndpointCount == 0)
        {
            return (Severity.High, RuleNames.ServiceZeroEndpoints);
        }

        return Default;
    }

    private static (Severity, string) ClassifyDeployment(AnomalyEvidence e)
    {
        if (e.SpecReplicas > 0 && e.AvailableReplicas == 0)
        {
            return (Severity.High, RuleNames.DeploymentTotalUnavailable);
        }

        if (e.SpecReplicas > 0 && e.AvailableReplicas > 0 && e.AvailableReplicas < e.SpecReplicas)
        {
            return (Severity.Medium, RuleNames.DeploymentPartiallyUnavailable);
        }

        return Default;
    }

    private static (Severity, string) ClassifyPod(AnomalyEvidence e)
    {
        if (e.IsAllPodsAffected && IsCriticalCondition(e.PodCondition))
        {
            return (Severity.High, RuleNames.PodAllPodsCriticalCondition);
        }

        if (!string.IsNullOrEmpty(e.PodCondition) && IsCriticalCondition(e.PodCondition) && e.HasHealthySiblings)
        {
            return (Severity.Medium, RuleNames.PodSingleCriticalCondition);
        }

        if (e.RestartCountSinceLastCycle == 1)
        {
            return (Severity.Low, RuleNames.PodSingleRestart);
        }

        if (e.IsPending && (e.PendingDuration is not { } duration || duration <= PodPendingGracePeriod))
        {
            return (Severity.Low, RuleNames.PodPendingWithinGrace);
        }

        return Default;
    }

    private static (Severity, string) ClassifyEvent(AnomalyEvidence e)
    {
        if (e.IsSustained)
        {
            return (Severity.Medium, RuleNames.WarningSustained);
        }

        return (Severity.Low, RuleNames.WarningOneOff);
    }

    private static bool IsCriticalCondition(string? condition)
    {
        return condition switch
        {
            "CrashLoopBackOff" => true,
            "ImagePullBackOff" => true,
            "OOMKilled" => true,
            "ErrImagePull" => true,
            _ => false,
        };
    }
}
