using A2A;
using InfraGate.AgentLlm;
using InfraGate.AgentMcp;
using InfraGate.Observer.Contracts;
using InfraGate.Planner.Cycle;
using InfraGate.Planner.Tasks;
using InfraGate.Prompts;
using InfraGate.Remediation.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using TaskStatus = A2A.TaskStatus;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class BatchProcessorTaskLifecycleTests
{
    [Fact]
    public async Task ProcessTaskAsync_EmptyBatch_PersistsCompletedNoActionStatus()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateSubmittedTask(), CancellationToken.None);
        var lifecycle = new PlannerTaskLifecycle(store, new ChannelEventNotifier());
        var processor = CreateProcessor(lifecycle);

        await processor.ProcessTaskAsync(CreateWorkItem(), CancellationToken.None);

        var task = await store.GetTaskAsync("task-1", CancellationToken.None);
        Assert.Equal(TaskState.Completed, task!.Status.State);
        Assert.Equal(
            PlannerTaskStoreConventions.DomainStates.Unremediable,
            task.Status.Message!.Parts.Single().Text);
    }

    [Fact]
    public async Task ExecuteAsync_ToolsFetchFails_PersistsFailedTaskStatus()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateSubmittedTask(), CancellationToken.None);
        var lifecycle = new PlannerTaskLifecycle(store, new ChannelEventNotifier());
        var queue = new AnomalyBatchQueue();
        queue.TryEnqueue(CreateWorkItem());
        var processor = CreateProcessor(lifecycle, queue, new FailingMcpToolset());
        await processor.StartAsync(CancellationToken.None);

        AgentTask task;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            task = await WaitForStateAsync(store, TaskState.Failed, cts.Token);
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }

        Assert.Equal("MCP unavailable", task.Status.Message!.Parts.Single().Text);
    }

    private static BatchProcessor CreateProcessor(
        PlannerTaskLifecycle lifecycle,
        AnomalyBatchQueue? queue = null,
        IAgentMcpToolset? mcp = null) =>
        new(
            new FakeOptionsMonitor<PlannerOptions>(new PlannerOptions
            {
                GatewayBaseUrl = "http://gateway",
                LlmApiKey = "test",
            }),
            queue ?? new AnomalyBatchQueue(),
            new ToolCallingAgentFactory(new FixtureChatClient("no-action")),
            mcp ?? new StubMcpToolset(),
            new NullRemediationProposalSink(),
            NullLogger<BatchProcessor>.Instance,
            new StubPromptLibrary(),
            taskLifecycle: lifecycle);

    private static PlannerTaskWorkItem CreateWorkItem() =>
        new(
            "task-1",
            "anomaly-1",
            new AnomalyHandoffBatch
            {
                CycleId = "cycle-1",
                EmittedAt = DateTimeOffset.UtcNow,
                Reports = [],
            });

    private static AgentTask CreateSubmittedTask() =>
        new()
        {
            Id = "task-1",
            ContextId = "anomaly-1",
            Status = new TaskStatus
            {
                State = TaskState.Submitted,
                Timestamp = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            },
        };

    private static async Task<AgentTask> WaitForStateAsync(
        IPlannerTaskStore store,
        TaskState state,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var task = await store.GetTaskAsync("task-1", cancellationToken);
            if (task?.Status.State == state)
                return task;

            await Task.Delay(TimeSpan.FromMilliseconds(10), TimeProvider.System, cancellationToken);
        }
    }

    private sealed class StubMcpToolset : IAgentMcpToolset
    {
        public string GatewayBaseUrl => "http://stub";
        public bool IsConnected => true;
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AITool>>([]);
        public Task<CallToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
            => Task.FromResult(new CallToolResult { IsError = true });
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingMcpToolset : IAgentMcpToolset
    {
        public string GatewayBaseUrl => "http://stub";
        public bool IsConnected => false;
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("MCP unavailable");
        public Task<CallToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken)
            => Task.FromResult(new CallToolResult { IsError = true });
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullRemediationProposalSink : IRemediationProposalSink
    {
        public Task PublishAsync(RemediationProposalBatch batch, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubPromptLibrary : IPromptLibrary
    {
        public Task<string> RenderAsync(string templateName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }

    private sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
