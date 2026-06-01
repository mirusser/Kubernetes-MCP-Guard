---
name: dotnet-a2a-agent
description: Create and configure .NET Agent-to-Agent (A2A) listeners and callers. Use when the user wants to implement A2A communication, set up an A2A server or client, handle incoming A2A messages or tasks, or configure A2A agent handlers in an ASP.NET Core project.
---

# .NET A2A Agent Development

## Two packages, different roles

| Package | Provides |
|---|---|
| `A2A` (raw SDK) | `IAgentHandler`, `TaskUpdater`, `ITaskStore`, `A2AServer`, `AgentTask`, `IA2AClient` |
| `Microsoft.Agents.AI.A2A` (wrapper) | `A2AAgent` (client-side, **message-only**), `AsAIAgent()` |

**Key limitation:** `A2AAgent` only supports messages, not A2A tasks. For task lifecycle, use raw SDK `IAgentHandler` + `TaskUpdater`.

## Quick start — listener (raw SDK)

```csharp
// Program.cs
builder.Services.AddSingleton<MyAgentHandler>();
builder.Services.AddSingleton<ITaskStore, InMemoryTaskStore>();
builder.Services.AddSingleton<ChannelEventNotifier>();
builder.Services.AddKeyedSingleton<A2AServer>("my-agent", (sp, _) =>
    new A2AServer(
        sp.GetRequiredService<MyAgentHandler>(),
        sp.GetRequiredService<ITaskStore>(),
        sp.GetRequiredService<ChannelEventNotifier>(),
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<A2AServer>()));

// ... after app.Build():
#pragma warning disable MEAI001 // MapA2AHttpJson is an experimental hosting helper
app.MapA2AHttpJson("my-agent", "/a2a/my-agent")
   .RequireAuthorization();
#pragma warning restore MEAI001
```

> `MapA2AHttpJson`/`MapA2AJsonRpc` come from `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` (an
> `[Experimental("MEAI001")]` helper) — *not* the raw `A2A` SDK, whose AspNetCore package exposes
> `MapA2A`. They resolve the `A2AServer` registered under the matching keyed name, which is why
> registering the keyed singleton directly (without `AddA2AServer`) is enough.

The handler implements `IAgentHandler`:

```csharp
#pragma warning disable MEAI001
public sealed class MyAgentHandler(ITaskStore taskStore) : IAgentHandler
{
    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken ct)
    {
        string? text = context.Message?.Parts.FirstOrDefault(p => p.Text is not null)?.Text;

        // Message-only reply:
        await eventQueue.EnqueueMessageAsync(new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = Role.Agent,
            ContextId = context.ContextId,
            Parts = [new Part { Text = $"Processed: {text}" }]
        }, ct);
    }

    public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken ct)
        => Task.CompletedTask;
}
#pragma warning restore MEAI001
```

## Quick start — caller (agent-framework, message-only)

```csharp
var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(61) };
var session = await agent.CreateSessionAsync(contextId);
var response = await agent.RunAsync(payload, session);
string result = response.Text ?? string.Empty;
```

For fire-and-forget:
```csharp
await agent.RunAsync(payload, session,
    options: new AgentRunOptions { AllowBackgroundResponses = true });
```

## Task lifecycle (raw SDK only)

Long-running agents should use A2A tasks via `TaskUpdater`, not bare message replies. See [`references/EXAMPLES.md`](references/EXAMPLES.md) for a complete task-driven handler.

## Framework server hosting (agent-framework, `[Experimental]`)

The agent-framework can wrap an `AIAgent` into an A2A server declaratively, avoiding manual `IAgentHandler`/`A2AServer` wiring:

```csharp
#pragma warning disable MEAI001 // AddA2AServer + AgentRunMode + MapA2AHttpJson are [Experimental]
builder.AddA2AServer("my-agent", options =>   // NuGet: Microsoft.Agents.AI.Hosting.A2A
{
    options.AgentRunMode = AgentRunMode.DisallowBackground;
});
// Map endpoint (NuGet: Microsoft.Agents.AI.Hosting.A2A.AspNetCore):
app.MapA2AHttpJson("my-agent", "/a2a/my-agent");
#pragma warning restore MEAI001
```

See [`references/SERVER_HOSTING.md`](references/SERVER_HOSTING.md) for `AgentRunMode`, `A2ACardResolver`, agent card discovery, and framework vs. raw SDK trade-offs.

## Security

- Use `AddJwtBearer` with `MapInboundClaims = false` to preserve standard JWT claims (`azp`).
- Use `RequireAuthorization(policy)` with `azp` claim checks to restrict callers.
- Constructor signatures and interface details: [`references/API_REFERENCE.md`](references/API_REFERENCE.md).

## In this repo

| Pattern | Reference implementation |
|---|---|
| Task-based listener (idempotent create + durable lifecycle) | `InfraGate.Planner/Handoff/PlannerHandoffAgentHandler.cs`, `InfraGate.Planner/Tasks/PlannerTaskLifecycle.cs` |
| Message-only listener | `InfraGate.Executor/Handoff/ExecutorAgentHandler.cs`, `InfraGate.Observer/Handoff/ObserverInboundAgentHandler.cs` |
| Fire-and-forget caller | `InfraGate.Observer/Handoff/A2APlannerHandoffClient.cs` |
| Synchronous caller (long timeout) | `InfraGate.Planner/Handoff/A2AExecutorDispatchClient.cs` |
| Keyed `A2AServer` + JWT/`azp` wiring | `InfraGate.{Observer,Planner,Executor}/Program.cs` |
| Idempotent task-store interface | `InfraGate.Planner/Tasks/IPlannerTaskStore.cs` |

Test handlers against `InMemoryTaskStore` and assert persisted state via `ITaskStore.GetTaskAsync` — see the `writing-tests` skill.
