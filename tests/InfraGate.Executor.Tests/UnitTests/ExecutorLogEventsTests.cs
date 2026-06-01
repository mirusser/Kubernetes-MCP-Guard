using InfraGate.Executor.Diagnostics;
using Microsoft.Extensions.Logging;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ExecutorLogEventsTests
{
    [Fact]
    public void LogStartupConnected_LogsAtInformationLevel()
    {
        var logger = new CapturingLogger<ExecutorLogEventsTests>();
        ExecutorLogEvents.LogStartupConnected(logger, "http://gateway:3001");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("http://gateway:3001", logger.Entries[0].Properties["GatewayBaseUrl"]);
    }

    [Fact]
    public void LogStartupConnectionFailed_LogsAtErrorLevel_WithAllProperties()
    {
        var logger = new CapturingLogger<ExecutorLogEventsTests>();
        var ex = new InvalidOperationException("conn failed");

        ExecutorLogEvents.LogStartupConnectionFailed(
            logger, "http://gateway:3001", "http://auth", "mcp:tools", "client-1", ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Equal("http://gateway:3001", logger.Entries[0].Properties["GatewayBaseUrl"]);
        Assert.Equal("http://auth", logger.Entries[0].Properties["Authority"]);
        Assert.Equal("mcp:tools", logger.Entries[0].Properties["Scope"]);
        Assert.Equal("client-1", logger.Entries[0].Properties["ClientId"]);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogHealthCheckStarting_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger<ExecutorLogEventsTests>();
        ExecutorLogEvents.LogHealthCheckStarting(logger);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
    }

    [Fact]
    public void LogHealthCheckFailed_LogsAtErrorLevel_WithException()
    {
        var logger = new CapturingLogger<ExecutorLogEventsTests>();
        var ex = new InvalidOperationException("health failed");
        ExecutorLogEvents.LogHealthCheckFailed(logger, ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogWatchStarted_LogsAtInformationLevel_WithPlanId()
    {
        var logger = new CapturingLogger<ExecutorLogEventsTests>();
        ExecutorLogEvents.LogWatchStarted(logger, "plan-abc");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("plan-abc", logger.Entries[0].Properties["PlanId"]);
    }

    [Fact]
    public void LogWatchApproved_LogsAtInformationLevel()
    {
        var logger = new CapturingLogger<ExecutorLogEventsTests>();
        ExecutorLogEvents.LogWatchApproved(logger, "plan-abc");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("plan-abc", logger.Entries[0].Properties["PlanId"]);
    }

    [Fact]
    public void LogWatchTimeout_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger<ExecutorLogEventsTests>();
        ExecutorLogEvents.LogWatchTimeout(logger, "plan-timeout");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("plan-timeout", logger.Entries[0].Properties["PlanId"]);
    }

    [Fact]
    public void LogWatchFailed_LogsAtErrorLevel_WithException()
    {
        var logger = new CapturingLogger<ExecutorLogEventsTests>();
        var ex = new InvalidOperationException("watch failed");
        ExecutorLogEvents.LogWatchFailed(logger, "plan-fail", ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Equal("plan-fail", logger.Entries[0].Properties["PlanId"]);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogExecuteSucceeded_LogsAtInformationLevel()
    {
        var logger = new CapturingLogger<ExecutorLogEventsTests>();
        ExecutorLogEvents.LogExecuteSucceeded(logger, "plan-ok");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Equal("plan-ok", logger.Entries[0].Properties["PlanId"]);
    }

    [Fact]
    public void LogExecuteFailed_LogsAtWarningLevel_WithException()
    {
        var logger = new CapturingLogger<ExecutorLogEventsTests>();
        var ex = new InvalidOperationException("execute failed");
        ExecutorLogEvents.LogExecuteFailed(logger, "plan-fail", ex);

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("plan-fail", logger.Entries[0].Properties["PlanId"]);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void LogExecuteBlocked_LogsAtWarningLevel()
    {
        var logger = new CapturingLogger<ExecutorLogEventsTests>();
        ExecutorLogEvents.LogExecuteBlocked(logger, "plan-blocked");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal("plan-blocked", logger.Entries[0].Properties["PlanId"]);
    }
}
