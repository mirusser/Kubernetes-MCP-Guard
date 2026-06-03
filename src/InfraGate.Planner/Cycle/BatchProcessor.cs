using System.Diagnostics.Metrics;
using InfraGate.AgentLlm;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Cycle.Workflow;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using InfraGate.Planner.Handoff;
using InfraGate.Planner.Llm;
using InfraGate.Planner.Tasks;
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
    private readonly Counter<long>? timeoutCounter;
    private readonly Counter<long>? proposeFailedCounter;
    private readonly AgentGuardrailPolicy? guardrailPolicy;
    private readonly AgentGuardrailMetrics? guardrailMetrics;
    private readonly IObserverChannel? observerChannel;
    private readonly PlannerTaskLifecycle? taskLifecycle;
    private readonly IExecutorDispatchClient? executorDispatchClient;
    private readonly IModelVisibleContentGuard? contentGuard;

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
        IPlannerAuditOutbox? auditOutbox = null,
        AgentGuardrailPolicy? guardrailPolicy = null,
        AgentGuardrailMetrics? guardrailMetrics = null,
        IObserverChannel? observerChannel = null,
        PlannerTaskLifecycle? taskLifecycle = null,
        IExecutorDispatchClient? executorDispatchClient = null,
        IModelVisibleContentGuard? contentGuard = null)
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
        timeoutCounter = PlannerMetrics.CreateDecisionTimeoutCounter(meter);
        proposeFailedCounter = PlannerMetrics.CreateProposeFailedCounter(meter);
        this.guardrailPolicy = guardrailPolicy;
        this.guardrailMetrics = guardrailMetrics;
        this.observerChannel = observerChannel;
        this.taskLifecycle = taskLifecycle;
        this.executorDispatchClient = executorDispatchClient;
        this.contentGuard = contentGuard;
    }

    internal async Task<IReadOnlyList<RemediationProposal>> ProcessBatchAsync(
        AnomalyHandoffBatch batch,
        CancellationToken shutdownToken)
    {
        var opts = optionsMonitor.CurrentValue;
        using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        batchCts.CancelAfter(TimeSpan.FromSeconds(opts.BatchWallClockCapSeconds));

        // Throws OCE immediately if shutdown CT was already cancelled.
        var tools = await mcpClient.GetAgentToolsAsync(batchCts.Token).ConfigureAwait(false);

        if (batch.Reports.Count == 0)
        {
            return [];
        }

        var systemPrompt = await promptLibrary.RenderAsync(
            PlannerConventions.Prompts.SystemPromptTemplateName,
            emptyPromptArgs,
            batchCts.Token).ConfigureAwait(false);

        IReadOnlyList<AITool> agentTools = observerChannel is null
            ? tools
            : [.. tools, AskObserverTool.Create(observerChannel, batch.CycleId)];

        var (workflow, _) = BuildWorkflow(opts, batch, agentTools, systemPrompt);

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

            return proposals;
        }
    }

    internal async Task ProcessTaskAsync(PlannerTaskWorkItem workItem, CancellationToken cancellationToken)
    {
        var lifecycle = taskLifecycle
            ?? throw new InvalidOperationException("Planner task lifecycle is not configured.");

        await lifecycle.StartWorkAsync(workItem.TaskId, workItem.ContextId, cancellationToken).ConfigureAwait(false);
        var proposals = await ProcessBatchAsync(workItem.Batch, cancellationToken).ConfigureAwait(false);

        if (proposals.Count == 0)
        {
            await lifecycle.CompleteNoActionAsync(workItem.TaskId, workItem.ContextId, cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var proposal in proposals)
        {
            await lifecycle.AddPlanArtifactAsync(
                workItem.TaskId,
                workItem.ContextId,
                proposal.PlanId,
                cancellationToken).ConfigureAwait(false);
        }

        await lifecycle.RequireApprovalAsync(workItem.TaskId, workItem.ContextId, cancellationToken).ConfigureAwait(false);

        if (executorDispatchClient is null)
        {
            return;
        }

        if (proposals.Count != 1)
        {
            throw new InvalidOperationException("Planner task execution requires exactly one remediation proposal.");
        }

        var outcome = await executorDispatchClient.DispatchAsync(
            workItem.ContextId,
            proposals[0].PlanId,
            cancellationToken).ConfigureAwait(false);
        await lifecycle.ApplyExecutorOutcomeAsync(
            workItem.TaskId,
            workItem.ContextId,
            outcome,
            cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
        {
            while (queue.Reader.TryRead(out var workItem))
            {
                try
                {
                    await ProcessTaskAsync(workItem, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    PlannerLogEvents.LogBatchProcessingFailed(logger, workItem.Batch.CycleId, ex);
                    if (taskLifecycle is not null)
                    {
                        await taskLifecycle.FailAsync(
                            workItem.TaskId,
                            workItem.ContextId,
                            ex.Message,
                            stoppingToken).ConfigureAwait(false);
                    }
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
                opts.MaxToolIterations, opts.AnomalyWallClockCapSeconds, timeoutCounter, logger, guardrailPolicy, dedupeStore, auditOutbox, contentGuard));
            validateExecs.Add(new ValidateExecutor($"validate-{i}", batchOperationKeys, dedupeStore,
                guardrailMetrics, logger, auditOutbox));
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
            .WithOpenTelemetry()
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
