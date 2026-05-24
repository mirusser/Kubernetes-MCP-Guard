namespace InfraGate.Observer.Classification;

internal static class SeverityRules
{
    public static readonly TimeSpan PodPendingGracePeriod = TimeSpan.FromMinutes(5);

    public static readonly (Severity Severity, string MatchedRule) Default = (Severity.Low, RuleNames.DefaultRule);

    public static class RuleNames
    {
        public const string ServiceZeroEndpoints = "service-zero-endpoints";
        public const string DeploymentTotalUnavailable = "deployment-total-unavailable";
        public const string DeploymentPartiallyUnavailable = "deployment-partially-unavailable";
        public const string PodAllPodsCriticalCondition = "pod-all-pods-critical-condition";
        public const string PodSingleCriticalCondition = "pod-single-critical-condition";
        public const string PodSingleRestart = "pod-single-restart";
        public const string PodPendingWithinGrace = "pod-pending-within-grace";
        public const string WarningSustained = "warning-sustained";
        public const string WarningOneOff = "warning-one-off";
        public const string DefaultRule = "default-low";
    }
}
