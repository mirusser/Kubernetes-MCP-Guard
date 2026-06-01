# A2A Agent Development — API Reference

## `A2AServer` constructor

**NuGet:** `A2A` (raw SDK, v1.0.0-preview2)  
**Source:** `A2A/Server/A2AServer.cs:35-37`

```csharp
public A2AServer(
    IAgentHandler handler,
    ITaskStore taskStore,
    ChannelEventNotifier notifier,
    ILogger<A2AServer> logger,
    A2AServerOptions? options = null)
```

All four required params must be registered in DI before the keyed singleton.

## `IAgentHandler` interface

**NuGet:** `A2A`  
**Source:** `A2A/Server/IAgentHandler.cs:18,28`

```csharp
public interface IAgentHandler
{
    Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken);
    Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken);
}
```

- `RequestContext` provides `TaskId`, `ContextId`, `Message`, `Metadata`.
- `AgentEventQueue` provides `EnqueueTaskAsync`, `EnqueueStatusUpdateAsync`, `EnqueueArtifactUpdateAsync`, `EnqueueMessageAsync`.
- `CancelAsync` has a default no-op implementation — only override if your agent supports cancellation.

## `AgentTask` model

**NuGet:** `A2A`  
**Source:** `A2A/Models/AgentTask.cs:7-29`

```csharp
public sealed class AgentTask
{
    public string Id { get; set; }
    public string ContextId { get; set; }
    public TaskStatus Status { get; set; }   // ← NOT a direct .State property
    public List<Message>? History { get; set; }
    public List<Artifact>? Artifacts { get; set; }
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}
```

Properties that do **NOT** exist: `Input`, `CreatedAt`, direct `.State`.

Set task state as: `Status = new TaskStatus { State = TaskState.Submitted, Timestamp = DateTimeOffset.UtcNow }`.

## `TaskState` enum

**NuGet:** `A2A`  
**Source:** `A2A/Models/TaskState.cs`

| Value | Category |
|---|---|
| `Submitted` (1) | In-progress |
| `Working` (2) | In-progress |
| `Completed` (3) | Terminal |
| `Failed` (4) | Terminal |
| `Canceled` (5) | Terminal |
| `InputRequired` (6) | Interrupted |
| `Rejected` (7) | Terminal |
| `AuthRequired` (8) | Interrupted |

Wire format: `SCREAMING_SNAKE_CASE` (e.g., `TASK_STATE_AUTH_REQUIRED`).

## `ITaskStore` interface

**NuGet:** `A2A`  
**Source:** `A2A/Server/ITaskStore.cs:15-47`

```csharp
public interface ITaskStore
{
    Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken ct = default);
    Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken ct = default);
    Task DeleteTaskAsync(string taskId, CancellationToken ct = default);
    Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken ct = default);
}
```

- `DeleteTaskAsync` is never called by the SDK — provided for pruning.
- `ListTasksAsync` supports filtering by `ContextId`, `Status`, `StatusTimestampAfter`, cursor pagination.
- `TryCreateTaskAsync` is **not** part of this interface. Add idempotent creation to your own implementation-specific interface.
- `InMemoryTaskStore` is the default implementation (ConcurrentDictionary-backed).

## `A2AAgent` class (agent-framework client)

**NuGet:** `Microsoft.Agents.AI.A2A`  
**Source:** `agent-framework/dotnet/src/Microsoft.Agents.AI.A2A/A2AAgent.cs`

```csharp
public sealed class A2AAgent : AIAgent
{
    public A2AAgent(IA2AClient a2aClient, string? id = null, string? name = null,
        string? description = null, ILoggerFactory? loggerFactory = null);
    public A2AAgent(IA2AClient a2aClient, A2AAgentOptions options,
        ILoggerFactory? loggerFactory = null);
}
```

**Limitation:** Supports only messages as responses from A2A agents. Task support is documented as "will be added later." For task-based A2A communication, use raw `A2A` SDK types (`IAgentHandler`, `TaskUpdater`, `IA2AClient`).

**Key methods (inherited from `AIAgent`):**
- `CreateSessionAsync(string contextId)` — create a session for a context
- `RunAsync(string message, AgentSession session, ...)` — returns `AgentResponse` (NOT A2A `Message`/`Task`). Access result via `.Text`.
- `RunAsync(..., AllowBackgroundResponses = true)` — fire-and-forget, remote agent returns ack immediately.

**Extension method on `IA2AClient`:**
```csharp
AIAgent agent = a2aClient.AsAIAgent(name: "my-agent");
```

## `A2AClient` (raw SDK client)

**NuGet:** `A2A`  
**Source:** `A2A/Client/A2AClient.cs:19`

```csharp
public A2AClient(Uri baseUrl, HttpClient? httpClient = null)
```

Implements `IA2AClient` with `SendMessageAsync`, `GetTaskAsync`, `ListTasksAsync`, etc. Pass a custom `HttpClient` with adjusted `Timeout` for long-running synchronous calls.
