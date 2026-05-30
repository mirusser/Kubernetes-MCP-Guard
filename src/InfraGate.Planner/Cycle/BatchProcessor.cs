using System.Diagnostics.Metrics;
using InfraGate.AgentLlm;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Cycle.Workflow;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using InfraGate.AgentMcp;
using InfraGate.Prompts;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace InfraGate.Planner.Cycle;

internal sealed class BatchProcessor : BackgroundService
{
    private readonly IOptionsMonitor<PlannerOptions> optionsMonitor;
    private readonly AnomalyBatchQueue queue;
    private readonly ToolCallingAgentFactory agentFactory;
    private readonly IAgentMcpToolset mcpClient;
    private readonly IRemediationProposalSink proposalSink;
    private readonly ILogger<BatchProcessor> logger;
    private readonly PlannerDedupeStore dedupeStore;
    private readonly IPlannerAuditOutbox? auditOutbox;
    private readonly IPromptLibrary promptLibrary;
    private static readonly IReadOnlyDictionary<string, object?> emptyPromptArgs =
        new Dictionary<string, object?>(0, StringComparer.Ordinal);
    private readonly Counter<long>? invalidOperationCounter;
    private readonly Counter<long>? invalidArgumentsCounter;
    private readonly Counter<long>? timeoutCounter;
    private readonly Counter<long>? proposeFailedCounter;

    public BatchProcessor( // NOSONAR:S107 — orchestrator dependencies are explicit production seams.
        IOptionsMonitor<PlannerOptions> optionsMonitor,
        AnomalyBatchQueue queue,
        ToolCallingAgentFactory agentFactory,
        IAgentMcpToolset mcpClient,
        IRemediationProposalSink proposalSink,
        ILogger<BatchProcessor> logger,
        IPromptLibrary promptLibrary,
        PlannerDedupeStore? dedupeStore = null,
        Meter? meter = null,
        IPlannerAuditOutbox? auditOutbox = null)
    {
        this.optionsMonitor = optionsMonitor;
        this.queue = queue;
        this.agentFactory = agentFactory;
        this.mcpClient = mcpClient;
        this.proposalSink = proposalSink;
        this.logger = logger;
        this.promptLibrary = promptLibrary;
        this.dedupeStore = dedupeStore ?? new PlannerDedupeStore();
        this.auditOutbox = auditOutbox;
        invalidOperationCounter = PlannerMetrics.CreateDecisionInvalidOperationCounter(meter);
        invalidArgumentsCounter = PlannerMetrics.CreateDecisionInvalidArgumentsCounter(meter);
        timeoutCounter = PlannerMetrics.CreateDecisionTimeoutCounter(meter);
        proposeFailedCounter = PlannerMetrics.CreateProposeFailedCounter(meter);
    }

    internal async Task ProcessBatchAsync(AnomalyHandoffBatch batch, CancellationToken shutdownToken)
    {
        var opts = optionsMonitor.CurrentValue;
        using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        batchCts.CancelAfter(TimeSpan.FromSeconds(opts.BatchWallClockCapSeconds));

        // Throws OCE immediately if shutdown CT was already cancelled.
        var tools = await mcpClient.GetAgentToolsAsync(batchCts.Token).ConfigureAwait(false);

        if (batch.Reports.Count == 0) return;

        var systemPrompt = await promptLibrary.RenderAsync(
            PlannerConventions.Prompts.SystemPromptTemplateName,
            emptyPromptArgs,
            batchCts.Token).ConfigureAwait(false);

        var (workflow, _) = BuildWorkflow(opts, batch, tools, systemPrompt);

        var run = await InProcessExecution
            .RunAsync<AnomalyHandoffBatch>(workflow, batch, cancellationToken: batchCts.Token)
            .ConfigureAwait(false);
        await using (run.ConfigureAwait(false))
        {
            if (batchCts.IsCancellationRequested && shutdownToken.IsCancellationRequested)
                throw new OperationCanceledException(shutdownToken);

            var proposals = run.OutgoingEvents
                .OfType<WorkflowOutputEvent>()
                .Where(e => e.Is<RemediationProposal>())
                .Select(e => e.As<RemediationProposal>()!)
                .ToList();

            if (proposals.Count > 0)
            {
                await proposalSink.PublishAsync(
                    new RemediationProposalBatch
                    {
                        CycleId = batch.CycleId,
                        EmittedAt = DateTimeOffset.UtcNow,
                        Proposals = proposals,
                    },
                    shutdownToken).ConfigureAwait(false);

                PlannerLogEvents.LogHandoffPublished(logger, batch.CycleId, proposals.Count);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
        {
            while (queue.Reader.TryRead(out var batch))
            {
                try
                {
                    await ProcessBatchAsync(batch, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    PlannerLogEvents.LogBatchProcessingFailed(logger, batch.CycleId, ex);
                }
            }
        }
    }

    private (Microsoft.Agents.AI.Workflows.Workflow Workflow, IReadOnlyList<ExecutorBinding> ProposeExecutors) BuildWorkflow(
        PlannerOptions opts,
        AnomalyHandoffBatch batch,
        IReadOnlyList<AITool> tools,
        string systemPrompt)
    {
        var batchOperationKeys = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var filterIds = batch.Reports.Select((_, i) => $"filter-{i}").ToArray();
        var batchIntake = new BatchIntakePassthroughExecutor(filterIds);

        var filterExecs = new List<ExecutorBinding>(batch.Reports.Count);
        var dedupeExecs = new List<ExecutorBinding>(batch.Reports.Count);
        var decideExecs = new List<ExecutorBinding>(batch.Reports.Count);
        var validateExecs = new List<ExecutorBinding>(batch.Reports.Count);
        var proposeExecs = new List<ExecutorBinding>(batch.Reports.Count);

        for (int i = 0; i < batch.Reports.Count; i++)
        {
            filterExecs.Add(new FilterExecutor(filterIds[i], dedupeStore, auditOutbox, logger));
            dedupeExecs.Add(new DedupeGateExecutor($"dedupe-{i}", dedupeStore, auditOutbox, logger));
            decideExecs.Add(new DecideExecutor($"decide-{i}", agentFactory, systemPrompt, tools,
                opts.MaxToolIterations, opts.AnomalyWallClockCapSeconds, timeoutCounter, logger));
            validateExecs.Add(new ValidateExecutor($"validate-{i}", batchOperationKeys, dedupeStore,
                invalidOperationCounter, invalidArgumentsCounter, logger));
            proposeExecs.Add(new ProposeExecutor($"propose-{i}", mcpClient, dedupeStore,
                auditOutbox, proposeFailedCounter, logger));
        }

        var builder = new WorkflowBuilder(batchIntake)
            .AddFanOutEdge(batchIntake, filterExecs);

        for (var i = 0; i < batch.Reports.Count; i++)
        {
            builder = builder
                .AddEdge(filterExecs[i], dedupeExecs[i])
                .AddEdge(dedupeExecs[i], decideExecs[i])
                .AddEdge(decideExecs[i], validateExecs[i])
                .AddEdge(validateExecs[i], proposeExecs[i]);
        }

        var workflow = builder
            .WithOutputFrom([.. proposeExecs])
            .Build();

        return (workflow, proposeExecs);
    }

    [SendsMessage(typeof(AnomalyReport))]
    private sealed class BatchIntakePassthroughExecutor(string[] targetIds)
        : Executor<AnomalyHandoffBatch>("batch-intake")
    {
        public override async ValueTask HandleAsync(
            AnomalyHandoffBatch message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < message.Reports.Count; i++)
            {
                await context.SendMessageAsync(message.Reports[i], targetId: targetIds[i], cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
