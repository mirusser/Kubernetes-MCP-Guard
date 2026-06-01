# A2A — Framework Server Hosting

The agent-framework provides a declarative hosting layer that wraps an `AIAgent` into an A2A server. This is an alternative to the raw SDK path of hand-writing `IAgentHandler` + `A2AServer` + `ITaskStore`.

**Packages required:**

| Package | Purpose |
|---|---|
| `Microsoft.Agents.AI.Hosting.A2A` | `AddA2AServer(agentName)` — wraps `AIAgent` into `A2AServer` |
| `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` | `MapA2AHttpJson` / `MapA2AJsonRpc` — endpoint mapping |

**Status:** All hosting types are marked `[Experimental]` (`DiagnosticIds.Experiments.AIResponseContinuations`). The raw SDK path is stable.

## `AddA2AServer` — declarative server registration

Registers an `AIAgent` as an A2A server, automatically wiring the internal `A2AAgentHandler` bridge and `A2AServer`:

```csharp
using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Agents.AI.Hosting.A2A.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Register an AIAgent first (e.g., ChatClientAgent)
builder.Services.AddSingleton<AIAgent>(sp => { /* ... create agent ... */ });

// Wrap it as an A2A server:
builder.AddA2AServer("my-agent", options =>
{
    options.AgentRunMode = AgentRunMode.AllowBackgroundIfSupported;
});

var app = builder.Build();
app.MapA2AHttpJson("my-agent", "/a2a/my-agent");
app.MapA2AJsonRpc("my-agent", "/a2a/my-agent");  // JSON-RPC binding
await app.RunAsync();
```

Under the hood, `AddA2AServer` creates the internal `A2AAgentHandler` (which implements `IAgentHandler`), constructs an `A2AServer`, and registers it as a keyed singleton. You do not need to manually wire `ITaskStore` or `ChannelEventNotifier`.

## `AgentRunMode` — background execution policy

Controls whether the server accepts background (non-blocking) tasks from callers:

| Value | Behavior |
|---|---|
| `DisallowBackground` | Rejects `return_immediately: true` requests. Callers must request blocking mode. |
| `AllowBackgroundIfSupported` | Accepts background requests. Handler spawns a Task via `ITaskStore` + notifier to deliver updates after the HTTP response. |
| `AllowBackgroundWhen(Func<RequestContext, bool>)` | Dynamic decision per request. |

## `A2ACardResolver` — agent discovery

Resolve remote agents from a well-known card URL or a pre-configured `AgentCard`:

```csharp
var resolver = new A2ACardResolver(new Uri("http://planner:8080"), httpClient);
AIAgent agent = await resolver.GetAIAgentAsync();
```

Or directly from an `AgentCard` object:

```csharp
AIAgent agent = agentCard.AsAIAgent(name: "planner", httpClient: httpClient);
```

Both return an `A2AAgent` (message-only) wrapping an `IA2AClient` created from the card's endpoint.

## Framework vs. raw SDK — when to use each

| Scenario | Use |
|---|---|
| Task lifecycle (`Submitted → Working → AuthRequired → terminal`), durable `ITaskStore`, `TaskUpdater` | Raw SDK (`IAgentHandler` + `A2AServer` directly) |
| Simple request/response agents (chat, RAG, tool-calling) | Framework hosting (`AddA2AServer`) |
| Calling another A2A agent (message-based) | Framework client (`A2AAgent`) |
| Calling another A2A agent (task-based) | Raw SDK (`IA2AClient.SendMessageAsync` directly) |
| Agent card discovery | Framework (`A2ACardResolver` / `AgentCard.AsAIAgent()`) |
