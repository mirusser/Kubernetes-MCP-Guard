using InfraGate.Observer.Cycle;
using InfraGate.Observer.Diagnostics;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserverLogEventsTests
{
    [Fact]
    public void LogObserverStarting_LogsAtInformationLevel_WithCadenceSeconds()
    {
        var logger = new CapturingLogger<ObservationCycleLoop>();
        ObserverLogEvents.LogObserverStarting(logger, 60);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal(60, logger.Entries[0].Properties["CadenceSeconds"]);
    }

    [Fact]
    public void LogObserverStopping_LogsAtInformationLevel()
    {
        var logger = new CapturingLogger<ObservationCycleLoop>();
        ObserverLogEvents.LogObserverStopping(logger);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
    }

    [Fact]
    public void LogCycleCompleted_LogsAllProperties()
    {
        var logger = new CapturingLogger<ObservationCycleLoop>();
        ObserverLogEvents.LogCycleCompleted(logger, "abc-123", 2, 1500L);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("abc-123", logger.Entries[0].Properties["CycleId"]);
        Assert.Equal(2, logger.Entries[0].Properties["ReportCount"]);
        Assert.Equal(1500L, logger.Entries[0].Properties["DurationMs"]);
    }

    [Fact]
    public void LogCycleCompletedDetailed_LogsAllProperties()
    {
        var logger = new CapturingLogger<ObservationCycleRunner>();
        ObserverLogEvents.LogCycleCompletedDetailed(logger, "abc-123", 3, 2, 1, 5, 1, 1500L);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("abc-123", logger.Entries[0].Properties["CycleId"]);
        Assert.Equal(3, logger.Entries[0].Properties["ReportCount"]);
        Assert.Equal(2, logger.Entries[0].Properties["Emitted"]);
        Assert.Equal(1, logger.Entries[0].Properties["Resolved"]);
        Assert.Equal(5, logger.Entries[0].Properties["ToolCalls"]);
        Assert.Equal(1, logger.Entries[0].Properties["Disagreements"]);
        Assert.Equal(1500L, logger.Entries[0].Properties["DurationMs"]);
    }

    [Fact]
    public void LogCycleTruncated_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger<ObservationCycleLoop>();
        ObserverLogEvents.LogCycleTruncatedWithDetails(logger, "abc-123", 5, 2000L);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("abc-123", logger.Entries[0].Properties["CycleId"]);
        Assert.Equal(5, logger.Entries[0].Properties["ToolCalls"]);
        Assert.Equal(2000L, logger.Entries[0].Properties["DurationMs"]);
    }

    [Fact]
    public void LogCycleCancelled_LogsAtInformationLevel()
    {
        var logger = new CapturingLogger<ObservationCycleLoop>();
        ObserverLogEvents.LogCycleCancelled(logger);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
    }

    [Fact]
    public void LogCycleSkipped_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger<ObservationCycleLoop>();
        ObserverLogEvents.LogCycleSkipped(logger);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
    }

    [Fact]
    public void LogCycleError_LogsAtErrorLevel_WithException()
    {
        var logger = new CapturingLogger<ObservationCycleLoop>();
        var ex = new InvalidOperationException("test error");
        ObserverLogEvents.LogCycleError(logger, ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogTruncatedNoReports_LogsAllProperties()
    {
        var logger = new CapturingLogger<ObservationCycleRunner>();
        ObserverLogEvents.LogTruncatedNoReports(logger, "abc-123", 4);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("abc-123", logger.Entries[0].Properties["CycleId"]);
        Assert.Equal(4, logger.Entries[0].Properties["ToolCalls"]);
    }

    [Fact]
    public void LogSeverityDisagreement_LogsAllProperties()
    {
        var logger = new CapturingLogger<ObservationCycleRunner>();
        ObserverLogEvents.LogSeverityDisagreement(logger, "High", "Medium", "pod-single-critical-condition", "PodUnhealthy", "Pod/crash-pod");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("High", logger.Entries[0].Properties["LlmSeverity"]);
        Assert.Equal("Medium", logger.Entries[0].Properties["ClassifierSeverity"]);
        Assert.Equal("pod-single-critical-condition", logger.Entries[0].Properties["Rule"]);
        Assert.Equal("PodUnhealthy", logger.Entries[0].Properties["Kind"]);
        Assert.Equal("Pod/crash-pod", logger.Entries[0].Properties["Target"]);
    }

    [Fact]
    public void LogToolCallFailed_LogsWithException()
    {
        var logger = new CapturingLogger<ObservationCycleRunner>();
        var ex = new HttpRequestException("network error");
        ObserverLogEvents.LogToolCallFailed(logger, "get_k8s_status", ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("get_k8s_status", logger.Entries[0].Properties["ToolName"]);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogSnapshotFetchFailed_LogsAllProperties()
    {
        var logger = new CapturingLogger<SnapshotFetcher>();
        var ex = new InvalidOperationException("timeout");
        ObserverLogEvents.LogSnapshotFetchFailed(logger, "get_k8s_events", "default", ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("get_k8s_events", logger.Entries[0].Properties["ToolName"]);
        Assert.Equal("default", logger.Entries[0].Properties["Namespace"]);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogJsonArrayExtractFailed_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger<ObservationCycleRunner>();
        ObserverLogEvents.LogJsonArrayExtractFailed(logger, "default");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("default", logger.Entries[0].Properties["Namespace"]);
    }

    [Fact]
    public void LogJsonParseFailed_LogsWithException()
    {
        var logger = new CapturingLogger<ObservationCycleRunner>();
        var ex = new JsonException("invalid JSON");
        ObserverLogEvents.LogJsonParseFailed(logger, "default", ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("default", logger.Entries[0].Properties["Namespace"]);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogStartupConnected_LogsAtInformationLevel()
    {
        var logger = new CapturingLogger<ObserverLogEventsTests>();
        ObserverLogEvents.LogStartupConnected(logger, "http://localhost:3001/mcp", "[\"default\"]");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("http://localhost:3001/mcp", logger.Entries[0].Properties["Gateway"]);
        Assert.Equal("[\"default\"]", logger.Entries[0].Properties["AllowedNamespaces"]);
    }

    [Fact]
    public void LogStartupConnectionFailed_LogsAllProperties_WithException()
    {
        var logger = new CapturingLogger<ObserverLogEventsTests>();
        var ex = new HttpRequestException("connection refused");
        ObserverLogEvents.LogStartupConnectionFailed(
            logger, "http://localhost:3001/mcp", "http://keycloak:8080", "mcp:tools.readonly", "infra-gate-observer", ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("http://localhost:3001/mcp", logger.Entries[0].Properties["Gateway"]);
        Assert.Equal("http://keycloak:8080", logger.Entries[0].Properties["Authority"]);
        Assert.Equal("mcp:tools.readonly", logger.Entries[0].Properties["Scope"]);
        Assert.Equal("infra-gate-observer", logger.Entries[0].Properties["ClientId"]);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogMcpConnected_LogsAtInformationLevel()
    {
        var logger = new CapturingLogger<ObserverMcpClient>();
        ObserverLogEvents.LogMcpConnected(logger, "http://localhost:3001/mcp");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("http://localhost:3001/mcp", logger.Entries[0].Properties["GatewayBaseUrl"]);
    }

    [Fact]
    public void LogHealthCheckStarting_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger<ObserverLogEventsTests>();
        ObserverLogEvents.LogHealthCheckStarting(logger);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
    }

    [Fact]
    public void LogHealthCheckFailed_LogsWithException()
    {
        var logger = new CapturingLogger<ObserverLogEventsTests>();
        var ex = new InvalidOperationException("token failed");
        ObserverLogEvents.LogHealthCheckFailed(logger, ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogHandoffSinkFailed_LogsAtErrorLevel_WithException()
    {
        var logger = new CapturingLogger<CompositeAnomalyHandoffSink>();
        var ex = new IOException("disk full");
        ObserverLogEvents.LogHandoffSinkFailed(logger, "JsonFileSink", "disk full", ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Equal("JsonFileSink", logger.Entries[0].Properties["SinkName"]);
        Assert.Equal("disk full", logger.Entries[0].Properties["ErrorMessage"]);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogAnomalyReport_LogsAtInformationLevel()
    {
        var logger = new CapturingLogger<LoggingAnomalyHandoffSink>();
        ObserverLogEvents.LogAnomalyReport(
            logger, "cycle-001", "anomaly-abc", "PodUnhealthy", "High", "Active", "Pod/crash-pod", "Pod is crash-looping");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("cycle-001", logger.Entries[0].Properties["CycleId"]);
        Assert.Equal("anomaly-abc", logger.Entries[0].Properties["AnomalyId"]);
        Assert.Equal("PodUnhealthy", logger.Entries[0].Properties["Kind"]);
        Assert.Equal("High", logger.Entries[0].Properties["Severity"]);
        Assert.Equal("Active", logger.Entries[0].Properties["Status"]);
        Assert.Equal("Pod/crash-pod", logger.Entries[0].Properties["Target"]);
        Assert.Equal("Pod is crash-looping", logger.Entries[0].Properties["Summary"]);
    }

    [Fact]
    public void LogObserveNowTriggered_LogsAtInformationLevel()
    {
        var logger = new CapturingLogger<ObserverLogEventsTests>();
        ObserverLogEvents.LogObserveNowTriggered(logger);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
    }

    [Fact]
    public void LogObserveNowTimeout_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger<ObserverLogEventsTests>();
        ObserverLogEvents.LogObserveNowTimeout(logger);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
    }

    [Fact]
    public void LogObserveNowError_LogsAtErrorLevel_WithException()
    {
        var logger = new CapturingLogger<ObserverLogEventsTests>();
        var ex = new InvalidOperationException("cycle failure");
        ObserverLogEvents.LogObserveNowError(logger, ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Same(ex, logger.Entries[0].Exception);
    }
}
