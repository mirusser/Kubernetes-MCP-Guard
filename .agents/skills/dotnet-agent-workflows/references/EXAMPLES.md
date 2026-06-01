# Agent Workflows — Examples

## Executor patterns

### Filter executor (early termination)

To drop an item without passing it downstream, return without calling `SendMessageAsync`:

```csharp
[SendsMessage(typeof(TItem))]
public sealed class FilterExecutor(string id) : Executor<TItem>(id)
{
    public override async ValueTask HandleAsync(
        TItem message, IWorkflowContext context, CancellationToken ct)
    {
        if (ShouldSkip(message))
            return; // downstream DAG path terminates here

        await context.SendMessageAsync(message, ct);
    }
}
```

### Decide executor (LLM + branching)

The executor calls an LLM and optionally terminates the path:

```csharp
[SendsMessage(typeof(ProposedAction))]
public sealed class DecideExecutor(string id, AIAgent llmAgent) : Executor<TItem>(id)
{
    public override async ValueTask HandleAsync(
        TItem message, IWorkflowContext context, CancellationToken ct)
    {
        var response = await llmAgent.RunAsync(message.Summary, cancellationToken: ct);

        if (response.Text == "no_action")
            return; // terminate

        await context.SendMessageAsync(new ProposedAction { Details = response.Text }, ct);
    }
}
```

### Output executor (yield terminal result)

Terminal executors emit results via `context.YieldOutputAsync`:

```csharp
// see InfraGate.Planner/Cycle/Workflow/ProposeExecutor.cs
[YieldsOutput(typeof(RemediationPlan))]
public sealed class ProposeExecutor(string id, IAgentMcpToolset tools) : Executor<TItem>(id)
{
    public override async ValueTask HandleAsync(
        TItem message, IWorkflowContext context, CancellationToken ct)
    {
        var result = await tools.CallToolAsync("propose_plan", message.ToArgs(), ct);
        await context.YieldOutputAsync(RemediationPlan.From(result), ct);
    }
}
```

Note: `YieldOutputAsync` vs `SendMessageAsync` — use `YieldOutputAsync` for terminal results that should be collected at workflow completion; use `SendMessageAsync` for intermediate messages that continue the DAG.

## Workflow builder — wiring and execution

```csharp
using Microsoft.Agents.AI.Workflows;

var intake = new IntakeExecutor("intake");
var filter = new FilterExecutor("filter");
var decide = new DecideExecutor("decide", llmAgent);
var validate = new ValidateExecutor("validate");
var propose = new ProposeExecutor("propose", tools);

var workflow = new WorkflowBuilder(intake)
    .AddEdge(intake, filter)
    .AddEdge(filter, decide)
    .AddEdge(decide, validate)
    .AddEdge(validate, propose)
    .WithOutputFrom([propose])
    .WithOpenTelemetry()
    .Build();

// Execute with DAG-based concurrency:
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var run = await InProcessExecution.RunAsync<TInput>(
    workflow, input, cancellationToken: cts.Token);
await using (run.ConfigureAwait(false))
{
    var results = run.OutgoingEvents
        .OfType<WorkflowOutputEvent>()
        .Where(e => e.Is<RemediationPlan>())
        .Select(e => e.As<RemediationPlan>()!)
        .ToList();
}
```

### Fan-out / fan-in

Fan-out happens when an executor calls `SendMessageAsync` multiple times or when `AddFanOutEdge` is used:

```csharp
// Single executor sends to multiple downstream nodes concurrently:
builder.AddFanOutEdge(intake, [filterA, filterB]);
```

Each downstream executor receives a copy of the message and runs concurrently.

## Custom intake executor (splitting a batch)

For batch processing, create an intake that fans out individual items:

```csharp
[SendsMessage(typeof(TItem))]
private sealed class BatchIntakeExecutor(string[] targetIds) : Executor<TBatch>("intake")
{
    public override async ValueTask HandleAsync(
        TBatch batch, IWorkflowContext context, CancellationToken ct)
    {
        for (var i = 0; i < batch.Items.Count; i++)
        {
            await context.SendMessageAsync(batch.Items[i], targetId: targetIds[i], cancellationToken: ct);
        }
    }
}
```
