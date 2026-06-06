# MAF Workflows, Explained

*A conceptual guide to `Microsoft.Agents.AI.Workflows` — what it is, how execution
actually flows, a real-world analogy, and the two workflows already running in InfraGate.*

> Written 2026-06-05. Companion to
> [`maf-workflows-vs-langgraph-dags-cycles-and-refinement-loops.md`](./maf-workflows-vs-langgraph-dags-cycles-and-refinement-loops.md),
> which compares the engine to LangGraph and analyses refinement loops. This article is the
> from-scratch "what is it and how does it work" explainer.

---

## What "MAF" stands for

**MAF = Microsoft Agent Framework** — Microsoft's unified, supported successor to
*Semantic Kernel* and *AutoGen* (those two product lines merged into one stack). It ships
for .NET/C# and Python.

It has two layers, and "Workflows" is the second one:

| Layer | Package | What it gives you |
|---|---|---|
| **Agents** | `Microsoft.Agents.AI` | A *single* agent — a model + tools + instructions, the `AIAgent` abstraction that wraps an `IChatClient`. One smart thing that reasons in a loop. |
| **Workflows** | `Microsoft.Agents.AI.Workflows` | How you wire *many* steps/agents into a **deterministic graph**. The orchestration layer. |

When people say "MAF Workflows" they mean the second row: the graph engine that orchestrates
work between (and around) agents.

---

## The problem Workflows solve

If you hand one agent a big job, it "wings it" in a ReAct loop — call a tool, look at the
result, call another, decide it's done. That is powerful but **nondeterministic, hard to test,
and hard to bound**.

Workflows take the opposite stance: **break the job into small, typed, individually-testable
stations and make the routing between them explicit.**

> "Ask one smart agent to do everything"
> → vs →
> "Build a pipeline where each station is small and the routing is fixed — even if a given
> station happens to call an LLM."

What you get from the graph approach:

- **Explicit structure** — you can see, name, and unit-test every stage.
- **Type safety per stage** — each node declares what it consumes and what it emits.
- **Determinism at the orchestration level** — the wiring is fixed even when a node is an LLM.
- **Parallelism** — fan a batch out into lanes that run concurrently.
- **Checkpointing, human-in-the-loop, and observability** — built into the engine.

---

## The building blocks

- **Executor** — a *node*. You subclass `Executor<TInput>` and override
  `HandleAsync(input, context, ct)`. One unit of work.
  → InfraGate's `DecideExecutor : Executor<AnomalyReport>`.

- **Typed message** — the *thing that flows on the wire*. A node receives one type and emits
  another via `context.SendMessageAsync(...)` (hand to the next node) or
  `context.YieldOutputAsync(...)` (emit to the workflow's output). Attributes declare the
  contract: `[SendsMessage(typeof(DecisionContext))]`, `[YieldsOutput(typeof(RemediationProposal))]`.

- **Edge** — a *directed* connection between nodes:
  - `AddEdge(a, b)` — `b` consumes what `a` sends.
  - `AddFanOutEdge(a, [b, c, d])` — `a`'s output sprays to many lanes (parallelism).
  - `AddFanInBarrierEdge([b, c, d], e)` — `e` **waits for all** of `b`, `c`, `d` before it runs.

- **WorkflowBuilder** — the wiring API:
  `new WorkflowBuilder(start).AddEdge(...).WithOutputFrom(...).Build()`.

- **Runner** — `InProcessExecution.RunAsync<T>(workflow, input)` drives execution and emits
  **events** (`WorkflowOutputEvent`, status events, …) that you subscribe to as the workflow runs.

---

## How it *actually* runs — the superstep model

This is the part that makes a Workflow different from a plain chain of method calls. MAF
Workflows execute on a **Pregel / BSP (Bulk-Synchronous-Parallel) superstep** engine:

1. Execution advances in **rounds** (supersteps).
2. In a round, *every* executor that has a message waiting runs — possibly **in parallel**.
3. Everything they emit is collected at a **synchronization barrier**.
4. Only when the whole round finishes do those messages become the inboxes for the **next** round.
5. Repeat until nothing is in flight.

Two important payoffs fall directly out of this model:

- **Checkpoints** are taken at the barriers — clean, consistent snapshots between rounds. That
  is what lets a workflow **pause and resume** mid-run (including across a process restart).
- A **fan-in barrier node** naturally blocks until *every* parallel branch has caught up,
  because it simply has no inbox until all its upstreams have emitted in a prior superstep.

> **Note — InfraGate doesn't wire checkpointing.** Its two workflows are short-lived and run to
> completion in a single process (`InProcessExecution.RunAsync`), so there is no paused mid-graph
> state to resume. Durability of the approval lifecycle lives in the **A2A task store (Postgres)**,
> not in Workflow checkpoints. See *Where the loops live* below.

---

## Real-life analogy — a factory assembly line

| Factory floor | MAF Workflow |
|---|---|
| A **station** (weld, paint, inspect) — does one job, ignores the rest of the line | An **executor** |
| The **conveyor belt** between stations | An **edge** (`AddEdge`) |
| The **part on the belt** — a "painted chassis" can't go to the engine station | The **typed message** (type safety) |
| One belt **splitting into 4 parallel lines** (assemble 4 doors at once) | **Fan-out** (`AddFanOutEdge`) |
| Final station that **bolts all 4 doors on** — *cannot start until all 4 arrive* | **Fan-in barrier** (`AddFanInBarrierEdge`) |
| The line advancing **one click in lockstep** when the shift-bell rings | A **superstep** + its barrier |
| **Photographing the whole line** at the bell, so a power cut restarts from that frame | A **checkpoint** |
| A worker at one station **sanding → checking → sanding again** | The agent's **ReAct tool loop** *inside* a node — not the line looping back |

The last row is the crux: the *line* never bends backward — it's a **DAG** (Directed Acyclic
Graph), every belt points forward. Any "looping" in such a system lives either *inside* one
station (the agent's tool loop) or *outside* the line entirely (a timer that re-runs the whole
line) — **never in the belts.**

---

## Grounded in InfraGate's two real workflows

InfraGate runs two MAF Workflows in production. Both are DAGs — fan out, do per-lane work, then
collect.

### Observer — the "watcher" (`InfraGate.Observer/Cycle/ObservationCycleRunner.cs`)
```text
                    ┌▶ snapshot(ns-a) ─▶ agent(ns-a) ─▶ parse(ns-a) ─┐
cycleInput ─fanout──┼▶ snapshot(ns-b) ─▶ agent(ns-b) ─▶ parse(ns-b) ─┼─[BARRIER]─▶ aggregate ─▶ output
                    └▶ snapshot(ns-c) ─▶ agent(ns-c) ─▶ parse(ns-c) ─┘
```

One lane per namespace. Each lane: snapshot the namespace → an LLM agent hunts for anomalies →
parse its JSON output. `AddFanInBarrierEdge(parseExecutors, aggregate)` makes `aggregate` wait
for **every** namespace lane before emitting the combined `AnomalyBatch`. The barrier encodes a
real rule: *don't report until all namespaces are in.*

### Planner — the "fixer" (`InfraGate.Planner/Cycle/BatchProcessor.cs`)
```text
                     ┌▶ filter ─▶ dedupe ─▶ decide ─▶ validate ─▶ propose ─┐
batchIntake ─fanout──┼▶ filter ─▶ dedupe ─▶ decide ─▶ validate ─▶ propose ─┼▶ outputs
                     └▶ ...                                                 ┘
```

One lane per anomaly. Each lane: **filter → dedupe → decide** (the LLM picks an operation — the
only "smart" station) **→ validate** (a deterministic whitelist gate) **→ propose** (call the MCP
`propose_plan` tool and `YieldOutputAsync` the proposal). `WithOutputFrom([...proposeExecs])`
harvests each lane's result.

`DecideExecutor` is the textbook node shape:

```csharp
[SendsMessage(typeof(DecisionContext))]
internal sealed class DecideExecutor(/* DI services */) : Executor<AnomalyReport>
{
    public override async ValueTask HandleAsync(
        AnomalyReport message, IWorkflowContext context, CancellationToken ct = default)
    {
        // ... decide (calls the LLM once) ...
        await context.SendMessageAsync(new DecisionContext(message, decision), ct);
    }
}
```

`Executor<AnomalyReport>` declares the input type; `[SendsMessage(typeof(DecisionContext))]`
declares the output type; `context.SendMessageAsync(...)` hands the typed "part" to the next
station on the belt.

---

## Where the loops live (and why the graph stays acyclic)

A common point of confusion is "if agents loop, why is the workflow a DAG?" In InfraGate there
are three distinct loop *levels*, and only one of them is even allowed to be a cycle — and it
isn't in the graph:

| Level | Where it lives | Looping? |
|---|---|---|
| **Agent ReAct loop** | *Inside* a single node (`UseFunctionInvocation` + `MaxToolIterations`) | Yes — bounded, inside the station |
| **The workflow graph** | The executor/edge wiring | **No** — strict DAG, every edge forward |
| **Outer scheduling loop** | *Outside* the graph — Observer's `Timer` re-runs the whole cycle every interval | Yes — re-runs the line from the top |

So the engine *can* express graph cycles (back-edges + conditional edges), but InfraGate
deliberately keeps the graph acyclic and lets the **outer Observer cycle** provide any
"try again next time" behaviour. (See the companion article for why a bounded in-graph
refinement loop wasn't worth building.)

---

## TL;DR

- **MAF = Microsoft Agent Framework** — the merged Semantic Kernel + AutoGen stack.
- **Workflows** (`Microsoft.Agents.AI.Workflows`) is its **graph-orchestration layer**:
  **executors** (stations) pass **typed messages** along **edges** (belts).
- Execution is driven **round-by-round** by a **synchronized superstep (BSP)** engine, which is
  exactly what makes **checkpoint/resume** and **fan-in barriers** work.
- Think **factory assembly line**: stations, conveyor belts, fan-out into parallel lines,
  fan-in barrier for final assembly, the shift-bell as the superstep.
- InfraGate's **Observer** and **Planner** are two such lines — fan out into lanes, do per-lane
  work, then barrier-aggregate (Observer) or collect outputs (Planner). Both are DAGs; the loops
  live *inside* a node or *outside* the graph, never in the wiring.
