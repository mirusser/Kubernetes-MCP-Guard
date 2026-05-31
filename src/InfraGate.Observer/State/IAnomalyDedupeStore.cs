namespace InfraGate.Observer.State;

internal interface IAnomalyDedupeStore
{
    (IReadOnlyList<AnomalyReport> Emitted, IReadOnlyList<AnomalyReport> Resolved, IReadOnlyList<AnomalyReport> Suppressed) ProcessReports(
        string cycleId,
        IReadOnlyList<AnomalyReport> incomingReports,
        int suppressionWindowCycles,
        int resolutionAbsenceThreshold,
        DateTimeOffset detectedAt);

    bool HasActiveAnomaly(DedupKey key);
}
