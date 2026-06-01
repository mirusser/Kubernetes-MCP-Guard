using A2A;
using InfraGate.AgentMcp;
using InfraGate.Planner.Handoff;
using InfraGate.Planner.Tasks;
using InfraGate.Remediation.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using TaskStatus = A2A.TaskStatus;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerTaskReconcilerTests
{
    [Theory]
    [InlineData("ApprovalRequired")]
    [InlineData("Approved")]
    public async Task ReconcileAsync_NonTerminalPlan_DispatchesAndCompletesTask(string planStatus)
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateWaitingTask("task-1", "anomaly-1", "plan-1"));
        var dispatchClient = new StubExecutorDispatchClient(ExecutorDispatchResult.Applied("Plan applied."));
        var reconciler = CreateReconciler(store, new StubMcpToolset(planStatus), dispatchClient);

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal([("anomaly-1", "plan-1")], dispatchClient.Dispatches);
        var task = await store.GetTaskAsync("task-1");
        Assert.Equal(TaskState.Completed, task!.Status.State);
        Assert.Equal("Plan applied.", task.Status.Message!.Parts.Single().Text);
    }

    [Theory]
    [InlineData("Applied", TaskState.Completed)]
    [InlineData("Expired", TaskState.Failed)]
    [InlineData("NotFound", TaskState.Failed)]
    public async Task ReconcileAsync_TerminalPlan_PersistsTerminalStateWithoutDispatch(
        string planStatus,
        TaskState expectedTaskState)
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateWaitingTask("task-1", "anomaly-1", "plan-1"));
        var dispatchClient = new StubExecutorDispatchClient(ExecutorDispatchResult.Applied("unexpected"));
        var reconciler = CreateReconciler(store, new StubMcpToolset(planStatus), dispatchClient);

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Empty(dispatchClient.Dispatches);
        var task = await store.GetTaskAsync("task-1");
        Assert.Equal(expectedTaskState, task!.Status.State);
    }

    [Fact]
    public async Task ReconcileAsync_MissingPlanArtifact_FailsTaskWithoutCallingGateway()
    {
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync("task-1", CreateWaitingTask("task-1", "anomaly-1"));
        var mcp = new StubMcpToolset("ApprovalRequired");
        var reconciler = CreateReconciler(
            store,
            mcp,
            new StubExecutorDispatchClient(ExecutorDispatchResult.Applied("unexpected")));

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(0, mcp.CallCount);
        var task = await store.GetTaskAsync("task-1");
        Assert.Equal(TaskState.Failed, task!.Status.State);
    }

    [Fact]
    public async Task ReconcileAsync_MultiplePages_ReconcilesEveryWaitingTask()
    {
        var store = new PagedPlannerTaskStore(
            CreateWaitingTask("task-1", "anomaly-1", "plan-1"),
            CreateWaitingTask("task-2", "anomaly-2", "plan-2"));
        var reconciler = CreateReconciler(
            store,
            new StubMcpToolset("Applied"),
            new StubExecutorDispatchClient(ExecutorDispatchResult.Applied("unexpected")));

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(3, store.ListCallCount);
        Assert.Equal(TaskState.Completed, (await store.GetTaskAsync("task-1"))!.Status.State);
        Assert.Equal(TaskState.Completed, (await store.GetTaskAsync("task-2"))!.Status.State);
    }

    private static PlannerTaskReconciler CreateReconciler(
        IPlannerTaskStore store,
        IAgentMcpToolset mcp,
        IExecutorDispatchClient dispatchClient) =>
        new(
            store,
            new PlannerTaskLifecycle(store, new ChannelEventNotifier()),
            mcp,
            NullLogger<PlannerTaskReconciler>.Instance,
            dispatchClient);

    private static AgentTask CreateWaitingTask(string taskId, string contextId, string? planId = null) =>
        new()
        {
            Id = taskId,
            ContextId = contextId,
            Status = new TaskStatus
            {
                State = TaskState.AuthRequired,
                Timestamp = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            },
            Artifacts = planId is null
                ? null
                :
                [
                    new Artifact
                    {
                        ArtifactId = PlannerTaskStoreConventions.Artifacts.PlanReferenceId,
                        Name = PlannerTaskStoreConventions.Artifacts.PlanReferenceName,
                        Parts = [new Part { Text = planId }],
                    },
                ],
        };

    private sealed class StubMcpToolset(string planStatus) : IAgentMcpToolset
    {
        public string GatewayBaseUrl => "http://gateway";
        public bool IsConnected => true;
        public int CallCount { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AITool>>([]);

        public Task<CallToolResult> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Assert.Equal(PlannerConventions.ToolNames.GetPlanStatus, toolName);
            string planId = Assert.IsType<string>(arguments![PlannerConventions.ToolArguments.PlanId]);
            return Task.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = $$"""{"planId":"{{planId}}","status":"{{planStatus}}"}""" }],
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubExecutorDispatchClient(ExecutorDispatchResult result) : IExecutorDispatchClient
    {
        public List<(string ContextId, string PlanId)> Dispatches { get; } = [];

        public Task<ExecutorDispatchResult> DispatchAsync(
            string contextId,
            string planId,
            CancellationToken cancellationToken)
        {
            Dispatches.Add((contextId, planId));
            return Task.FromResult(result);
        }
    }

    private sealed class PagedPlannerTaskStore(params AgentTask[] tasks) : IPlannerTaskStore
    {
        private readonly Dictionary<string, AgentTask> tasksById =
            tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);

        public int ListCallCount { get; private set; }

        public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tasksById.GetValueOrDefault(taskId));

        public Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
        {
            tasksById[taskId] = task;
            return Task.CompletedTask;
        }

        public Task<bool> TryCreateTaskAsync(
            string taskId,
            AgentTask task,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ListTasksResponse> ListTasksAsync(
            ListTasksRequest request,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(TaskState.AuthRequired, request.Status);
            Assert.True(request.IncludeArtifacts);
            ListCallCount++;

            var waitingTasks = tasksById.Values
                .Where(task => task.Status.State == TaskState.AuthRequired)
                .OrderBy(task => task.Id, StringComparer.Ordinal)
                .ToList();
            int offset = request.PageToken is null ? 0 : 1;
            var page = waitingTasks.Skip(offset).Take(1).ToList();

            return Task.FromResult(new ListTasksResponse
            {
                Tasks = page,
                NextPageToken = waitingTasks.Count > offset + page.Count ? "page-2" : string.Empty,
            });
        }
    }
}
