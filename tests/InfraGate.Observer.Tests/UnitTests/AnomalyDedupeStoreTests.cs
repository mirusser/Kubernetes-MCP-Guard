using InfraGate.Observer.State;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class AnomalyDedupeStoreTests
{
    private static AnomalyReport CreateReport(
        AnomalyKind kind,
        string resourceKind,
        string namespaceName,
        string name,
        string apiVersion = "v1",
        Severity severity = Severity.High)
    {
        var target = new ResourceRef
        {
            ApiVersion = apiVersion,
            Kind = resourceKind,
            Namespace = namespaceName,
            Name = name,
        };

        return new AnomalyReport
        {
            AnomalyId = AnomalyObserverConventions.ComputeAnomalyId(kind, target),
            CycleId = Guid.NewGuid().ToString("D"),
            DetectedAt = DateTimeOffset.UtcNow,
            Kind = kind,
            Target = target,
            Severity = severity,
            Status = AnomalyStatus.Active,
            Summary = "Test anomaly",
            Evidence = Array.Empty<EvidenceItem>(),
            Annotations = new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    private static DedupKey Key(AnomalyReport report) =>
        new(report.Kind, report.Target.Kind, report.Target.Namespace, report.Target.Name);

    // ── First occurrence ───────────────────────────────────────

    [Fact]
    public void ProcessReports_FirstOccurrence_EmitsReport()
    {
        var store = new AnomalyDedupeStore();
        var report = CreateReport(AnomalyKind.PodUnhealthy, "Pod", "default", "crash-pod");
        var detectedAt = DateTimeOffset.UtcNow;

        var (emitted, resolved) = store.ProcessReports(
            Guid.NewGuid().ToString("D"),
            new[] { report },
            suppressionWindowCycles: 5,
            resolutionAbsenceThreshold: 2,
            detectedAt);

        Assert.Single(emitted);
        Assert.Equal(report.AnomalyId, emitted[0].AnomalyId);
        Assert.Empty(resolved);
    }

    [Fact]
    public void ProcessReports_FirstOccurrence_StoresState()
    {
        var store = new AnomalyDedupeStore();
        var report = CreateReport(AnomalyKind.PodUnhealthy, "Pod", "default", "crash-pod");
        var detectedAt = DateTimeOffset.UtcNow;

        store.ProcessReports(
            Guid.NewGuid().ToString("D"),
            new[] { report },
            suppressionWindowCycles: 5,
            resolutionAbsenceThreshold: 2,
            detectedAt);

        var key = Key(report);
        Assert.True(store.HasActiveAnomaly(key));
    }

    // ── Suppression window ───────────────────────────────────────

    [Fact]
    public void ProcessReports_WithinSuppressionWindow_SuppressesSecondEmission()
    {
        var store = new AnomalyDedupeStore();
        var report = CreateReport(AnomalyKind.PodUnhealthy, "Pod", "default", "crash-pod");
        var detectedAt = DateTimeOffset.UtcNow;
        const int window = 5;

        // Cycle 1: first occurrence → emitted
        var (firstEmitted, _) = store.ProcessReports(
            Guid.NewGuid().ToString("D"),
            new[] { report },
            suppressionWindowCycles: window,
            resolutionAbsenceThreshold: 2,
            detectedAt);
        Assert.Single(firstEmitted);

        // Cycles 2-5: within suppression window → suppressed
        for (var i = 0; i < window - 1; i++)
        {
            var (emitted, _) = store.ProcessReports(
                Guid.NewGuid().ToString("D"),
                new[] { report },
                suppressionWindowCycles: window,
                resolutionAbsenceThreshold: 2,
                detectedAt);

            Assert.Empty(emitted);
        }
    }

    [Fact]
    public void ProcessReports_AfterSuppressionWindow_ReEmits()
    {
        var store = new AnomalyDedupeStore();
        var report = CreateReport(AnomalyKind.PodUnhealthy, "Pod", "default", "crash-pod");
        var detectedAt = DateTimeOffset.UtcNow;
        const int window = 3;

        // Cycle 1: first occurrence
        store.ProcessReports("c1", new[] { report }, window, 2, detectedAt);

        // Cycles 2-3: suppressed (window = 3, so cycles 1-3 inclusive)
        for (var i = 0; i < window - 1; i++)
        {
            var (emitted, _) = store.ProcessReports(
                Guid.NewGuid().ToString("D"),
                new[] { report },
                window,
                2,
                detectedAt);
            Assert.Empty(emitted);
        }

        // Cycle 4: outside window → re-emitted
        var (fourthEmitted, _) = store.ProcessReports("c4", new[] { report }, window, 2, detectedAt);
        Assert.Single(fourthEmitted);
    }

    // ── Resolution emission ───────────────────────────────────────

    [Fact]
    public void ProcessReports_AbsentForTwoCycles_EmitsResolved()
    {
        var store = new AnomalyDedupeStore();
        var report = CreateReport(AnomalyKind.PodUnhealthy, "Pod", "default", "crash-pod");
        var detectedAt = DateTimeOffset.UtcNow;

        // Cycle 1: anomaly detected
        var (firstEmitted, firstResolved) = store.ProcessReports("c1", new[] { report }, 5, 2, detectedAt);
        Assert.Single(firstEmitted);
        Assert.Empty(firstResolved);

        // Cycle 2: absent (no reports for this key) — one cycle absent
        var (c2Emitted, c2Resolved) = store.ProcessReports("c2", Array.Empty<AnomalyReport>(), 5, 2, detectedAt);
        Assert.Empty(c2Emitted);
        Assert.Empty(c2Resolved); // not yet 2 cycles absent

        // Cycle 3: still absent — second cycle → resolved
        var (c3Emitted, c3Resolved) = store.ProcessReports("c3", Array.Empty<AnomalyReport>(), 5, 2, detectedAt);
        Assert.Empty(c3Emitted);
        Assert.Single(c3Resolved);
        Assert.Equal(AnomalyStatus.Resolved, c3Resolved[0].Status);
        Assert.Equal(Severity.Low, c3Resolved[0].Severity);
        Assert.Equal(report.AnomalyId, c3Resolved[0].AnomalyId);
    }

    [Fact]
    public void ProcessReports_ResolvedEntry_IsRemovedFromState()
    {
        var store = new AnomalyDedupeStore();
        var report = CreateReport(AnomalyKind.PodUnhealthy, "Pod", "default", "crash-pod");
        var key = Key(report);
        var detectedAt = DateTimeOffset.UtcNow;

        // Cycle 1: detected
        store.ProcessReports("c1", new[] { report }, 5, 2, detectedAt);
        Assert.True(store.HasActiveAnomaly(key));

        // Cycle 2-3: absent → resolved
        store.ProcessReports("c2", Array.Empty<AnomalyReport>(), 5, 2, detectedAt);
        store.ProcessReports("c3", Array.Empty<AnomalyReport>(), 5, 2, detectedAt);

        Assert.False(store.HasActiveAnomaly(key));
    }

    [Fact]
    public void ProcessReports_ReappearsBeforeResolved_ResetsAbsenceCounter()
    {
        var store = new AnomalyDedupeStore();
        var report = CreateReport(AnomalyKind.PodUnhealthy, "Pod", "default", "crash-pod");
        var detectedAt = DateTimeOffset.UtcNow;

        // Cycle 1: detected
        store.ProcessReports("c1", new[] { report }, 5, 2, detectedAt);

        // Cycle 2: absent (one cycle absent)
        store.ProcessReports("c2", Array.Empty<AnomalyReport>(), 5, 2, detectedAt);

        // Cycle 3: reappears — resets LastSeenCycle
        var (c3Emitted, c3Resolved) = store.ProcessReports("c3", new[] { report }, 5, 2, detectedAt);
        Assert.Empty(c3Emitted); // still suppressed (within window)
        Assert.Empty(c3Resolved); // not resolved since it reappeared

        // Cycles 4-5: absent again → should need 2 more cycles
        store.ProcessReports("c4", Array.Empty<AnomalyReport>(), 5, 2, detectedAt);
        var (c5Emitted, c5Resolved) = store.ProcessReports("c5", Array.Empty<AnomalyReport>(), 5, 2, detectedAt);
        Assert.Empty(c5Emitted);
        Assert.Single(c5Resolved); // now 2 cycles absent since re-appearance
    }

    // ── Multiple different anomalies ───────────────────────────────────────

    [Fact]
    public void ProcessReports_DifferentAnomalies_HaveIndependentState()
    {
        var store = new AnomalyDedupeStore();
        var podReport = CreateReport(AnomalyKind.PodUnhealthy, "Pod", "default", "pod-a");
        var deploymentReport = CreateReport(AnomalyKind.DeploymentUnavailable, "Deployment", "default", "nginx");
        var detectedAt = DateTimeOffset.UtcNow;

        // Both first seen → both emitted
        var (emitted, _) = store.ProcessReports(
            "c1",
            new[] { podReport, deploymentReport },
            5, 2, detectedAt);

        Assert.Equal(2, emitted.Count);

        // Next cycle: pod suppresses, deployment absent
        var (c2Emitted, _) = store.ProcessReports(
            "c2",
            new[] { podReport }, // only pod seen, deployment absent
            5, 2, detectedAt);

        Assert.Empty(c2Emitted); // pod suppressed
    }

    [Fact]
    public void ProcessReports_MultipleNamespaces_IndependentState()
    {
        var store = new AnomalyDedupeStore();
        var ns1Report = CreateReport(AnomalyKind.PodUnhealthy, "Pod", "ns1", "pod-a");
        var ns2Report = CreateReport(AnomalyKind.PodUnhealthy, "Pod", "ns2", "pod-a");
        var detectedAt = DateTimeOffset.UtcNow;

        var (emitted, _) = store.ProcessReports("c1", new[] { ns1Report, ns2Report }, 5, 2, detectedAt);
        Assert.Equal(2, emitted.Count);
        Assert.NotEqual(emitted[0].AnomalyId, emitted[1].AnomalyId);
    }

    // ── Empty cycles ───────────────────────────────────────

    [Fact]
    public void ProcessReports_EmptyInput_DoesNotCrash()
    {
        var store = new AnomalyDedupeStore();
        var detectedAt = DateTimeOffset.UtcNow;

        var (emitted, resolved) = store.ProcessReports(
            "c1",
            Array.Empty<AnomalyReport>(),
            5, 2, detectedAt);

        Assert.Empty(emitted);
        Assert.Empty(resolved);
    }

    // ── Concurrent access ───────────────────────────────────────

    [Fact]
    public async Task ProcessReports_ConcurrentCycles_RemainsConsistent()
    {
        var store = new AnomalyDedupeStore();
        var reports = Enumerable.Range(0, 100)
            .Select(i => CreateReport(AnomalyKind.PodUnhealthy, "Pod", "default", $"pod-{i}"))
            .ToList();

        var tasks = new Task[5];
        for (var t = 0; t < tasks.Length; t++)
        {
            var cycleId = $"c{t}";
            tasks[t] = Task.Run(() =>
            {
                store.ProcessReports(cycleId, reports, 5, 2, DateTimeOffset.UtcNow);
            });
        }

        await Task.WhenAll(tasks);

        foreach (var report in reports)
        {
            Assert.True(store.HasActiveAnomaly(Key(report)));
        }
    }

    // ── Severity updates ───────────────────────────────────────

    [Fact]
    public void ProcessReports_ReappearingAnomaly_UpdatesLastSeverity()
    {
        var store = new AnomalyDedupeStore();
        var report = CreateReport(AnomalyKind.PodUnhealthy, "Pod", "default", "crash-pod", severity: Severity.Medium);
        var detectedAt = DateTimeOffset.UtcNow;

        // First occurrence
        store.ProcessReports("c1", new[] { report }, 5, 2, detectedAt);

        // After suppression window, re-emit with different severity
        // Window = 2 means first occurrence at cycle 1, suppressed at cycle 2, re-emit at cycle 3
        store.ProcessReports("c2", new[] { report }, 2, 2, detectedAt); // suppressed
        var updatedReport = CreateReport(AnomalyKind.PodUnhealthy, "Pod", "default", "crash-pod", severity: Severity.High);
        var (emitted, _) = store.ProcessReports("c3", new[] { updatedReport }, 2, 2, detectedAt);

        Assert.Single(emitted);
        Assert.Equal(Severity.High, emitted[0].Severity);
    }
}
