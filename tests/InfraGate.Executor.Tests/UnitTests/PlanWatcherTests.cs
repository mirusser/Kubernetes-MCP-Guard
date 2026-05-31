using System.Diagnostics.Metrics;
using System.Text.Json;
using InfraGate.Executor.Diagnostics;
using InfraGate.Executor.Mcp;
using InfraGate.Executor.Queue;
using InfraGate.Executor.Watch;
using InfraGate.Remediation.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class PlanWatcherTests
{
    [Fact]
    public async Task WatchPlanAsync_ExecutionBlocked_IncrementsBlockedCounterAndLogs()
    {
        using var meter = new Meter("executor-test-blocked");
        var logger = new CapturingLogger<PlanWatcher>();
        var mcpClient = Substitute.For<IExecutorMcpClient>();
        mcpClient.CallToolAsync(
                ExecutorConventions.ToolNames.WaitForPlanApproval,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"status":"Approved","timedOut":false}""");
        mcpClient.CallToolAsync(
                ExecutorConventions.ToolNames.ExecuteApprovedPlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"isError":true,"content":[{"text":"Pre-execution gate rejected the plan."}]}""");
        var proposal = CreateProposal("plan-blocked");
        var (watcher, queue) = CreateWatcher(mcpClient, logger, meter);
        using var probe = ListenForCounter(meter, ExecutorMetrics.ExecuteBlockedCounterName);
        queue.TryEnqueueAll([proposal]);

        await watcher.WatchPlanAsync(proposal, CancellationToken.None);

        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("executor.execute.blocked", StringComparison.Ordinal) &&
            e.Properties.TryGetValue("PlanId", out var p) && "plan-blocked".Equals(p));
    }

    [Fact]
    public async Task WatchPlanAsync_ExecutionBlocked_DoesNotIncrementFailedCounter()
    {
        using var meter = new Meter("executor-test-nofail");
        using var probe = ListenForCounter(meter, ExecutorMetrics.ExecuteFailedCounterName);
        var mcpClient = Substitute.For<IExecutorMcpClient>();
        mcpClient.CallToolAsync(
                ExecutorConventions.ToolNames.WaitForPlanApproval,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"status":"Approved","timedOut":false}""");
        mcpClient.CallToolAsync(
                ExecutorConventions.ToolNames.ExecuteApprovedPlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"isError":true}""");
        var proposal = CreateProposal("plan-gated");
        var (watcher, queue) = CreateWatcher(mcpClient, meter: meter);
        queue.TryEnqueueAll([proposal]);

        await watcher.WatchPlanAsync(proposal, CancellationToken.None);

        Assert.Empty(probe.Measurements);
    }

    [Fact]
    public async Task WatchPlanAsync_ExecutionSucceeds_IncrementsSucceededCounter()
    {
        using var meter = new Meter("executor-test-ok");
        var logger = new CapturingLogger<PlanWatcher>();
        var mcpClient = Substitute.For<IExecutorMcpClient>();
        mcpClient.CallToolAsync(
                ExecutorConventions.ToolNames.WaitForPlanApproval,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"status":"Approved","timedOut":false}""");
        mcpClient.CallToolAsync(
                ExecutorConventions.ToolNames.ExecuteApprovedPlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"status":"Applied"}""");
        var proposal = CreateProposal("plan-ok");
        var (watcher, queue) = CreateWatcher(mcpClient, logger, meter);
        using var probe = ListenForCounter(meter, ExecutorMetrics.ExecuteSucceededCounterName);
        queue.TryEnqueueAll([proposal]);

        await watcher.WatchPlanAsync(proposal, CancellationToken.None);

        Assert.Single(probe.Measurements);
        Assert.Equal(1L, probe.Measurements[0].Value);
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("executor.execute.succeeded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WatchPlanAsync_ExecutionFails_IncrementsFailedCounterNotBlocked()
    {
        using var meter = new Meter("executor-test-fail");
        var mcpClient = Substitute.For<IExecutorMcpClient>();
        mcpClient.CallToolAsync(
                ExecutorConventions.ToolNames.WaitForPlanApproval,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns("""{"status":"Approved","timedOut":false}""");
        mcpClient.CallToolAsync(
                ExecutorConventions.ToolNames.ExecuteApprovedPlan,
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new HttpRequestException("gateway down")));
        var proposal = CreateProposal("plan-fail");
        var (watcher, queue) = CreateWatcher(mcpClient, meter: meter);
        using var failedProbe = ListenForCounter(meter, ExecutorMetrics.ExecuteFailedCounterName);
        using var blockedProbe = ListenForCounter(meter, ExecutorMetrics.ExecuteBlockedCounterName);
        queue.TryEnqueueAll([proposal]);

        await watcher.WatchPlanAsync(proposal, CancellationToken.None);

        Assert.Single(failedProbe.Measurements);
        Assert.Empty(blockedProbe.Measurements);
    }

    [Fact]
    public async Task WatchPlanAsync_AlreadyTracked_SkipsExecution()
    {
        var mcpClient = Substitute.For<IExecutorMcpClient>();
        var dedupeStore = Substitute.For<IExecutorDedupeStore>();
        dedupeStore.TryTrack(Arg.Any<string>()).Returns(false);
        var proposal = CreateProposal("plan-tracked");
        var (watcher, queue) = CreateWatcher(mcpClient, dedupeStore: dedupeStore);
        queue.TryEnqueueAll([proposal]);

        await watcher.WatchPlanAsync(proposal, CancellationToken.None);

        await mcpClient.DidNotReceive().CallToolAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
    }

    private static (PlanWatcher Watcher, ProposalQueue Queue) CreateWatcher(
        IExecutorMcpClient mcpClient,
        ILogger<PlanWatcher>? logger = null,
        Meter? meter = null,
        IExecutorDedupeStore? dedupeStore = null)
    {
        var options = Options.Create(new ExecutorOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            WatchTimeoutSeconds = 900,
            ConcurrencyCap = 64,
        });
        var optionsMonitor = Substitute.For<IOptionsMonitor<ExecutorOptions>>();
        optionsMonitor.CurrentValue.Returns(options.Value);
        var queue = new ProposalQueue(options);
        return (
            new PlanWatcher(
                queue,
                dedupeStore ?? new ExecutorDedupeStore(),
                mcpClient,
                optionsMonitor,
                logger ?? new CapturingLogger<PlanWatcher>(),
                meter),
            queue);
    }

    [Theory]
    [InlineData("""{"status":"Approved","timedOut":false}""", "Approved", false)]
    [InlineData("""{"status":"NotFound","timedOut":false}""", "NotFound", false)]
    [InlineData("""{"status":"Expired","timedOut":false}""", "Expired", false)]
    [InlineData("""{"status":"Approved","timedOut":true}""", "Approved", true)]
    public void TryParseWaitResult_ValidJson_ReturnsTrue(string json, string expectedStatus, bool expectedTimedOut)
    {
        var result = PlanWatcher.TryParseWaitResult(json, out var status, out var timedOut);

        Assert.True(result);
        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedTimedOut, timedOut);
    }

    [Fact]
    public void TryParseWaitResult_InvalidJson_ReturnsFalse()
    {
        Assert.False(PlanWatcher.TryParseWaitResult("not-json", out _, out _));
    }

    [Fact]
    public void TryFindWaitResult_NestedStatus_ReturnsTrue()
    {
        using var doc = JsonDocument.Parse("""{"data":{"status":"Approved","timedOut":false}}""");
        var status = string.Empty;
        var timedOut = false;

        Assert.True(PlanWatcher.TryFindWaitResult(doc.RootElement, ref status, ref timedOut));
        Assert.Equal("Approved", status);
    }

    [Fact]
    public void TryFindWaitResult_PrimitiveValue_ReturnsFalse()
    {
        using var doc = JsonDocument.Parse("42");
        var status = string.Empty;
        var timedOut = false;

        Assert.False(PlanWatcher.TryFindWaitResult(doc.RootElement, ref status, ref timedOut));
    }

    [Fact]
    public void TryFindWaitResult_NoStatus_ReturnsFalse()
    {
        using var doc = JsonDocument.Parse("""{"data":{"value":1}}""");
        var status = string.Empty;
        var timedOut = false;

        Assert.False(PlanWatcher.TryFindWaitResult(doc.RootElement, ref status, ref timedOut));
    }

    [Fact]
    public void TryFindWaitResultInArray_Empty_ReturnsFalse()
    {
        using var doc = JsonDocument.Parse("""[]""");
        var status = string.Empty;
        var timedOut = false;

        Assert.False(PlanWatcher.TryFindWaitResultInArray(doc.RootElement, ref status, ref timedOut));
    }

    [Fact]
    public void TryFindWaitResultInJsonString_NullOrEmpty_ReturnsFalse()
    {
        var status = string.Empty;
        var timedOut = false;
        Assert.False(PlanWatcher.TryFindWaitResultInJsonString(null, ref status, ref timedOut));
        Assert.False(PlanWatcher.TryFindWaitResultInJsonString("", ref status, ref timedOut));
    }

    [Fact]
    public void TryFindWaitResultInJsonString_NoQuotes_ReturnsFalse()
    {
        var status = string.Empty;
        var timedOut = false;
        Assert.False(PlanWatcher.TryFindWaitResultInJsonString("noquotes", ref status, ref timedOut));
    }

    [Fact]
    public void TryFindWaitResultInJsonString_InvalidJson_ReturnsFalse()
    {
        var status = string.Empty;
        var timedOut = false;

        Assert.False(PlanWatcher.TryFindWaitResultInJsonString("""{"broken":""", ref status, ref timedOut));
    }

    [Fact]
    public void IsErrorResponse_True_ReturnsTrue()
    {
        Assert.True(PlanWatcher.IsErrorResponse("""{"isError":true}"""));
    }

    [Fact]
    public void IsErrorResponse_NoField_ReturnsFalse()
    {
        Assert.False(PlanWatcher.IsErrorResponse("""{"status":"ok"}"""));
    }

    [Fact]
    public void IsErrorResponse_InvalidJson_ReturnsFalse()
    {
        Assert.False(PlanWatcher.IsErrorResponse("not-json"));
    }

    private static RemediationProposal CreateProposal(string planId) =>
        new()
        {
            PlanId = planId,
            AnomalyId = "anomaly-1",
            ProposedAt = DateTimeOffset.UtcNow,
        };

    private static CounterProbe ListenForCounter(Meter meter, string counterName) =>
        new(meter, counterName);

    private sealed class CounterProbe : IDisposable
    {
        private readonly MeterListener listener;

        public CounterProbe(Meter meter, string counterName)
        {
            listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == meter && instrument.Name == counterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>(
                (instrument, measurement, tags, state) =>
                    Measurements.Add(new Measurement<long>(measurement, tags)));
            listener.Start();
        }

        public List<Measurement<long>> Measurements { get; } = [];

        public void Dispose() => listener.Dispose();
    }
}
