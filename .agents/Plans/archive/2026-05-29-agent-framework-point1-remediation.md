# Plan: Remediation — Wire ToolCallingAgentFactory + Split ReportProcessorExecutor

> Follows the verification of `.agents/Plans/loose/2026-05-29-agent-framework-point1-managed-workflows.md`.
> Branch: `feature/audit`. Staged state is the baseline.

## Context

Two structural gaps remain after the managed-workflow migration:

1. **`ToolCallingAgentFactory` is dead code.** Both `ObservationCycleRunner.BuildWorkflow` and the
   `ReportProcessorExecutor.DecideCoreAsync` build their `ChatClientAgent` +
   `FunctionInvokingChatClient` + `CountingAiFunction` inline, duplicating logic that the factory
   was specifically designed to centralise. The factory exists in
   `src/InfraGate.AgentLlm/ToolCallingAgentFactory.cs` but is never referenced from production code.
   
2. **`ReportProcessorExecutor` is a 300-line monolith.** The plan specifies five separate executors
   with conditional edges (Filter → DedupeGate → Decide → Validate → Propose). The current
   single class collapses all of them, making independent unit tests impossible and future
   graph changes (e.g. retry propose on 429) require forking the whole class.

Additionally, two plan tasks were never implemented:
- **Task 11** — Provider guard (fail-fast on Anthropic)
- **Task 12** — README + roadmap update

## Architecture decisions

1. **Make `ToolCallingAgentFactory` `public sealed`.** The factory returns `(ChatClientAgent, Func<int>)`.
   All callers are Observer and Planner assemblies that already reference `InfraGate.AgentLlm`. No
   interface is needed: the underlying `IChatClientFactory` is already mockable in tests, and the
   factory's logic is deterministic given the client.

2. **Five separate Planner workflow executors, each `Executor<TIn>` with explicit
   `context.SendMessageAsync()` for conditional routing.** Not calling `SendMessageAsync` is the
   framework's drop/skip path. Each executor is pre-parameterised with its `AnomalyReport` at
   construction time (same pattern as `SnapshotExecutor` / `AnomalyParseExecutor`).

   Message type chain per anomaly:
   ```
   AnomalyHandoffBatch  ─fanout─▶  FilterExecutor
   AnomalyReport        ─continue─▶ DedupeGateExecutor
   AnomalyReport        ─continue─▶ DecideExecutor
   DecisionContext      ─continue─▶ ValidateExecutor
   DecisionContext      ─continue─▶ ProposeExecutor
                            └─YieldsOutput(RemediationProposal)
   ```

   `DecisionContext` is a new internal record: `(AnomalyReport Report, RemediationDecision Decision)`.

3. **`BatchProcessor.BuildWorkflow` chains N×5 executor instances** — one chain per anomaly —
   following the same fluent `WorkflowBuilder` pattern the Observer already uses. `BuildWorkflow`
   fans out from `BatchIntakePassthroughExecutor` to the N `FilterExecutor` instances; edges chain
   the remaining four executors in order per anomaly.

4. **Observer `BuildWorkflow` replaces its inline agent-building block with
   `agentFactory.Create(name, instructions, tools, cap)`.** The private `CountingAiFunction` nested
   class is deleted from the runner (already in the factory).

5. **Provider guard in `ChatClientFactory.Create()`.** When `LlmProvider.Anthropic` is configured,
   throw `InvalidOperationException` with a clear message directing operators to use OpenRouter.
   This is a startup-time guard (the factory is called once per cycle/batch).

## Critical files

### Phase 1 — Wire `ToolCallingAgentFactory`

| File | Change |
|------|--------|
| `src/InfraGate.AgentLlm/ToolCallingAgentFactory.cs` | `internal sealed` → `public sealed` |
| `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs` | Add `ToolCallingAgentFactory agentFactory` constructor param; in `BuildWorkflow` replace the inline `countedTools` + `agentChatClient` + `new ChatClientAgent` + `callCount`/`agentGetCounts` block (lines ~163-185) with `var (agent, getCount) = agentFactory.Create(…)`; delete private `CountingAiFunction` nested class (~241-250) |
| `src/InfraGate.Observer/Program.cs` | Add `builder.Services.AddSingleton<ToolCallingAgentFactory>()` before the `IObservationCycleRunner` registration |
| `tests/InfraGate.Observer.Tests/UnitTests/ObservationCycleRunnerTests.cs` | Wherever `ObservationCycleRunner` is constructed, pass `new ToolCallingAgentFactory(fixture)` (fixture already implements `IChatClientFactory`) |

### Phase 2 — Split `ReportProcessorExecutor`

New files in `src/InfraGate.Planner/Cycle/Workflow/`:

**`DecisionContext.cs`**
```csharp
internal sealed record class DecisionContext(AnomalyReport Report, RemediationDecision Decision);
```

**`FilterExecutor.cs`** — `Executor<AnomalyHandoffBatch>` pre-parameterised with `AnomalyReport`.
- Calls `GetFilterReason(report)` (logic extracted from `ReportProcessorExecutor`).
- If reason is not null: emit `ProposalSkipped` audit (unless `Resolved`); return.
- If null: `await context.SendMessageAsync(report, ct)`.
- Attribute: `[SendsMessage(typeof(AnomalyReport))]`.

**`DedupeGateExecutor.cs`** — `Executor<AnomalyReport>` pre-parameterised with `AnomalyReport`.
- Checks `dedupeStore.HasActivePlan(report.AnomalyId)`.
- If active plan: log + emit `ProposalSkipped` audit; return.
- Else: `await context.SendMessageAsync(report, ct)`.
- Attribute: `[SendsMessage(typeof(AnomalyReport))]`.

**`DecideExecutor.cs`** — `Executor<AnomalyReport>` pre-parameterised with `AnomalyReport`.
- Holds `ToolCallingAgentFactory agentFactory`, `IReadOnlyList<AITool> tools`, `string systemPrompt`, `int maxToolIterations`, `int anomalyWallClockCapSeconds`, `Counter<long>? timeoutCounter`.
- `DecideWithTimeoutAsync`: linked `CancellationTokenSource` + `.CancelAfter`; on timeout increments counter + logs.
- Uses `agentFactory.Create($"planner-{anomalyId[..8]}", systemPrompt, tools, maxToolIterations)`; calls `agent.RunAsync(anomalyJson)`.
- If decision is null: return.
- Else: `await context.SendMessageAsync(new DecisionContext(report, decision), ct)`.
- Attribute: `[SendsMessage(typeof(DecisionContext))]`.

**`ValidateExecutor.cs`** — `Executor<DecisionContext>` pre-parameterised with `AnomalyReport` and the shared `batchOperationKeys`.
- `ParseDecision` + `OperationArgumentValidator.TryNormalize` + `AllowedOperationTypes` check.
- Batch op-key dedupe: `batchOperationKeys.TryAdd`; if collision, log + track active plan + return.
- If invalid: increment counters + log; return.
- Else: `await context.SendMessageAsync(ctx with { Decision = normalized }, ct)`.
- Attribute: `[SendsMessage(typeof(DecisionContext))]`.

**`ProposeExecutor.cs`** — `Executor<DecisionContext>` pre-parameterised with `AnomalyReport`.
- Calls `mcpClient.CallToolAsync(propose_plan, …)`.
- On success: emit audit + log + `dedupeStore.TrackActivePlan`; `await context.YieldOutputAsync(proposal, ct)`.
- On failure/missing plan-id: emit audit + increment counter + `dedupeStore.TrackActivePlan` (backoff TTL).
- Attribute: `[YieldsOutput(typeof(RemediationProposal))]`.

**`BatchProcessor.BuildWorkflow`** — replace the single-executor fan-out with a five-executor chain
per anomaly:
```csharp
var batchOperationKeys = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
var filterExecs = new List<ExecutorBinding>();
for (var i = 0; i < batch.Reports.Count; i++) {
    var report = batch.Reports[i];
    var filter = new FilterExecutor($"filter-{i}", report, dedupeStore, auditOutbox, logger);
    var dedupe = new DedupeGateExecutor($"dedupe-{i}", report, dedupeStore, auditOutbox, logger);
    var decide = new DecideExecutor($"decide-{i}", report, agentFactory, systemPrompt.Value, tools,
        opts.MaxToolIterations, opts.AnomalyWallClockCapSeconds, timeoutCounter, logger);
    var validate = new ValidateExecutor($"validate-{i}", report, batchOperationKeys, dedupeStore,
        invalidOperationCounter, invalidArgumentsCounter, logger);
    var propose = new ProposeExecutor($"propose-{i}", report, mcpClient, dedupeStore,
        auditOutbox, proposeFailedCounter, logger);

    filterExecs.Add(filter);
    builder = builder.AddEdge(filter, dedupe).AddEdge(dedupe, decide)
                     .AddEdge(decide, validate).AddEdge(validate, propose);
}
workflow = new WorkflowBuilder(batchIntake)
    .AddFanOutEdge(batchIntake, filterExecs)
    .WithOutputFrom([..proposeExecs])
    .Build();
```

**Delete:** `src/InfraGate.Planner/Cycle/Workflow/ReportProcessorExecutor.cs`

**Add `ToolCallingAgentFactory` DI and constructor injection:**
- `src/InfraGate.Planner/Program.cs` — `builder.Services.AddSingleton<ToolCallingAgentFactory>()`
- `src/InfraGate.Planner/Cycle/BatchProcessor.cs` — add `ToolCallingAgentFactory agentFactory` constructor param; remove `IChatClientFactory chatClientFactory` field (not needed once Decide owns agent creation) — **check**: `chatClientFactory` may still be referenced via `systemPrompt` loading or integration test setup; remove only after confirming no other uses.

### Phase 3 — Provider guard (Task 11)

**`src/InfraGate.Observer/Llm/ChatClientFactory.cs`** and
**`src/InfraGate.Planner/Llm/ChatClientFactory.cs`** — in `Create()`, add early guard:
```csharp
if (options.Provider == LlmProvider.Anthropic)
    throw new InvalidOperationException(
        "LlmProvider.Anthropic does not support native function calling. " +
        "Configure INFRAGATE_LLM_PROVIDER=OpenRouter.");
```

### Phase 4 — Docs + roadmap (Task 12)

- `src/InfraGate.Observer/README.md` — replace "hand-rolled tool-call loop" description with
  workflow graph description (SnapshotExecutor → agent → AnomalyParseExecutor → CycleAggregateExecutor).
- `src/InfraGate.Planner/README.md` — replace `TOOL_CALL:` batch loop description with the five-executor
  Planner workflow graph.
- `.agents/Plans/Roadmap/2026-05-29-agent-framework-migration-roadmap.md` — tick point 1 as done.

### Phase 5 — Test updates

- `tests/InfraGate.Planner.Tests/UnitTests/BatchProcessorTests.cs` — all existing tests must
  continue to pass after the split (behaviour is preserved). Add at least one focused executor test:
  - `FilterExecutor_Resolved_DropsSilently` — asserts audit outbox NOT called for `Resolved`
  - `DedupeGateExecutor_ActivePlan_Emits_ProposalSkipped`
  - `DecideExecutor_UsesFactory_AgentTools_ExcludeProposePlan`

- Remove any test that directly constructs `ReportProcessorExecutor`.

## Risks

| Risk | Mitigation |
|------|------------|
| `WorkflowBuilder.AddEdge` requires matching `SendsMessage`/input types | Follow the same attribute pattern as `SnapshotExecutor`; run `dotnet build` after each executor |
| `BatchProcessor` constructor change breaks tests that instantiate it directly | Update test constructors in the same commit; grep `new BatchProcessor(` to find all sites |
| `ToolCallingAgentFactory` made public exposes `ChatClientAgent` return type | Both are `Microsoft.Agents.AI` public types; no encapsulation issue |
| `IChatClientFactory` still needed in `BatchProcessor` for backward compat | Check usage: if `chatClientFactory` is used only for agent creation (now in `DecideExecutor`), remove it from `BatchProcessor`; if used elsewhere (e.g. system prompt loading — no, that's reflection), remove it cleanly |

## Verification

1. `dotnet build InfraGate.slnx` — 0 errors, 0 warnings
2. `dotnet test tests/InfraGate.Observer.Tests` — 278+ passed
3. `dotnet test tests/InfraGate.Planner.Tests` — 216+ passed (all preserved + new executor tests)
4. Grep `CountingAiFunction` — appears only inside `ToolCallingAgentFactory.cs`
5. Grep `ReportProcessorExecutor` — zero hits (file deleted)
6. Grep `new ChatClientAgent` — zero hits in Observer runner or Planner workflow files
7. Confirm `ToolCallingAgentFactory` is registered in both `Program.cs` files

## Task list

- [x] T1 — Make `ToolCallingAgentFactory` public; inject + wire in Observer runner; register in Observer `Program.cs`; update Observer tests. *Scope: S*
- [x] T2 — Wire `ToolCallingAgentFactory` into Planner: register in `Program.cs`; add to `BatchProcessor` ctor; remove `chatClientFactory` field if unused. *Scope: S*
- [x] T3 — Implement `DecisionContext.cs`, `FilterExecutor.cs`, `DedupeGateExecutor.cs`. *Scope: S–M*
- [x] T4 — Implement `DecideExecutor.cs` (uses factory). *Scope: M*
- [x] T5 — Implement `ValidateExecutor.cs`, `ProposeExecutor.cs`. *Scope: M*
- [x] T6 — Rewrite `BatchProcessor.BuildWorkflow` to chain the five executors; delete `ReportProcessorExecutor.cs`. *Deps: T3–T5. Scope: M*
- [x] T7 — Update Planner tests: adapt `BatchProcessorTests` to new structure; add focused executor tests. *Deps: T6. Scope: M*
- [x] T8 — Provider guard in both `ChatClientFactory.Create()`. *Scope: S*
- [x] T9 — README + roadmap updates. *Scope: S*
