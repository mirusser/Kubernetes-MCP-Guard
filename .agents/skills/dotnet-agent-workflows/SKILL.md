---
name: dotnet-agent-workflows
description: Build LLM agents and Directed Acyclic Graph (DAG) workflows using Microsoft.Agents.AI.Workflows and Microsoft.Agents.AI. Use when the user wants to set up an AI agent, configure tools (AIFunction), or create step-by-step executor pipelines in a .NET project.
---

# .NET Agent Workflows & LLM Agents

## Quick start — LLM Agent

Create a tool, wrap an `IChatClient` into an `AIAgent`, and run it:

```csharp
using Microsoft.Agents.AI;

AIFunction myTool = AIFunctionFactory.Create(
    (string query) => $"Results for {query}",
    name: "search_database",
    description: "Searches the database.");

var chatClient = chatClientFactory.Create()
    .AsBuilder()
    .UseFunctionInvocation(c => c.MaximumIterationsPerRequest = 5)
    .Build();

var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
{
    Name = "my-agent",
    ChatOptions = new ChatOptions
    {
        Instructions = "You are a helpful assistant.",
        Tools = [myTool]
    }
});

var response = await agent.RunAsync("Fix the failing pod.");
Console.WriteLine(response.Text);
```

## Quick start — DAG Workflows

`Microsoft.Agents.AI.Workflows` provides a concurrent, message-driven DAG engine. Data routes from one `Executor` to the next by declared message types.

### 1. Create an Executor

Inherit from `Executor<TInput>`. Declare output type with `[SendsMessage]` for downstream routing, or `[YieldsOutput]` for terminal results:

```csharp
using Microsoft.Agents.AI.Workflows;

[SendsMessage(typeof(ValidationResult))]
public sealed class DecideExecutor(string id, AIAgent llmAgent) : Executor<TItem>(id)
{
    public override async ValueTask HandleAsync(
        TItem message, IWorkflowContext context, CancellationToken ct)
    {
        var response = await llmAgent.RunAsync(message.Summary, cancellationToken: ct);
        if (response.Text == "skip")
            return; // terminate this DAG path
        await context.SendMessageAsync(new ValidationResult { Plan = response.Text }, ct);
    }
}
```

### 2. Wire and run the workflow

```csharp
var workflow = new WorkflowBuilder(intake)
    .AddEdge(intake, filter)
    .AddEdge(filter, decide)
    .AddEdge(decide, action)
    .WithOutputFrom([action])
    .Build();

var run = await InProcessExecution.RunAsync<TInput>(workflow, input, ct);
await using (run.ConfigureAwait(false))
{
    var results = run.OutgoingEvents
        .OfType<WorkflowOutputEvent>()
        .Where(e => e.Is<ResultType>())
        .Select(e => e.As<ResultType>()!)
        .ToList();
}
```

## Best Practices

- **One executor, one job** — Filter, Validate, LLM, Propose should be separate executors.
- **Fan-out** — call `SendMessageAsync` multiple times or use `AddFanOutEdge` for concurrent DAG branches.
- **Early termination** — before returning without `SendMessageAsync`, do any required cleanup first (backoff tracking, audit outbox write). The executor is the last place with full context; callers never see a dropped item.
- **Terminal results** — use `context.YieldOutputAsync(result, ct)` for outputs collected at workflow end.
- **Guardrails** — inject guardrails into the agent builder to monitor tool usage.
- **Side-effectful dependencies** — pass `auditOutbox`, dedup stores, and metrics counters as optional constructor parameters (`= null`). Executors write audit entries and update dedup state on every rejection/drop path, not only on success.

See [`references/EXAMPLES.md`](references/EXAMPLES.md) for filter, output, batch-intake, and fan-out patterns.

## In this repo

| Pattern | Reference implementation |
|---|---|
| LLM agent (function invocation, iteration cap, tool-call guardrail) | `InfraGate.AgentLlm/ToolCallingAgentFactory.cs` |
| Filter / dedupe / decide / validate / propose executors (with audit + backoff on every rejection path) | `InfraGate.Planner/Cycle/Workflow/*.cs` |
| Batch-intake fan-out + workflow build & run | `InfraGate.Planner/Cycle/BatchProcessor.cs` |
| Snapshot fetch → LLM agent → anomaly parse → fan-in aggregate (Observer DAG) | `InfraGate.Observer/Cycle/Workflow/*.cs`, `InfraGate.Observer/Cycle/ObservationCycleRunner.cs` |

Executors are unit-testable by calling `HandleAsync` with a captured `IWorkflowContext` and asserting the messages/outputs they send — see the `writing-tests` skill.
