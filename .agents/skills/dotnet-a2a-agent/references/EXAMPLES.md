# A2A Agent Development — Examples

## Task-driven listener with `TaskUpdater`

> Mirrors the repo's `InfraGate.Planner/Handoff/PlannerHandoffAgentHandler.cs` (handler) and
> `InfraGate.Planner/Tasks/PlannerTaskLifecycle.cs` (durable state transitions).

A task-based handler has **two halves**:

1. `ExecuteAsync` runs on the server's event queue and must return quickly — it creates the
   task idempotently, surfaces it to the caller, and hands the payload to a background worker.
2. Every state transition *after* `ExecuteAsync` returns goes through a lifecycle helper that
   owns its **own** queue and persists each event itself.

This split is mandatory. Once `ExecuteAsync` returns, the server calls `Complete()` on the
queue it gave you (so that queue can no longer deliver events), and every terminal/interrupt
`TaskUpdater` method (`CompleteAsync`, `RequireAuthAsync`, …) calls `eventQueue.Complete()`
internally — so a single long-lived updater cannot drive more than one transition.

```csharp
#pragma warning disable MEAI001
public sealed class RemediationAgentHandler(
    IRemediationTaskStore taskStore,        // ITaskStore + idempotent TryCreateTaskAsync
    RemediationTaskLifecycle lifecycle,
    WorkItemQueue workQueue) : IAgentHandler
{
    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken ct)
    {
        string? json = context.Message?.Parts.FirstOrDefault(p => p.Text is not null)?.Text;
        if (json is null)
            throw new A2AException("No payload.", A2AErrorCode.InvalidParams);

        var task = new AgentTask
        {
            Id = context.TaskId,
            ContextId = context.ContextId,
            // Qualify: A2A.TaskStatus collides with System.Threading.Tasks.TaskStatus (CS0104).
            Status = new A2A.TaskStatus
            {
                State = TaskState.Submitted,
                Timestamp = DateTimeOffset.UtcNow,
            },
        };

        // Idempotent create (repo-specific; NOT part of ITaskStore). Returns false when a task
        // already exists for this contextId — ack and stop.
        if (!await taskStore.TryCreateTaskAsync(context.TaskId, task, ct))
        {
            await eventQueue.EnqueueMessageAsync(AckMessage(context), ct);
            return;
        }

        // Surface the new task to the caller, then hand off to a durable background worker.
        await eventQueue.EnqueueTaskAsync(task, ct);
        workQueue.Enqueue(new WorkItem(context.TaskId, context.ContextId, json));
    }

    public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken ct)
        => Task.CompletedTask;

    private static Message AckMessage(RequestContext ctx) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = Role.Agent,
        ContextId = ctx.ContextId,
        Parts = [new Part { Text = "accepted" }],
    };
}
#pragma warning restore MEAI001
```

The background worker drains `workQueue` and drives the task through its lifecycle. Each call
is an independent, fully-persisted transition:

```csharp
async Task ProcessAsync(WorkItem item, CancellationToken ct)
{
    await lifecycle.StartWorkAsync(item.TaskId, item.ContextId, ct);                // -> Working
    string planId = await AnalyzeAsync(item.Payload, ct);
    await lifecycle.AddPlanArtifactAsync(item.TaskId, item.ContextId, planId, ct);  // artifact
    await lifecycle.RequireApprovalAsync(item.TaskId, item.ContextId, ct);          // -> AuthRequired
    // ... dispatch to executor, await result ...
    await lifecycle.CompleteAsync(item.TaskId, item.ContextId, "applied", ct);      // -> Completed
    // Or: lifecycle.FailAsync / lifecycle.RejectAsync
}
```

The lifecycle helper is the crux: a **fresh** queue + updater per transition, then it drains
that queue and applies every event to the store under the per-task lock. Without this loop the
`TaskUpdater` calls persist **nothing** — `TaskUpdater` only *enqueues* events; the server (or,
here, this helper) is what projects and saves them.

```csharp
public sealed class RemediationTaskLifecycle(IRemediationTaskStore taskStore, ChannelEventNotifier notifier)
{
    public Task StartWorkAsync(string taskId, string contextId, CancellationToken ct) =>
        ApplyAsync(taskId, contextId, (u, c) => u.StartWorkAsync(Status("planning"), cancellationToken: c), ct);

    public Task AddPlanArtifactAsync(string taskId, string contextId, string planId, CancellationToken ct) =>
        ApplyAsync(taskId, contextId, (u, c) => u.AddArtifactAsync(
            [new Part { Text = planId }], artifactId: "plan_reference", name: "Approval Plan", cancellationToken: c), ct);

    public Task RequireApprovalAsync(string taskId, string contextId, CancellationToken ct) =>
        ApplyAsync(taskId, contextId, (u, c) => u.RequireAuthAsync(Status("waiting for approval"), cancellationToken: c), ct);

    public Task CompleteAsync(string taskId, string contextId, string outcome, CancellationToken ct) =>
        ApplyAsync(taskId, contextId, (u, c) => u.CompleteAsync(Status(outcome), cancellationToken: c), ct);
    // FailAsync / RejectAsync follow the same shape.

    private async Task ApplyAsync(
        string taskId, string contextId,
        Func<TaskUpdater, CancellationToken, ValueTask> emit, CancellationToken ct)
    {
        var eventQueue = new AgentEventQueue();
        var updater = new TaskUpdater(eventQueue, taskId, contextId);

        await emit(updater, ct);   // enqueues one lifecycle event
        eventQueue.Complete();

        await foreach (var evt in eventQueue.WithCancellation(ct))
        {
            using (await notifier.AcquireTaskLockAsync(taskId, ct))
            {
                var current = await taskStore.GetTaskAsync(taskId, ct)
                    ?? throw new InvalidOperationException($"A2A task '{taskId}' not found.");
                var updated = TaskProjection.Apply(current, evt)
                    ?? throw new InvalidOperationException($"A2A task '{taskId}' could not apply its event.");
                await taskStore.SaveTaskAsync(taskId, updated, ct);
                notifier.Notify(taskId, evt);
            }
        }
    }

    private static Message Status(string domainState) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = Role.Agent,
        Parts = [new Part { Text = domainState }],
    };
}
```

### TaskUpdater methods (all return `ValueTask`)

| Method | A2A TaskState | Purpose |
|---|---|---|
| `SubmitAsync(metadata?, ct)` | `Submitted` | Initial handoff (**no message param**) |
| `StartWorkAsync(msg?, metadata?, ct)` | `Working` | Begin processing |
| `AddArtifactAsync(parts, artifactId?, name?, description?, lastChunk?, append?, metadata?, ct)` | — | Attach output artifact |
| `RequireAuthAsync(msg?, metadata?, ct)` | `AuthRequired` | Awaiting approval/credentials |
| `RequireInputAsync(msg, metadata?, ct)` | `InputRequired` | Awaiting client input (message **required**) |
| `CompleteAsync(msg?, metadata?, ct)` | `Completed` | Terminal: success |
| `FailAsync(msg?, metadata?, ct)` | `Failed` | Terminal: error |
| `RejectAsync(msg?, metadata?, ct)` | `Rejected` | Terminal: rejected |
| `CancelAsync(metadata?, ct)` | `Canceled` | Terminal: canceled (**no message param**) |

> `CompleteAsync`, `FailAsync`, `RejectAsync`, `CancelAsync`, `RequireInputAsync`, and
> `RequireAuthAsync` call `eventQueue.Complete()` internally — use one queue + updater per
> transition (see `ApplyAsync` above).

## Fire-and-forget caller (agent-framework)

Observer handoff pattern — send payload and don't block:

```csharp
public async Task HandoffAsync(A2AAgent agent, string contextId, string payload)
{
    var session = await agent.CreateSessionAsync(contextId);
    await agent.RunAsync(payload, session,
        options: new AgentRunOptions { AllowBackgroundResponses = true });
}
```

The remote listener must return an immediate ack message. Its heavy work happens on a durable task.

## Synchronous caller with long timeout

Planner-to-Executor dispatch — send planId, block until executor returns result:

```csharp
public async Task<ExecutorDispatchResult> DispatchAsync(
    A2AAgent agent, string contextId, string planId, CancellationToken ct)
{
    var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(61) // must exceed executor's watch timeout
    };
    var executorAgent = new A2AAgent(
        new A2AClient(new Uri("http://executor:8082/a2a/executor"), httpClient));

    var session = await executorAgent.CreateSessionAsync(contextId);
    var response = await executorAgent.RunAsync(planId, session,
        cancellationToken: ct);
    return JsonSerializer.Deserialize<ExecutorDispatchResult>(response.Text!)!;
}
```

## Registering a Postgres-backed task store

For durability across restarts:

```csharp
// Override the default InMemoryTaskStore. Register the extended interface so handlers
// can resolve TryCreateTaskAsync, plus ITaskStore for the SDK:
var dataSource = NpgsqlDataSource.Create(connectionString);
var store = new PostgresTaskStore(dataSource);
builder.Services.AddSingleton<IRemediationTaskStore>(store);  // ITaskStore + TryCreateTaskAsync
builder.Services.AddSingleton<ITaskStore>(store);
```

`PostgresTaskStore` must implement `ITaskStore`:
- `GetTaskAsync(string taskId, CancellationToken)`
- `SaveTaskAsync(string taskId, AgentTask task, CancellationToken)`
- `DeleteTaskAsync(string taskId, CancellationToken)` (optional — SDK never calls this)
- `ListTasksAsync(ListTasksRequest, CancellationToken)` (supports filtering by `ContextId`, `Status`)

For idempotent "one per contextId", add a UNIQUE constraint on `context_id` and expose an
`INSERT ... ON CONFLICT DO NOTHING` helper as `TryCreateTaskAsync` on an interface that extends
`ITaskStore` (the repo's `IPlannerTaskStore` does exactly this). It is **not** part of the
standard `ITaskStore`.
