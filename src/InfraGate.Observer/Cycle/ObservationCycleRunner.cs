using System.Diagnostics;
using System.Diagnostics.Metrics;
using InfraGate.AgentLlm;
using InfraGate.Observer.Audit;
using InfraGate.Observer.Classification;
using InfraGate.Observer.Cycle.Workflow;
using InfraGate.Observer.Diagnostics;
using InfraGate.Observer.Mcp;
using InfraGate.Observer.Prompts;
using InfraGate.Observer.Snapshot;
using InfraGate.Observer.State;
using Microsoft.Agents.AI.Workflows;
using Serilog.Context;

namespace InfraGate.Observer.Cycle;

internal sealed class ObservationCycleRunner : IObservationCycleRunner
{
    private readonly IOptionsMonitor<ObserverOptions> optionsMonitor;
    private readonly ISnapshotFetcher snapshotFetcher;
    private readonly ISystemPromptProvider systemPromptProvider;
    private readonly ToolCallingAgentFactory agentFactory;
    private readonly ISeverityClassifier severityClassifier;
    private readonly IObserverMcpClient mcpClient;
    private readonly IAnomalyDedupeStore dedupeStore;
    private readonly IAnomalyHandoffSink handoffSink;
    private readonly IObserverAuditOutbox? auditOutbox;
    private readonly ILogger<ObservationCycleRunner> logger;
    private readonly Counter<long>? cycleCountCounter;
    private readonly Counter<long>? toolCallsCounter;
    private readonly Counter<long>? severityDisagreementCounter;
    private readonly Counter<long>? reportsEmittedCounter;
    private readonly Histogram<double>? cycleDurationHistogram;

    public ObservationCycleRunner( // NOSONAR:S107 — DI constructor; all params are required services.
        IOptionsMonitor<ObserverOptions> optionsMonitor,
        ISnapshotFetcher snapshotFetcher,
        ISystemPromptProvider systemPromptProvider,
        ToolCallingAgentFactory agentFactory,
        ISeverityClassifier severityClassifier,
        IObserverMcpClient mcpClient,
        IAnomalyDedupeStore dedupeStore,
        IAnomalyHandoffSink handoffSink,
        ILogger<ObservationCycleRunner> logger,
        Meter? meter = null,
        IObserverAuditOutbox? auditOutbox = null)
    {
        this.optionsMonitor = optionsMonitor;
        this.snapshotFetcher = snapshotFetcher;
        this.systemPromptProvider = systemPromptProvider;
        this.agentFactory = agentFactory;
        this.severityClassifier = severityClassifier;
        this.mcpClient = mcpClient;
        this.dedupeStore = dedupeStore;
        this.handoffSink = handoffSink;
        this.auditOutbox = auditOutbox;
        this.logger = logger;

        cycleCountCounter = ObserverMetrics.CreateCycleCountCounter(meter);
        toolCallsCounter = ObserverMetrics.CreateToolCallsCounter(meter);
        severityDisagreementCounter = ObserverMetrics.CreateSeverityDisagreementCounter(meter);
        reportsEmittedCounter = ObserverMetrics.CreateReportsEmittedCounter(meter);
        cycleDurationHistogram = ObserverMetrics.CreateCycleDurationHistogram(meter);
    }

    public async Task<CycleResult> RunAsync(CancellationToken shutdownToken)
    {
        var cycleId = Guid.NewGuid().ToString("D");
        using var _ = LogContext.PushProperty("CycleId", cycleId);

        var opts = optionsMonitor.CurrentValue;

        if (opts.AllowedNamespaces.Count == 0)
        {
            return EmptyResult(cycleId);
        }

        var stopwatch = Stopwatch.StartNew();

        using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        cycleCts.CancelAfter(TimeSpan.FromSeconds(opts.WallClockCapSeconds));

        var tools = await mcpClient.GetReadOnlyToolsAsync(cycleCts.Token).ConfigureAwait(false);
        var input = new CycleWorkflowInput(cycleId, opts.MaxToolIterations);

        var (workflow, getToolCallCounts) = BuildWorkflow(opts, cycleId, tools, stopwatch);

        try
        {
            var run = await InProcessExecution
                .RunAsync<CycleWorkflowInput>(workflow, input, cancellationToken: cycleCts.Token)
                .ConfigureAwait(false);
            await using (run.ConfigureAwait(false))
            {
                // Check cancellation first — the workflow may have captured the OCE from executors
                // as ExecutorFailedEvents without re-throwing it.
                if (cycleCts.IsCancellationRequested)
                    throw new OperationCanceledException(cycleCts.Token);

                var outputEvent = run.OutgoingEvents
                    .OfType<WorkflowOutputEvent>()
                    .FirstOrDefault(e => e.Is<CycleResult>());

                if (outputEvent?.As<CycleResult>() is { } cycleResult)
                {
                    return cycleResult;
                }
            }

            // Workflow completed but yielded no output — treat as empty cycle.
            return EmptyResult(cycleId);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            var isTruncated = true;
            var totalToolCalls = getToolCallCounts();

            if (shutdownToken.IsCancellationRequested)
                ObserverLogEvents.LogCycleCancelled(logger);
            else
                ObserverLogEvents.LogCycleTruncated(logger);

            ObserverLogEvents.LogTruncatedNoReports(logger, cycleId, totalToolCalls);

            cycleCountCounter?.Add(1,
                new KeyValuePair<string, object?>(ObserverMetrics.ResultTag, ObserverMetrics.ResultTruncated));
            toolCallsCounter?.Add(totalToolCalls);

            return new CycleResult
            {
                CycleId = cycleId,
                Reports = Array.Empty<AnomalyReport>(),
                IsTruncated = isTruncated,
                ToolCallsUsed = totalToolCalls,
                SeverityDisagreements = 0,
                Duration = stopwatch.Elapsed,
            };
        }
    }

    private (Microsoft.Agents.AI.Workflows.Workflow Workflow, Func<int> GetToolCallCounts) BuildWorkflow(
        ObserverOptions opts,
        string cycleId,
        IReadOnlyList<AITool> tools,
        Stopwatch stopwatch)
    {
        var namespaces = opts.AllowedNamespaces;
        var cycleInput = new CycleInputPassthroughExecutor();

        // Per-namespace agents with tool-call counting.
        var agentGetCounts = new List<Func<int>>(namespaces.Count);
        var snapExecutors = new List<ExecutorBinding>(namespaces.Count);
        var agentExecutors = new List<ExecutorBinding>(namespaces.Count);
        var parseExecutors = new List<ExecutorBinding>(namespaces.Count);

        for (var i = 0; i < namespaces.Count; i++)
        {
            var ns = namespaces[i];
            var systemPrompt = systemPromptProvider.Get(ns, opts.MaxToolIterations);

            var (agent, getCount) = agentFactory.Create($"observer-{ns}", systemPrompt, tools, opts.MaxToolIterations);
            var agentBinding = agent.BindAsExecutor(new AIAgentHostOptions { ForwardIncomingMessages = false });

            ExecutorBinding snap = new SnapshotExecutor($"snapshot-{i}", ns, snapshotFetcher, logger);
            ExecutorBinding parse = new AnomalyParseExecutor(
                $"parse-{i}", ns, cycleId,
                getCount,
                severityClassifier, logger);

            snapExecutors.Add(snap);
            agentExecutors.Add(agentBinding);
            parseExecutors.Add(parse);
            agentGetCounts.Add(getCount);
        }

        var aggregate = new CycleAggregateExecutor(
            "aggregate",
            suppressionWindow: opts.DedupeSuppressionWindow,
            resolutionThreshold: opts.DedupeResolutionThreshold,
            wallClockElapsed: stopwatch.Elapsed,
            dedupeStore: dedupeStore,
            handoffSink: handoffSink,
            auditOutbox: auditOutbox,
            logger: logger,
            cycleCountCounter: cycleCountCounter,
            toolCallsCounter: toolCallsCounter,
            severityDisagreementCounter: severityDisagreementCounter,
            reportsEmittedCounter: reportsEmittedCounter,
            cycleDurationHistogram: cycleDurationHistogram);

        var builder = new WorkflowBuilder(cycleInput)
            .AddFanOutEdge(cycleInput, snapExecutors);

        for (var i = 0; i < namespaces.Count; i++)
        {
            builder = builder
                .AddEdge(snapExecutors[i], agentExecutors[i])
                .AddEdge(agentExecutors[i], parseExecutors[i]);
        }

        var workflow = builder
            .AddFanInBarrierEdge(parseExecutors, aggregate)
            .WithOutputFrom(aggregate)
            .Build();

        return (workflow, () => agentGetCounts.Sum(f => f()));
    }

    private static CycleResult EmptyResult(string cycleId) => new()
    {
        CycleId = cycleId,
        Reports = Array.Empty<AnomalyReport>(),
        IsTruncated = false,
        ToolCallsUsed = 0,
        SeverityDisagreements = 0,
        Duration = TimeSpan.Zero,
    };

    // Trivial pass-through start node; converts the typed workflow input into the fan-out.
    private sealed class CycleInputPassthroughExecutor()
        : Executor<CycleWorkflowInput, CycleWorkflowInput>("cycle-input")
    {
        public override ValueTask<CycleWorkflowInput> HandleAsync(
            CycleWorkflowInput message, IWorkflowContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(message);
    }


}
