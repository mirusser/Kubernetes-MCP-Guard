namespace InfraGate.Observer.State;

internal sealed class AnomalyDedupeStore : IAnomalyDedupeStore
{
    private readonly ConcurrentDictionary<DedupKey, ActiveAnomalyState> state = new();
    private long cycleCount;

    public (IReadOnlyList<AnomalyReport> Emitted, IReadOnlyList<AnomalyReport> Resolved, IReadOnlyList<AnomalyReport> Suppressed) ProcessReports(
        string cycleId,
        IReadOnlyList<AnomalyReport> incomingReports,
        int suppressionWindowCycles,
        int resolutionAbsenceThreshold,
        DateTimeOffset detectedAt)
    {
        var currentCycle = Interlocked.Increment(ref cycleCount);

        var seenThisCycle = new HashSet<DedupKey>();
        var emitted = new List<AnomalyReport>();
        var suppressed = new List<AnomalyReport>();

        foreach (var report in incomingReports)
        {
            var key = ComputeKey(report);
            seenThisCycle.Add(key);

            if (state.TryGetValue(key, out var existing))
            {
                existing.LastSeenCycle = currentCycle;
                existing.LastSeverity = report.Severity;

                if (currentCycle - existing.FirstSeenCycle < suppressionWindowCycles)
                {
                    suppressed.Add(report);
                    continue;
                }

                emitted.Add(report);
            }
            else
            {
                state[key] = new ActiveAnomalyState
                {
                    FirstSeenCycle = currentCycle,
                    LastSeenCycle = currentCycle,
                    AnomalyId = report.AnomalyId,
                    LastSeverity = report.Severity,
                };

                emitted.Add(report);
            }
        }

        var resolved = new List<AnomalyReport>();
        foreach (var (key, existing) in state)
        {
            if (seenThisCycle.Contains(key))
            {
                continue;
            }

            if (currentCycle - existing.LastSeenCycle < resolutionAbsenceThreshold)
            {
                continue;
            }

            resolved.Add(CreateResolvedReport(cycleId, detectedAt, key, existing.AnomalyId));
        }

        foreach (var report in resolved)
        {
            var key = ComputeKey(report);
            state.TryRemove(key, out _);
        }

        return (emitted, resolved, suppressed);
    }

    public bool HasActiveAnomaly(DedupKey key) => state.ContainsKey(key);

    private static DedupKey ComputeKey(AnomalyReport report)
    {
        return new DedupKey(report.Kind, report.Target.Kind, report.Target.Namespace, report.Target.Name);
    }

    private static AnomalyReport CreateResolvedReport(
        string cycleId,
        DateTimeOffset detectedAt,
        DedupKey key,
        string anomalyId)
    {
        return new AnomalyReport
        {
            AnomalyId = anomalyId,
            CycleId = cycleId,
            DetectedAt = detectedAt,
            Kind = key.Kind,
            Target = new ResourceRef
            {
                ApiVersion = key.Kind switch
                {
                    AnomalyKind.DeploymentUnavailable => "apps/v1",
                    AnomalyKind.ServiceNoEndpoints => "v1",
                    _ => "v1",
                },
                Kind = key.ResourceKind,
                Namespace = key.Namespace,
                Name = key.Name,
            },
            Severity = Severity.Low,
            Status = AnomalyStatus.Resolved,
            Summary = "Anomaly resolved",
            Evidence = Array.Empty<EvidenceItem>(),
            Annotations = new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }
}
