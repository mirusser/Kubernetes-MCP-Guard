# A2A Agent Development — Examples

## Task-driven listener with `TaskUpdater`

A handler that creates a durable task, processes in background, and drives state transitions:

```csharp
#pragma warning disable MEAI001
public sealed class RemediationAgentHandler(
    ITaskStore taskStore,
    ChannelEventNotifier notifier) : IAgentHandler
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
            Status = new TaskStatus
            {
                State = TaskState.Submitted,
                Timestamp = DateTimeOffset.UtcNow,
            },
        };

        // Fail fast: if not persisted, WasCreated is false.
        bool wasCreated = await taskStore.SaveIfNotExistsAsync(context.TaskId, task, ct);
        if (!wasCreated)
        {
            await eventQueue.EnqueueMessageAsync(AckMessage(context), ct);
            return; // idempotent — task already exists for this contextId
        }

        // Acknowledge the caller (non-blocking handoff).
        await eventQueue.EnqueueTaskAsync(task, ct);
        await eventQueue.EnqueueMessageAsync(AckMessage(context), ct);

        // Offload to background processor (not shown: queue/dequeue).
        _ = ProcessTaskAsync(context.TaskId, context.ContextId, json, ct);
    }

    private async Task ProcessTaskAsync(string taskId, string contextId, string payload, CancellationToken ct)
    {
        var updater = new TaskUpdater(new AgentEventQueue(), taskId, contextId);

        await updater.StartWorkAsync(StatusMessage("planning"), cancellationToken: ct);
        // ... LLM analysis ...
        await updater.AddArtifactAsync(
            [new Part { Text = planId }],
            artifactId: "plan_reference",
            name: "Approval Plan",
            cancellationToken: ct);
        await updater.RequireAuthAsync(StatusMessage("waiting for approval"), cancellationToken: ct);

        // ... dispatch to executor, wait for result ...

        await updater.CompleteAsync(StatusMessage("applied"), cancellationToken: ct);
        // Or: updater.FailAsync / updater.RejectAsync
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

    private static Message StatusMessage(string domainState) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = Role.Agent,
        Parts = [new Part { Text = domainState }],
    };
}
#pragma warning restore MEAI001
```

### TaskUpdater methods (all return `ValueTask`)

| Method | A2A TaskState | Purpose |
|---|---|---|
| `SubmitAsync(msg?, metadata?, ct)` | `Submitted` | Initial handoff |
| `StartWorkAsync(msg?, metadata?, ct)` | `Working` | Begin processing |
| `AddArtifactAsync(parts, artifactId?, name?, ct)` | — | Attach output artifact |
| `RequireAuthAsync(msg?, metadata?, ct)` | `AuthRequired` | Awaiting approval/credentials |
| `RequireInputAsync(msg?, metadata?, ct)` | `InputRequired` | Awaiting client input |
| `CompleteAsync(msg?, metadata?, ct)` | `Completed` | Terminal: success |
| `FailAsync(msg?, metadata?, ct)` | `Failed` | Terminal: error |
| `RejectAsync(msg?, metadata?, ct)` | `Rejected` | Terminal: rejected |
| `CancelAsync(msg?, metadata?, ct)` | `Canceled` | Terminal: canceled |

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
// Override the default InMemoryTaskStore:
var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton<ITaskStore>(new PostgresTaskStore(dataSource));
```

`PostgresTaskStore` must implement `ITaskStore`:
- `GetTaskAsync(string taskId, CancellationToken)`
- `SaveTaskAsync(string taskId, AgentTask task, CancellationToken)`
- `DeleteTaskAsync(string taskId, CancellationToken)` (optional — SDK never calls this)
- `ListTasksAsync(ListTasksRequest, CancellationToken)` (supports filtering by `ContextId`, `Status`)

For idempotent "one per contextId", add a UNIQUE constraint on `context_id` and an `INSERT ... ON CONFLICT DO NOTHING` helper — this is not part of the standard `ITaskStore`, so add it to your implementation-specific interface.
