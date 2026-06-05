# MAF Workflows vs LangGraph: DAGs, Cycles, and a Bounded Refinement Loop

> **Date:** 2026-06-05  
> **Repo:** `kubernetes-MCP-guard` (InfraGate)  
> **Scope:** 
> - whether to adopt LangChain/LangGraph given the two A2A agents (Observer, Planner);
> - how `Microsoft.Agents.AI.Workflows` compares to LangGraph; 
> - what a DAG is; how LangGraph-style loops map onto Microsoft libraries; 
> - and whether to turn the Planner's `decide → validate → propose` chain into a bounded refinement loop.  
>  
> **Sources:** InfraGate source (cited as `file:line`) and official Microsoft Learn docs for Microsoft Agent Framework (linked inline). LangGraph behavior described conceptually.

---

## TL;DR

1. **Adopting LangChain/LangGraph is not worth it for InfraGate.** It's a Python framework; you're a .NET shop running `Microsoft.Agents.AI` + `Microsoft.Agents.AI.Workflows` + A2A. LangGraph's core value (a stateful agent-orchestration graph) is something you already run natively, and its multi-agent model would compete with the A2A seam you deliberately built.
2. **`Microsoft.Agents.AI.Workflows` and LangGraph are siblings under the hood** — both are *modified-Pregel / Bulk-Synchronous-Parallel (BSP) superstep* engines. The real differences are language/ecosystem, the **state model** (typed message-passing vs shared mutable state), the graph *idiom* (feed-forward pipelines vs cyclic agent loops), and the surrounding product (OpenTelemetry/MEAI vs LangSmith/LangChain).
3. **A DAG is a Directed Acyclic Graph** — edges are one-way and no path returns to a visited node. InfraGate's Observer and Planner workflows are DAGs. MAF the *engine* permits cycles (back-edges); InfraGate *chooses* DAGs.
4. **LangGraph's canonical cycle is the ReAct tool loop — and in MAF that loop lives inside the agent** (`UseFunctionInvocation` + `MaxToolIterations`), not in the graph. You already run that, plus an outer scheduling loop. A genuine graph-level loop is available via a conditional back-edge when you need one.
5. **The bounded refinement loop isn't worth building.** Most `ValidateExecutor` rejections aren't re-prompt-fixable; the prompt is *already* hardened (allowed ops + arg schemas + exact-match guard + "return no output rather than invent" — `PlannerSystemPrompt.md:5-8,14-19`); the **outer Observer cycle + `FailedProposalBackoff` already provides governed cross-cycle refinement** at no batch-budget cost; and the only un-handled hallucination class (well-formed-but-wrong) is the **human approval gate's** job, which a decide→validate loop never even triggers on. Net benefit ≈ nil.

---

## 1. Should InfraGate adopt LangChain/LangGraph?

### Bottom line

Not worth it — and not for taste reasons. Two facts dominate:

1. **You're .NET; LangGraph is Python-first.** LangGraph ships for Python and JS/TS. There is no official .NET LangGraph. The only .NET "LangChain" is a community port (`tryAGI/LangChain`) that isn't at parity and has no maintained LangGraph equivalent. Adoption means either a **polyglot runtime** (a Python sidecar) or betting on an **immature port**.
2. **You already run a DAG agent-orchestration engine.** `Microsoft.Agents.AI.Workflows` *is* LangGraph's core value proposition — typed `Executor<T>` nodes, `AddEdge`/`AddFanOutEdge`, fan-in aggregation, early termination. The Observer (`snapshot → LLM → parse → fan-in`) and Planner (`filter → dedupe → decide → validate → propose`) cycles are already graphs.

### How it could realistically look

**Option A — Python orchestration sidecar (the "real" LangGraph):** keep A2A between agents, but move the planning brain to a Python LangGraph service reached over HTTP/gRPC, calling back into the MCP gateway. New deployment artifact, new CI toolchain, new dependency tree, a network hop on the hot path, and your guardrail/approval seam now straddles a language boundary.

**Option B — .NET community port (LangChain.NET):** no new runtime, but you swap a Microsoft-backed, MEAI-integrated engine for a community port that lags upstream and doesn't plug into `IChatClient` middleware (`UseFunctionInvocation`, your `.Use()` guardrails) or Workflows checkpointing.

### What LangGraph would actually buy you vs what you already have

| LangGraph feature | Existing native equivalent | Net gain |
|---|---|---|
| Stateful graph / nodes + edges | `Microsoft.Agents.AI.Workflows`: `Executor<T>`, `WorkflowBuilder.AddEdge` | ~zero |
| Fan-out / fan-in, conditional routing | `AddFanOutEdge`, `AddFanInBarrierEdge`, typed `[SendsMessage]` | ~zero |
| Multi-agent handoff (supervisor/swarm) | **A2A** (`A2AAgent` client ↔ `PlannerHandoffAgentHandler` server) | **negative** — competes with A2A |
| Human-in-the-loop `interrupt()` | A2A task `input-required`/`auth-required` + approval gate | ~zero |
| Durable state / checkpointing | A2A task lifecycle + Postgres task store, plus dedupe/audit stores. *(Workflows checkpointing is available but **unwired** — InfraGate's graphs run to completion in a single process; there is no paused mid-graph state to persist.)* | ~zero |
| LangSmith tracing/evals | OpenTelemetry (already chosen) | competing stack |
| Huge integration catalog (vector stores, loaders) | N/A — infra approval gateway, not a RAG app | irrelevant |
| Tool calling | MCP via `IAgentMcpToolset` / `GatewayAgentMcpToolset` | ~zero |

The one column where LangChain shines — the Python integration ecosystem — is exactly the column an infra approval gateway has no use for.

### The architectural argument that matters most

You *deliberately* split Observer and Planner into independently-deployable services and replaced the HTTP `/handoff` with A2A (Observer = client, Planner = A2A server, task lifecycle in Postgres). **LangGraph's multi-agent model is in-process graph handoffs — it pulls those two agents back into one process.** Adopting it for inter-agent coordination rows directly against the decoupling you just finished. So LangGraph could only sensibly live *inside* one agent (replacing Workflows), where its marginal value is ~zero — or *between* agents, where it conflicts with A2A.

### Pros (being fair)

- **LangSmith** is a strong eval/observability/prompt-management product.
- More mindshare, examples, prebuilt agent patterns (ReAct, supervisor, reflection).
- Unmatched Python ecosystem **if** you ever pivot to RAG/document pipelines.

### Cons / trade-offs

- **Polyglot tax** (Option A): second runtime, CI, vuln surface, deployment artifact, network hop.
- **Immaturity** (Option B): community .NET port, no LangGraph parity, breaks the MEAI `IChatClient` middleware seam your guardrails depend on.
- **Duplicated orchestration** (two DAG engines), **conflict with A2A**, **conflict with your OTel direction**.
- **Guardrail/approval regression risk**: your deterministic gates, hallucination metric, and mutation-approval profile are wired into the .NET function-invocation pipeline.

### Recommendation

Don't adopt now. Revisit only if you need a capability missing from Workflows *and* it's worth a second runtime — realistically only (a) you want LangSmith badly enough to run Python, or (b) the system pivots to document/RAG pipelines. If the underlying itch is graph debugging or evals, that's a tooling need (OTel spans + a viewer like Langfuse/Aspire) decoupled from the runtime — no LangGraph required.

---

## 2. `Microsoft.Agents.AI.Workflows` vs LangGraph

The headline: **they're built on the same execution engine.** Both are modified-Pregel / BSP *superstep* runtimes — the MS docs literally cite the same Pregel paper LangGraph's runtime is named after. At the engine level they're siblings; the differences are elsewhere.

| Axis | LangGraph | `Microsoft.Agents.AI.Workflows` |
|---|---|---|
| Language / runtime | Python + JS/TS | **C#** + Python (InfraGate uses C#) |
| Part of | LangChain ecosystem | Microsoft Agent Framework (SK + AutoGen successor) |
| Node unit | a `node` function over shared state | `Executor<T>` that consumes/emits **typed messages** |
| **Communication model** | **shared mutable state** (channels + reducers) | **typed message-passing** between executors |
| Routing | conditional edges — a routing fn returns the next node | edges keyed on message **type** + predicates |
| Execution engine | Pregel / BSP supersteps | **modified Pregel / BSP supersteps** (same family) |
| Graph idiom | cyclic by default (agent loops, ReAct, reflection) | feed-forward pipelines/DAG idiom; *can* add feedback edges |
| Checkpointing | checkpointers (Memory/SQLite/Postgres/Redis) | checkpoints at **superstep boundaries** |
| Human-in-the-loop | `interrupt()` / `Command(resume=…)` | `RequestPort` / `RequestInfoExecutor` |
| Multi-agent patterns | supervisor, swarm, handoff (in-graph) | sequential, concurrent, **hand-off, magentic** (built-in) |
| Tools | LangChain tools | MEAI `AIFunction` + MCP |
| Observability | **LangSmith** (first-party product) | **OpenTelemetry** + middleware (vendor-neutral) |
| Type safety | dynamic (`TypedDict` hints) | **strong static typing**, compile-time message-route validation |
| Maturity | mature, since 2024, huge adoption | newer (GA late 2025), Microsoft-backed |

### The difference that shapes your code: state model

Same engine, opposite developer mental model.

**LangGraph — nodes mutate a shared state object.** A node receives the *entire* `State`, returns a *partial* update, and channel **reducers** merge it:

```python
class State(TypedDict):
    messages: Annotated[list, operator.add]   # reducer: appends
    plan: str

def decide(state: State) -> dict:             # sees the WHOLE state
    return {"plan": llm(state["messages"])}    # returns a PARTIAL update

g.add_node("decide", decide)
g.add_conditional_edges("decide", route_fn)   # a function picks the next node
```

**MAF Workflows — executors pass typed messages.** An executor consumes one typed input and emits typed outputs; the graph routes them *by type*:

```csharp
[SendsMessage(typeof(ValidationResult))]
public sealed class DecideExecutor(string id, AIAgent agent) : Executor<TItem>(id)
{
    public override async ValueTask HandleAsync(
        TItem message, IWorkflowContext context, CancellationToken ct)
    {
        var r = await agent.RunAsync(message.Summary, cancellationToken: ct);
        await context.SendMessageAsync(new ValidationResult { Plan = r.Text }, ct);
    }                                          // edge routes ValidationResult downstream
}
```

So **LangGraph is state-centric** ("read the blackboard, write to the blackboard") and **MAF is dataflow/message-centric** ("a typed envelope arrives, a typed envelope leaves"). MAF *does* have shared state (`ctx.set_state()`), but it's secondary — mostly checkpointing and cross-executor data — whereas in LangGraph the shared state *is* what flows. Practical upshot: MAF catches a mis-wired edge at **compile time**; LangGraph catches it at runtime when a node reads a missing key.

### Other meaningful differences

- **Cycles / idiom.** LangGraph foregrounds loops (its reason to exist over a plain DAG). MAF's idiom — and InfraGate's usage — is feed-forward typed pipelines.
- **Surrounding product.** LangGraph's gravity well is LangSmith + the LangChain catalog; MAF's is MEAI + OpenTelemetry + DI + middleware + Azure/Foundry. InfraGate already picked the OTel/vendor-neutral side.
- **"Workflow vs agent" is explicit in MAF.** Use a *workflow* when steps are well-defined and you want explicit control; an *agent* when the path is LLM-decided. LangGraph blurs this — the graph *is* the agent.

---

## 3. What MAF Workflows is, what a DAG is, and how loops map onto Microsoft libraries

### What `Microsoft.Agents.AI.Workflows` is

A **type-safe, graph-based orchestration engine**: **executors** (nodes) pass **typed messages** along **edges**, executed via a modified-Pregel / BSP *superstep* model. The four official core concepts, each in InfraGate's code:

**Executor** — a node consuming one typed message and emitting typed messages. `src/InfraGate.Planner/Cycle/Workflow/DecideExecutor.cs:13-26,75`:

```csharp
[SendsMessage(typeof(DecisionContext))]                        // declares output type
internal sealed class DecideExecutor(...) : Executor<AnomalyReport>(id)   // consumes AnomalyReport
{
    public override async ValueTask HandleAsync(AnomalyReport message, IWorkflowContext context, ...)
    {
        ...
        await context.SendMessageAsync(new DecisionContext(message, decision), ...);  // emits downstream
    }
}
```

An executor that returns *without* `SendMessageAsync` **terminates that branch** — the official "drop this item" idiom (used on the early-return paths, `DecideExecutor.cs:39-73`).

**Edges** — typed connections, optionally conditional. Planner wiring (`src/InfraGate.Planner/Cycle/BatchProcessor.cs:239-254`):

```csharp
var builder = new WorkflowBuilder(batchIntake)
    .AddFanOutEdge(batchIntake, filterExecs);          // 1 → N
for (var i = 0; i < batch.Reports.Count; i++)
    builder = builder
        .AddEdge(filterExecs[i],  dedupeExecs[i])
        .AddEdge(dedupeExecs[i],  decideExecs[i])
        .AddEdge(decideExecs[i],  validateExecs[i])
        .AddEdge(validateExecs[i], proposeExecs[i]);
var workflow = builder.WithOutputFrom([.. proposeExecs]).WithOpenTelemetry().Build();
```

Observer adds a fan-in barrier: `.AddFanInBarrierEdge(parseExecutors, aggregate)` (`src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs:254-268`).

**Execution + Events** — run with `InProcessExecution.RunAsync<T>(...)`; harvest terminal results as `WorkflowOutputEvent` (`BatchProcessor.cs:103-115`). `.WithOpenTelemetry()` is the official observability hook.

**Agent vs workflow, nested.** InfraGate uses both: the workflow is the fixed deterministic pipeline, and inside `DecideExecutor` it embeds an `AIAgent` that decides dynamically (`DecideExecutor.cs:112-115`). That nesting is the crux of the loop answer.

### What a DAG is

**DAG = Directed Acyclic Graph.** *Directed* = every edge is one-way (`A → B`). *Acyclic* = following edges forward, you never return to a visited node; data flows strictly "downhill" from input to output.

InfraGate's Observer shape (`ObservationCycleRunner.cs:254-268`):
```text
              ┌─ snapshot-0 → observer-agent-0 → parse-0     ─┐
cycle-input ──┤   ┌─ snapshot-1 → observer-agent-1 → parse-1 ─┤── (fan-in barrier) → aggregate → OUTPUT
   (fan-out)  └─ … snapshot-N → observer-agent-N → parse-N   ─┘
```

Follow any edge and you always advance toward output — you can never loop back. The official validator only checks reachability, type-compatibility, and duplicate edges — **it does not forbid cycles.**

**LangGraph's headline feature is that it is not restricted to DAGs** — cycles (agent loops) are its reason to exist over a plain DAG runner. The nuance: **MAF Workflows is also a general directed-graph engine that permits cycles.** The "DAG engine" label describes how InfraGate wires it, not an engine limit.  
Proof — the official HITL sample builds a literal back-edge cycle:

```csharp
var workflow = new WorkflowBuilder(numberRequestPort)
    .AddEdge(numberRequestPort, judgeExecutor)
    .AddEdge(judgeExecutor, numberRequestPort)   // ← edge BACK = a cycle
    .WithOutputFrom(judgeExecutor)
    .Build();
```

### Implementing LangGraph-style loops with Microsoft libraries

Key reframing: **in LangGraph the canonical cycle is the ReAct tool-calling loop, and in the Microsoft stack that loop lives inside the agent, not the graph.** Five levels; InfraGate already runs three.

- **Level A — the agent's own tool loop (already present).** `UseFunctionInvocation` runs think/act/observe internally, capped by `MaximumIterationsPerRequest`. `DecideExecutor` calls the agent **once** (`agent.RunAsync`, `DecideExecutor.cs:115`); the loop is hidden inside, bounded by `maxToolIterations` (`DecideExecutor.cs:112`, from `opts.MaxToolIterations`, `BatchProcessor.cs:232`). The graph stays a DAG.

- **Level B — a real graph-level loop (back-edge + conditional edge).** Use `AddEdge(source, target, condition: Func<object?, bool>)`:
  ```text
  decide ──→ validate ──(valid)──────────────→ propose → OUTPUT
     ▲            │
     └──(invalid && attempts < N)──────────────┘   ← back-edge
  ```

  Bound it with an attempt counter carried in the message (LangGraph's `recursion_limit` analog) and your existing wall-clock caps (`BatchProcessor.cs:81-82`). Supersteps make each iteration deterministic and checkpointable.

- **Level C — human-in-the-loop loops (`RequestPort`).** The official HITL pattern is a loop: send a request out, get a response, repeat until satisfied. InfraGate does HITL today at the **A2A-task layer** (`lifecycle.RequireApprovalAsync`, `BatchProcessor.cs:158`), not as an in-graph `RequestPort`.

- **Level D — manager-driven multi-agent loops (Magentic).** Built-in iterative refinement with a progress ledger, stall detection, and max-round reset/replan — the analog of a LangGraph supervisor/swarm loop.

- **Level E — the outer "run forever" loop (already present).** `ObservationCycleLoop` is a `Timer` re-running the whole DAG every `CycleIntervalSeconds` (`ObservationCycleLoop.cs:24-28,76`); the Planner's is the `BackgroundService` channel `while` loop (`BatchProcessor.cs:183-189`).

| LangGraph loop construct | Microsoft-stack equivalent |
|---|---|
| ReAct cycle (node loops on tools) | `UseFunctionInvocation` + `MaxToolIterations` *inside the agent* (no graph cycle) |
| `add_conditional_edges` back to a node | `AddEdge(src, target, condition:)` back-edge |
| `interrupt()` human loop | `RequestPort` request/response |
| supervisor / swarm loop | Magentic orchestration |
| `recursion_limit` | attempt counter in the message + wall-clock caps |
| compiled-graph "run forever" | `ObservationCycleLoop` `Timer` / `BackgroundService` while-loop |

**Bottom line:** InfraGate's deliberate design — a deterministic DAG with the only loop (ReAct) sealed inside each agent and bounded by `MaxToolIterations` — is exactly the pattern Microsoft's "agent vs workflow" guidance recommends, which is why no graph cycle has been needed.

---

## 4. Should the `decide → validate → propose` chain become a bounded refinement loop?

The honest answer depends on **what `ValidateExecutor` actually rejects**, because a refinement loop only helps if re-prompting the LLM could plausibly fix the rejection.

### What ValidateExecutor rejects (`ValidateExecutor.cs`)

| Rejection | Line | Cause | Fixable by re-prompting? |
|---|---|---|---|
| **Invalid operation type** | 27 | LLM chose an op not in `AllowedOperationTypes` | **Maybe** — feedback "choose from [...]" could redirect it, *if* a valid op exists for this anomaly |
| **Invalid arguments** | 47 | `OperationArgumentValidator.TryNormalize` failed | **Maybe** — feedback on the normalization error could fix malformed/missing args |
| **Dedupe-in-batch** | 75 | Operation key `{op}:{ns}/{name}` already in this batch | **No** — it's a genuine duplicate; a retry reproduces it |

And `ProposeExecutor` failures (`ProposeExecutor.cs:87-93`) are **gateway/transport errors** (HTTP, JSON, missing planId) — an *infra* problem. Re-prompting the LLM won't help; you'd want a transport retry, not a refinement loop.

So of all the ways an anomaly currently fails, **only two are even theoretically LLM-recoverable**, and both already have a strong upstream guard: the decision is produced under a JSON schema (`ChatResponseFormat.ForJsonSchema<LlmDecisionOutput>()`, `DecideExecutor.cs:30-31`). Structurally-malformed output should therefore already be rare; what remains is more often "the model genuinely picked a disallowed operation because the right fix isn't in the allowed set" — which a retry will *not* fix; it will loop to the cap and fail anyway, having burned N× the tokens and latency.

### What you'd gain

- **Higher yield**: anomalies dropped today on invalid-operation / invalid-arguments could convert to valid proposals after a feedback retry — *if* those failures are common and genuinely feedback-fixable.
- **Lower latency to remediation — not new capability.** The only thing a tight loop adds is fixing the anomaly *this* cycle instead of a later one, because the **outer loop already self-corrects** (see next subsection). For a background remediation system on a cycle cadence, that latency saving is rarely worth the cost below.
- **Richer signal**: passing the exact validation error back to the LLM is a better prompt than a cold retry next cycle.

### Trade-offs / cons (specific to this codebase)

1. **It contradicts the deterministic-gate philosophy.** `ValidateExecutor` is a *hard* deterministic gate; today rejection is clean (drop + backoff + audit). A refinement loop turns "one-shot, deterministic reject" into "negotiate with the LLM until it complies" — more agentic and less predictable, which is against the grain of a safety/approval system where a rejected plan should *stay* rejected.
2. **The backoff/dedupe state machine assumes one attempt.** Every rejection path calls `dedupeStore.TrackActivePlan(..., FailedProposalBackoff)` (`ValidateExecutor.cs:32,57,79`). A loop must *not* record backoff until the final iteration, or the first failed attempt poisons the retry. You'd have to thread retry-awareness through dedupe + audit — breaking the "audit + backoff on every rejection path" invariant.
3. **The audit trail multiplies.** Each rejection writes an audit entry (`DecisionInvalidOperation`, etc.). A loop means N entries per anomaly per failure — noisier audit, and audit here is a compliance artifact. Intermediate attempts would need distinct marking.
4. **Latency & token budget — the batch is shared.** Each retry is another full agent run (with its own internal tool loop). The batch shares one `BatchWallClockCapSeconds` (`BatchProcessor.cs:82`). Spending more time refining one anomaly literally **starves the others** in the same batch, risking truncation of anomalies that would otherwise have succeeded.
5. **Mechanically it's fine.** The superstep model checkpoints each iteration and prevents runaway recursion. The cost is conceptual/economic, not technical.

### Two ways to implement it — and which to prefer

**(A) Graph back-edge** (`validate → decide` conditional edge): the literal LangGraph-style cycle.
- *Pros:* visible in the workflow graph/telemetry; reuses the separate executors.
- *Cons:* complicates dedupe/audit/backoff invariants; multiplies supersteps; harder to bound; blurs the deterministic-gate model; fan-in/output wiring gets trickier.

**(B) Bounded local retry inside the decide step** (a `for` loop around `agent.RunAsync` that feeds the validation error back, keeping decide+validate logic co-located):
- *Pros:* graph stays an acyclic DAG (the official guidance: "consolidate sequential steps into a single executor"); retry state is a local loop variable — no message threading; trivially bounded; contained blast radius; backoff/audit recorded once, after the final attempt.
- *Cons:* the pure validation predicate must be reachable from inside decide (today it lives in the downstream `ValidateExecutor`), so you'd factor `OperationArgumentValidator` + the allowed-ops check into a shared predicate; less visible as a "loop" in graph telemetry.

**Reserve (A) for when a separate node must sit in the loop** (e.g., a human approval via `RequestPort`, or executors with genuinely different DI). For decide→validate refinement, **(B) is cleaner.**

### The one-shot is already hardened (correcting an earlier suggestion)

An earlier draft suggested "inject the `AllowedOperationTypes` list + arg schema into the prompt" as a cheaper alternative. **That is already done — and more thoroughly than a list injection.** The Planner system prompt (`src/InfraGate.Planner/Prompts/PlannerSystemPrompt.md`, embedded via `Program.cs:109`) already:

- enumerates the three allowed operations *with their argument schemas* (l.5-8);
- carries a `⚠ CRITICAL` guard stating `operationType` must be **exactly** one of the three, **names the common wrong values** (tool names like `get_k8s_events`, `ask_observer_to_inspect`), and says any other value will be rejected (l.14);
- instructs **"return no output rather than inventing a new operationType"** (l.14) — i.e. fail closed, not guess;
- gives a JSON exemplar per operation (l.16-19).

So generation-time hardening is not a missing lever; it's in place. The residual rejections are therefore *not* the "model forgot the allowed set" class — they're the "no valid op exists for this anomaly" or "genuine ambiguity" class, which prompt tweaks and retries don't fix.

### The outer loop already *is* the refinement loop

A validation failure or a malformed/hallucinated decision does **not** strand the anomaly. `ValidateExecutor` records `FailedProposalBackoff` (`ValidateExecutor.cs:32,57,79`); the anomaly remains in the cluster, the **Observer re-detects it on its next cycle** (`ObservationCycleLoop` `Timer`), and the Planner gets a fresh attempt — with *fresh cluster state* and the LLM's natural nondeterminism. The `FailedProposalBackoff` window is precisely the **governed rate-limiter** for that cross-cycle retry, so it doesn't hammer. This is a strictly *better* place for "refinement" than an in-batch loop because it: (a) consumes **no** shared `BatchWallClockCapSeconds`, so it never starves sibling anomalies; (b) re-reads the world instead of re-reasoning over stale snapshot data; (c) keeps the graph a clean DAG; (d) reuses backoff/dedupe/audit exactly as designed.

**Hallucination taxonomy — where each class is actually caught:**

| Hallucination class | Caught by | Would a decide→validate loop help? |
|---|---|---|
| Disallowed op / malformed args / no output | prompt guards (rarer) → **outer cycle + backoff** | Marginally (latency only) |
| **Well-formed but semantically wrong** (validation *passes*) | the **human approval gate** (`RequireApprovalAsync`, `BatchProcessor.cs:158`) | **No** — validation passes, so the loop never triggers |
| Dedupe-in-batch / gateway-transport error | dedupe / transport layer | No — not LLM-fixable |

The dangerous class (plausible-but-wrong) sails through validation untouched, so a refinement loop adds nothing there — the **approval gate** is the real control. The benign class is already handled by the outer cycle. That's why the loop's net benefit is ~nil.

### Recommendation

**Don't build the loop.** Generation-time hardening is already in place, the outer Observer cycle + `FailedProposalBackoff` already provides governed cross-cycle refinement at no batch-budget cost, and the only un-handled hallucination class (well-formed-but-wrong) is the approval gate's job, not a loop's. If you ever want to confirm quantitatively, the signal already exists — `guardrailMetrics.RecordDecision(Rejected, InvalidOperation | InvalidArguments)` (`ValidateExecutor.cs:29,54`); if those counters are non-trivial, revisit the *prompt*, not the graph topology.

---

## Appendix — references

### InfraGate source

- `src/InfraGate.Planner/Cycle/Workflow/DecideExecutor.cs` — executor pattern; embedded agent; JSON-schema response format; `MaxToolIterations`.
- `src/InfraGate.Planner/Cycle/Workflow/ValidateExecutor.cs` — deterministic gate: invalid-operation (l.27), invalid-arguments (l.47), dedupe-in-batch (l.75); backoff + audit on each.
- `src/InfraGate.Planner/Cycle/Workflow/ProposeExecutor.cs` — `[YieldsOutput(typeof(RemediationProposal))]`; MCP `propose_plan` call; gateway-error handling.
- `src/InfraGate.Planner/Cycle/Workflow/DecisionContext.cs` — `record(AnomalyReport Report, RemediationDecision Decision)`.
- `src/InfraGate.Planner/Cycle/BatchProcessor.cs` — Planner `WorkflowBuilder` wiring (l.239-254); `InProcessExecution.RunAsync` (l.103-115); `BackgroundService` loop (l.183-189); wall-clock caps (l.81-82).
- `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs` — Observer DAG: fan-out → snapshot→agent→parse → fan-in barrier → aggregate (l.254-268).
- `src/InfraGate.Observer/Cycle/ObservationCycleLoop.cs` — outer scheduling `Timer` (l.24-28, 76).
- `src/InfraGate.Observer/Handoff/A2APlannerHandoffClient.cs`, `src/InfraGate.Planner/Handoff/PlannerHandoffAgentHandler.cs` — the A2A client/server handoff seam.

### Official Microsoft Learn docs

- Workflows overview — https://learn.microsoft.com/agent-framework/workflows/
- Execution model (supersteps / Pregel / BSP) — https://learn.microsoft.com/agent-framework/workflows/workflows#execution-model-supersteps
- Edges (conditional, switch-case) — https://learn.microsoft.com/agent-framework/workflows/edges
- Human-in-the-loop (`RequestPort`, back-edge loop) — https://learn.microsoft.com/agent-framework/workflows/human-in-the-loop
- Magentic orchestration — https://learn.microsoft.com/agent-framework/workflows/orchestrations/magentic
- Observability — https://learn.microsoft.com/agent-framework/workflows/observability
- Agent Framework overview ("agent vs workflow") — https://learn.microsoft.com/agent-framework/overview/
