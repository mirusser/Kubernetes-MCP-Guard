# Plan: Roadmap Point 1 — Migrate Observer & Planner to Managed Agent **Workflows**

> Source roadmap: `.agents/Plans/Roadmap/2026-05-29-agent-framework-migration-roadmap.md` (item 1,
> "Migrating to Managed Agent Workflows"). Target framework:
> [microsoft/agent-framework](https://github.com/microsoft/agent-framework).

## Context

`InfraGate.Observer` and `InfraGate.Planner` are LLM-driven background services that today
hand-roll their own agent loop on top of raw `Microsoft.Extensions.AI` (`IChatClient`):

- **Observer** (`ObservationCycleRunner.AnalyzeNamespaceAsync`) builds a `List<ChatMessage>`, calls
  `chatClient.GetResponseAsync` in a `while (toolCallsUsed < maxToolIterations)` loop, parses each
  response **as text** to detect a `{"Tool","Arguments"}` JSON blob (`TryParseToolCall`), executes
  the tool via `IObserverMcpClient.GetToolResultAsync`, and feeds the result back — until the model
  returns a final JSON anomaly array (`ParseLlmOutput`). The cycle loops over `AllowedNamespaces` and
  then dedupes + hands off.
- **Planner** (`BatchProcessor.DecideCoreAsync`) does the same with a `TOOL_CALL:`-prefixed protocol
  plus a "one final call after the cap" workaround, then **deterministically** calls `propose_plan`.
  `BatchProcessor` is a queue `BackgroundService` that, per anomaly, filters → dedupe-gates →
  decides (LLM) → validates → proposes → publishes.

**Pivotal constraint:** the framework's automatic tool iteration (`FunctionInvokingChatClient`) only
works when the underlying `IChatClient` emits native `FunctionCallContent`. The OpenRouter path
(`OpenAI.Chat.ChatClient.AsIChatClient()` wrapped by `RateLimitRetryingChatClient`, which passes
`ChatOptions`/`GetService` straight through) supports this; the custom `AnthropicChatClient` does
**not** (it never sends `options.Tools` and parses only `text` blocks). The hand-rolled JSON protocol
exists to work around that gap.

**Decisions (confirmed with user):**
- **Full commitment to the agent framework** — adopt the framework's **`Workflow` graph** for *both*
  agents (not just the LLM step). This reverses the earlier "keep deterministic orchestration"
  stance. The Observer becomes an explicit agent graph; the Planner batch lifecycle becomes a
  sequential validation workflow with conditional edges.
- **Provider:** OpenRouter only (tool-capable). Anthropic stays parked; no `AnthropicChatClient`
  work.
- **Base branch:** continue on `feature/audit`.
- **Rollout:** replace the manual loops outright; adapt the existing unit suites
  (`ObservationCycleRunnerTests`, `BatchProcessorTests`) to guard behavior. No feature flag.

**Intended outcome:** Observer and Planner are expressed as Agent-Framework `Workflow`s — class-based
deterministic `Executor`s for the safety-critical steps wired to `AgentExecutor`s (a `ChatClientAgent`
that natively iterates the read-only MCP tools), with `AgentThread` holding per-namespace /
per-anomaly chat history. All existing safety, dedupe, validation, audit, and metric behavior is
preserved; the bespoke text-parsing tool protocol is deleted.

## Architecture decisions

1. **Native tool wiring is the floor (overlaps roadmap point 3).** The agent can't iterate tools
   without tools, so surface the already-whitelisted read-only MCP tools as `AITool`s by filtering
   `McpClient.ListToolsAsync()` to `ReadOnlyToolNames` and handing those `McpClientTool`
   (`: AIFunction`) to the agent. Full dynamic scope-based loading (`mcp:tools.readonly` /
   `mcp:tools.propose`) stays roadmap point 3. Exposing only whitelisted tools means the agent
   *physically cannot* call anything else — a structural upgrade over `ToolWhitelist.AssertAllowed`.

2. **`propose_plan` stays deterministic and code-owned.** The Planner's decision `AgentExecutor` gets
   **only read-only** tools — never `propose_plan`. A downstream deterministic `ProposeExecutor`
   validates and calls `propose_plan`. This preserves the safety property that the LLM cannot propose
   a plan directly (today guarded by `ProcessBatchAsync_NonReadOnlyToolCall_RejectsAndContinues`), now
   enforced structurally by graph topology.

3. **Safety-critical logic moves into deterministic `Executor`s, not the agent.** Filtering, dedupe
   gates, argument validation (`OperationArgumentValidator`), propose, audit, and metrics become
   class-based executors with **conditional edges** for drop/skip/continue routing. Only the
   *reasoning* nodes are `AgentExecutor`s.

4. **Final structured output unchanged.** Each agent run ends in assistant text; existing
   `ParseLlmOutput` (Observer) / `ParseDecision` (Planner) keep parsing it inside a parse executor.
   The framework's `responseFormat` structured output is **not** adopted in this pass (keeps the diff
   surgical, preserves parse-failure tests). Noted as a follow-up.

5. **Shared agent/workflow plumbing in `InfraGate.AgentLlm`.** Building a tool-calling
   `ChatClientAgent` (function invocation on, `MaxToolIterations` cap, tool-invocation counter hook)
   is identical for both consumers and belongs in `InfraGate.AgentLlm` (charter: LLM plumbing).
   `ChatClientFactory` stays per-agent per the existing decision. Any shared executor base helpers
   also live here.

## Workflow graph designs

**Observer** (per cycle) — fan-out over namespaces, fan-in to dedupe/handoff:

```
CycleStart (emits AllowedNamespaces)
   └── fan-out per namespace ──┐
        SnapshotExecutor (ISnapshotFetcher.FetchAsync → snapshot JSON)
           → ObserverAgentExecutor (ChatClientAgent + read-only tools, native tool iteration)
              → AnomalyParseExecutor (ParseLlmOutput + SeverityClassifier)
   ┌── fan-in ─────────────────┘
   AggregateExecutor (collect per-ns reports + tool-call counts)
      → DedupeExecutor (AnomalyDedupeStore.ProcessReports)
         → HandoffExecutor (IAnomalyHandoffSink.PublishAsync + audit outbox)
            → CycleResult
```

**Planner** (per batch) — sequential per-anomaly pipeline with conditional edges, fan-in to publish:

```
BatchIntake (fan-out per AnomalyReport)
   FilterExecutor ──drop(condition)──▶ (skip + audit)
      └─continue─▶ DedupeGateExecutor ──active(condition)──▶ (skip + audit)
          └─continue─▶ DecideExecutor (ChatClientAgent, READ-ONLY tools only)
              → ValidateExecutor (ParseDecision + OperationArgumentValidator + AllowedOperationTypes
                                  + batch operation-key dedupe) ──invalid──▶ (skip + metric)
                  └─valid─▶ ProposeExecutor (deterministic propose_plan + audit + dedupe tracking)
                      └─success─▶ collect proposal
fan-in: PublishExecutor (RemediationProposalBatch → IRemediationProposalSink + log)
```

`ObservationCycleLoop` (timer) and `BatchProcessor` (queue host) remain thin shells: they build the
workflow input and run the workflow. Wall-clock caps / shutdown map to workflow-run cancellation;
"truncated" (Observer) = run cancelled with partial/no reports.

## Critical files

Foundation / shared:
- `src/InfraGate.AgentLlm/InfraGate.AgentLlm.csproj`, `…/InfraGate.Observer.csproj`,
  `…/InfraGate.Planner.csproj` — add `Microsoft.Agents.AI` **and** `Microsoft.Agents.AI.Workflows`
  (no central package management; pin versions compatible with `Microsoft.Extensions.AI 10.6.0` /
  `net10.0` — **verify on add**).
- `src/InfraGate.AgentLlm/` — new `ToolCallingAgentFactory` (build `ChatClientAgent` from
  `IChatClient` + `IList<AITool>` + cap; expose tool-invocation count) and any shared `Executor`
  base/util.

Observer:
- `src/InfraGate.Observer/Mcp/IObserverMcpClient.cs` + `ObserverMcpClient.cs` — add
  `Task<IList<AITool>> GetReadOnlyToolsAsync(CancellationToken)` (filter `ListToolsAsync()` to
  `ObserverConventions.ToolNames.ReadOnlyToolNames`). `SnapshotFetcher`'s direct `GetToolResultAsync`
  path stays.
- `src/InfraGate.Observer/Cycle/` — new `Executors/` (Snapshot, ObserverAgent, AnomalyParse,
  Aggregate, Dedupe, Handoff) + workflow assembly. `ObservationCycleRunner.RunAsync` rebuilt to run
  the workflow and assemble `CycleResult` (tool counts, truncation). Delete `TryParseToolCall` +
  `LlmToolCall`; keep `ParseLlmOutput`, severity, dedupe.
- `src/InfraGate.Observer/Prompts/ObserverSystemPrompt.md` — remove the manual `{"Tool","Arguments"}`
  block; keep anomaly-array contract + namespace/iteration guidance.
- `src/InfraGate.Observer/Program.cs` — register tools, agent, workflow.

Planner:
- `src/InfraGate.Planner/Mcp/PlannerMcpClient.cs` (+ `IPlannerMcpClient`) — read-only-tools accessor
  (filter to `PlannerConventions.ToolNames.ReadOnlyToolNames`; excludes `propose_plan`).
- `src/InfraGate.Planner/Cycle/` — new `Executors/` (Filter, DedupeGate, Decide, Validate, Propose,
  Publish) + workflow assembly. `BatchProcessor` reduced to: read queue → run workflow per batch.
  Delete the post-cap "final call" hack, `TryParseToolCall`, `ToolCallPrefix`, `LlmToolCall`; keep
  `ParseDecision`, `OperationArgumentValidator`, `ProposePlanAsync`, dedupe, audit, metrics.
- `src/InfraGate.Planner/Prompts/PlannerSystemPrompt.md` — remove `TOOL_CALL:` protocol; keep the
  decision contract and the "Planner service can validate it and call propose_plan" sentence
  (asserted by `ProcessBatchAsync_SystemPrompt_StatesPlannerServiceCallsProposePlan`).
- `src/InfraGate.Planner/Program.cs` — register tools, agent, workflow.

Tests (adapt — text fixtures → native function calls; add executor + workflow tests):
- `tests/InfraGate.Observer.Tests/UnitTests/FixtureChatClient.cs`,
  `tests/InfraGate.Planner.Tests/UnitTests/FixtureChatClient.cs` — support `FunctionCallContent` so
  the framework's function-invoker drives tool calls.
- `ObservationCycleRunnerTests` / `BatchProcessorTests` — re-express the tool-iteration tests against
  native function calls; `…NonReadOnlyToolCall…` becomes "agent never exposed `propose_plan` → only
  the deterministic propose fires". Other behavior tests (reports, dedupe, severity disagreement,
  handoff, cancellation/wall-clock truncation, validation/timeout/audit/metrics) stay green, possibly
  retargeted at the workflow entry point.

## Task list

### Phase 0: Foundation
- [ ] **Task 1 — Packages + shared agent builder.** Add `Microsoft.Agents.AI` +
  `Microsoft.Agents.AI.Workflows` to the three csprojs (compatible versions verified). Add
  `ToolCallingAgentFactory` in `InfraGate.AgentLlm`. *Verify:* `dotnet build`;
  `dotnet test --filter ToolCallingAgentFactory`. *Scope: M.*
- [ ] **Task 2 — Read-only MCP tools as `AITool`s.** Extend both MCP client abstractions to expose the
  whitelisted read-only tools; assert `propose_plan` absent from Planner's set. *Verify:*
  `dotnet test --filter McpClient`. *Deps: none. Scope: S–M.*

### Checkpoint: Foundation
- [ ] Solution builds; helper + tool accessors green.

### Phase 1: Observer workflow
- [ ] **Task 3 — Observer executors + graph.** Implement Snapshot/ObserverAgent/AnomalyParse/
  Aggregate/Dedupe/Handoff executors and assemble the fan-out/fan-in `WorkflowBuilder` graph.
  *Deps: 1,2. Scope: L (split per-executor during impl).*
- [ ] **Task 4 — Run workflow from `ObservationCycleRunner`.** Rebuild `RunAsync` to feed inputs, run
  the workflow, assemble `CycleResult` (tool counts via the agent hook; truncation via cancellation).
  Delete manual loop + DTOs. *Deps: 3. Scope: M.*
- [ ] **Task 5 — Trim Observer prompt.** *Deps: 4. Scope: S.*
- [ ] **Task 6 — Adapt Observer tests.** `FixtureChatClient` → `FunctionCallContent`; re-express
  tool-iteration tests; keep behavior tests green. *Deps: 4. Scope: M.*

### Checkpoint: Observer
- [ ] `dotnet test tests/InfraGate.Observer.Tests` green; Observer builds; `POST /observe-now` works.

### Phase 2: Planner workflow
- [ ] **Task 7 — Planner executors + graph.** Implement Filter/DedupeGate/Decide/Validate/Propose/
  Publish executors with conditional edges and fan-in; agent gets read-only tools only.
  *Deps: 1,2. Scope: L (split per-executor during impl).*
- [ ] **Task 8 — Run workflow from `BatchProcessor`.** Reduce `BatchProcessor` to a queue host that
  runs the workflow per batch. Delete post-cap hack + DTOs. Preserve caps, dedupe, audit, metrics.
  *Deps: 7. Scope: M.*
- [ ] **Task 9 — Trim Planner prompt.** *Deps: 8. Scope: S.*
- [ ] **Task 10 — Adapt Planner tests.** *Deps: 8. Scope: M.*

### Checkpoint: Planner
- [ ] `dotnet test tests/InfraGate.Planner.Tests` green; Planner builds; handoff→propose smoke works.

### Phase 3: Memory boundaries + cleanup
- [ ] **Task 11 — Memory + provider guard.** `AgentThread` is the sole conversation-state holder in
  the agent executors; fail-fast when a non-tool-capable provider (Anthropic) is configured, with a
  clear "use OpenRouter" message; remove dead code. *Deps: 4,8. Scope: S.*
- [ ] **Task 12 — Docs + roadmap.** Update `src/InfraGate.Observer/README.md`,
  `src/InfraGate.Planner/README.md`, any `docs/mutation-approval-flow.md` reference to the manual
  loop; note the OpenRouter requirement; tick roadmap point 1. *Deps: all. Scope: S.*

### Checkpoint: Complete
- [ ] Full solution builds; full Observer + Planner suites pass; manual smoke (below) succeeds.

## Risks and mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| `Microsoft.Agents.AI[.Workflows]` version vs M.E.AI 10.6.0 / net10.0 | High | Verify compatible versions in Task 1 before building further; bump M.E.AI only if forced. |
| Workflow .NET API surface (exact `Executor` base, run entry point, edge-condition signatures) differs from assumptions | High | Confirm against the installed package + official .NET samples (`dotnet/samples/04-hosting/DurableWorkflows`, `02-agents`) before Task 3. |
| Behavioral mapping of truncation / cross-namespace dedupe ordering / cap-reached→empty onto a graph | High | Drive metrics/truncation from explicit executor outputs; assert against existing truncation + dedupe tests; keep dedupe a single fan-in executor to preserve ordering. |
| Planner safety regression (LLM proposing plans) | High | Topological guarantee: `propose_plan` never in the agent's tool set; keep a test asserting only the deterministic propose fires. |
| OpenRouter *free* models with weak/no function-calling | Med | Document a known-good tool-capable model; provider guard surfaces misconfig early. |
| Tool-call metric fidelity (`ToolCallsUsed`, `infragate.*.tool_calls`) | Med | Count via the agent's per-invocation callback, not text parsing. |
| Workflow refactor is larger than a "swap the LLM step" change | Med | Vertical slices (Observer fully green before Planner); split the two L tasks per-executor at impl time. |

## Open questions (non-blocking)
- Adopt `responseFormat` structured output to replace `ExtractJsonArray`/`ExtractJsonObject`? Defer.
- Expose the assembled workflows as hosted agents (`AddAsAIAgent`) / DevUI for inspection? Out of
  scope for point 1; revisit with roadmap point 4 (observability).

## Verification
- **Build:** `dotnet build` at repo root.
- **Unit tests:** `dotnet test tests/InfraGate.Observer.Tests` and
  `dotnet test tests/InfraGate.Planner.Tests` (run-tests skill). All green.
- **Observer smoke:** run Observer against the local MCP gateway and `POST /observe-now`
  (`ObserveNowEndpoint`); confirm the agent calls read-only K8s tools (visible in
  `RateLimitRetryingChatClient` `llm.input/llm.output` logs) and emits a parsed anomaly batch.
- **Planner smoke:** POST an `AnomalyHandoffBatch` to the handoff endpoint; confirm read-only
  inspection, a validated decision, and exactly one `propose_plan` per accepted anomaly with dedupe
  honored.
- **Metrics:** confirm `infragate.observer.tool_calls` / `infragate.planner.*` counters still record.
