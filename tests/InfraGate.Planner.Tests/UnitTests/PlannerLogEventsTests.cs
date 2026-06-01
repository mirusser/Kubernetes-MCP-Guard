using InfraGate.Planner.Diagnostics;
using Microsoft.Extensions.Logging;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerLogEventsTests
{
    [Fact]
    public void LogStartupConnected_LogsAtInformationLevel_WithProperties()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        PlannerLogEvents.LogStartupConnected(logger, "http://gateway:3001", "ns1,ns2");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("http://gateway:3001", logger.Entries[0].Properties["GatewayBaseUrl"]);
        Assert.Equal("ns1,ns2", logger.Entries[0].Properties["AllowedNamespaces"]);
    }

    [Fact]
    public void LogStartupConnectionFailed_LogsAtErrorLevel_WithException()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        var ex = new InvalidOperationException("connect fail");
        PlannerLogEvents.LogStartupConnectionFailed(
            logger, "http://gateway:3001", "http://auth", "mcp:tools", "planner-1", ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Equal("http://gateway:3001", logger.Entries[0].Properties["GatewayBaseUrl"]);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogHealthCheckStarting_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        PlannerLogEvents.LogHealthCheckStarting(logger);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
    }

    [Fact]
    public void LogHealthCheckFailed_LogsAtErrorLevel_WithException()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        var ex = new InvalidOperationException("health fail");
        PlannerLogEvents.LogHealthCheckFailed(logger, ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogHandoffBatchReceived_LogsAtInformationLevel_WithProperties()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        PlannerLogEvents.LogHandoffBatchReceived(logger, "cycle-xyz", "anomaly-1", "PodUnhealthy", "High", "Active");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("cycle-xyz", logger.Entries[0].Properties["CycleId"]);
        Assert.Equal("anomaly-1", logger.Entries[0].Properties["AnomalyId"]);
        Assert.Equal("PodUnhealthy", logger.Entries[0].Properties["Kind"]);
        Assert.Equal("High", logger.Entries[0].Properties["Severity"]);
    }

    [Fact]
    public void LogDecisionInvalidOperation_LogsAtWarningLevel_WithProperties()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        PlannerLogEvents.LogDecisionInvalidOperation(logger, "anomaly-1", "unsupported_op");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("anomaly-1", logger.Entries[0].Properties["AnomalyId"]);
        Assert.Equal("unsupported_op", logger.Entries[0].Properties["OperationType"]);
    }

    [Fact]
    public void LogDecisionInvalidArguments_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        PlannerLogEvents.LogDecisionInvalidArguments(logger, "anomaly-2", "restart_deployment");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("anomaly-2", logger.Entries[0].Properties["AnomalyId"]);
    }

    [Fact]
    public void LogDecisionTimedOut_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        PlannerLogEvents.LogDecisionTimedOut(logger, "anomaly-3");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("anomaly-3", logger.Entries[0].Properties["AnomalyId"]);
    }

    [Fact]
    public void LogProposePlanFailed_LogsAtWarningLevel_WithException()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        var ex = new InvalidOperationException("propose failed");
        PlannerLogEvents.LogProposePlanFailed(logger, "anomaly-4", ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("anomaly-4", logger.Entries[0].Properties["AnomalyId"]);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogProposePlanMissingPlanId_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        PlannerLogEvents.LogProposePlanMissingPlanId(logger, "anomaly-5");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("anomaly-5", logger.Entries[0].Properties["AnomalyId"]);
    }

    [Fact]
    public void LogBatchProcessingFailed_LogsAtErrorLevel_WithException()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        var ex = new InvalidOperationException("batch failed");
        PlannerLogEvents.LogBatchProcessingFailed(logger, "cycle-99", ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Equal("cycle-99", logger.Entries[0].Properties["CycleId"]);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogRemediationProposal_LogsAtInformationLevel_WithAllProperties()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        var now = DateTimeOffset.UtcNow;
        PlannerLogEvents.LogRemediationProposal(logger, "cycle-1", "anomaly-1", "plan-1", now);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("cycle-1", logger.Entries[0].Properties["CycleId"]);
        Assert.Equal("anomaly-1", logger.Entries[0].Properties["AnomalyId"]);
        Assert.Equal("plan-1", logger.Entries[0].Properties["PlanId"]);
        Assert.Equal(now, logger.Entries[0].Properties["ProposedAt"]);
    }

    [Fact]
    public void LogHandoffSinkFailed_LogsAtWarningLevel_WithException()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        var ex = new InvalidOperationException("sink failed");
        PlannerLogEvents.LogHandoffSinkFailed(logger, "HttpSink", "connection refused", ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("HttpSink", logger.Entries[0].Properties["SinkName"]);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogFilterDropped_LogsAtDebugLevel_WithProperties()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        PlannerLogEvents.LogFilterDropped(logger, "anomaly-drop", "DedupeOperationInBatch");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, logger.Entries[0].Level);
        Assert.Equal("anomaly-drop", logger.Entries[0].Properties["AnomalyId"]);
        Assert.Equal("DedupeOperationInBatch", logger.Entries[0].Properties["Reason"]);
    }

    [Fact]
    public void LogDecisionCompleted_LogsAtInformationLevel_WithProperties()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        PlannerLogEvents.LogDecisionCompleted(logger, "anomaly-ok", "restart_deployment");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("anomaly-ok", logger.Entries[0].Properties["AnomalyId"]);
        Assert.Equal("restart_deployment", logger.Entries[0].Properties["OperationType"]);
    }

    [Fact]
    public void LogProposePlanSucceeded_LogsAtInformationLevel()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        PlannerLogEvents.LogProposePlanSucceeded(logger, "anomaly-done", "plan-done");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("anomaly-done", logger.Entries[0].Properties["AnomalyId"]);
        Assert.Equal("plan-done", logger.Entries[0].Properties["PlanId"]);
    }

    [Fact]
    public void LogHandoffPublished_LogsAtInformationLevel_WithProperties()
    {
        var logger = new CapturingLogger<PlannerLogEventsTests>();
        PlannerLogEvents.LogHandoffPublished(logger, "cycle-pub", 7);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("cycle-pub", logger.Entries[0].Properties["CycleId"]);
        Assert.Equal(7, logger.Entries[0].Properties["ProposalCount"]);
    }
}
