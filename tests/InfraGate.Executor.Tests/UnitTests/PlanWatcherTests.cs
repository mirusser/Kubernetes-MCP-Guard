using System.Diagnostics.Metrics;
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
