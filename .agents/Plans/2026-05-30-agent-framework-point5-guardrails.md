# Implementation Plan: Agent Framework Migration §5 — Enforcing Framework-Level Guardrails

**Date:** 2026-05-30
**Roadmap item:** `.agents/Plans/Roadmap/2026-05-29-agent-framework-migration-roadmap.md` §5
**Maps to:** *"Defining and maintaining guardrails to ensure reliable, secure, and compliant AI behavior." / "Establishing and monitoring controls for hallucinations..."*

## Overview

Roadmap §5 has two bullets that, read literally, sound like one feature but actually live at **two
different layers**:

1. *"Implement the framework's Interceptors or Filters at the client level."* → an **agent-layer**
   control: framework middleware on the agent's tool calls.
2. *"Before the Planner calls `propose_plan`, run a lightweight, deterministic validation step ... to
   ensure the plan matches the `restart_deployment` / `scale_deployment` whitelist ... prevents wasted
   gateway calls and helps track hallucination rates in the metrics."* → a **workflow-layer** control,
   on the deterministic pipeline that calls `propose_plan`.

**The deterministic validation in bullet 2 already exists.** The Planner workflow is
`Filter → DedupeGate → Decide → **Validate** → Propose`. `ValidateExecutor`
(`src/InfraGate.Planner/Cycle/Workflow/ValidateExecutor.cs`) already:

- rejects any `decision.OperationType` not in `PlannerConventions.OperationTypes.AllowedOperationTypes`
  (the `restart_deployment` / `scale_deployment` / `set_deployment_image` whitelist),
- runs `OperationArgumentValidator.TryNormalize` (per-operation required-argument validation),
- **drops invalid decisions before `ProposeExecutor` ever calls `propose_plan`** — already "prevents
  wasted gateway calls,"
- increments `invalidOperationCounter` / `invalidArgumentsCounter` — a latent hallucination signal,
  now exportable after §4.

Also note: `propose_plan` is invoked **deterministically** by `ProposeExecutor`
(`mcpClient.CallToolAsync(...)`), **not** chosen by the LLM. The Planner agent only ever sees read-only
inspection tools. So the agent-layer middleware (bullet 1) and the propose-gate (bullet 2) genuinely
sit at different layers and must not be conflated or duplicated.

This plan therefore does **not** re-implement deterministic validation. It delivers the missing pieces:

1. A new **`InfraGate.AgentGuardrails`** module — one deep seam owning the guardrail vocabulary, the
   guardrail/hallucination **metric**, and a framework **tool-call guardrail middleware** wired once at
   the shared `ToolCallingAgentFactory` so **both** Observer and Planner get it.
2. A **tool-call guardrail** (framework function-invocation middleware, `AIAgentBuilder.Use(...)`) that
   fails closed on any tool the agent is not explicitly allowed to invoke — defense-in-depth behind §3's
   selection-time `ReadOnlyHint` filtering and behind the McpGateway (the **ultimate** runtime control,
   per the roadmap). It is also the emission point for a tool-level guardrail metric.
3. **Hallucination-rate metrics**, consolidated: `ValidateExecutor`'s existing decision rejections are
   re-pointed to a single, reason-tagged guardrail metric in the new module (replacing the two ad-hoc
   `PlannerMetrics` counters — consolidation, not duplication), so rejections at the decision layer and
   blocks at the tool layer feed one observable signal that §4's exporter already ships.

The result: a single guardrail vocabulary and metric across both agents and both layers, a
framework-idiomatic interceptor at the client level, and a measurable hallucination rate — with no
duplication of the deterministic gate that already earns its keep.

## Architecture Decisions

These follow the *deepening* lens from `.agents/skills/improve-codebase-architecture` and the repo's
domain language (`CONTEXT.md`). Three forks were resolved with the user (2026-05-30):

- **Add a layer; do not duplicate `ValidateExecutor` (decision 1).** `ValidateExecutor` is already a
  **deep module**: a small interface (one executor handling a `DecisionContext`) behind which sit the
  whitelist check, per-operation argument normalization, and in-batch dedupe. *Deletion test:* remove it
  and that complexity scatters across `DecideExecutor`/`ProposeExecutor` and every future caller. It
  earns its keep. §5 leaves its logic intact and only re-points its **metric** emission to the shared
  guardrail vocabulary.

- **Deterministic only; no LLM judge (decision 2).** Consistent with **ADR 0012**
  (`hybrid-severity-llm-proposes-rules-win` — rules win) and `AGENTS.md` "Simplicity First." A secondary
  LLM judge would add token cost, latency, and *another* hallucination surface to a control whose job is
  to *reduce* hallucination risk. Deferred unless the rejection metrics later prove a need.

- **New `InfraGate.AgentGuardrails` module (decision 3).** Two real consumers (Observer **and** Planner)
  already flow through the shared `ToolCallingAgentFactory` — *two adapters = a real seam*, not a
  hypothetical one. The guardrail concept (interceptor + outcome vocabulary + metric) is distinct from
  agent construction (`InfraGate.AgentLlm`) and from observability transport (`InfraGate.Observability`).
  *Deletion test:* fold it back into `AgentLlm` + `Planner` and the guardrail naming/metric semantics
  scatter across two projects and two layers. A dedicated module concentrates that complexity. **Leverage:**
  one `UseToolCallGuardrail(...)` call guards every agent; one `AgentGuardrailMetrics` names every
  guardrail outcome. **Locality:** all guardrail vocabulary lives in one place.

- **Tool-call guardrail = framework function-invocation middleware.** The decided framework mechanism is
  the **decorator/`.Use()`** pattern (agent-framework **ADR 0007**, *Agent Filtering Middleware*). The
  `AIAgentBuilder.Use(Func<AIAgent, FunctionInvocationContext, Func<…>, CancellationToken, ValueTask<object?>>)`
  overload is **confirmed present in `Microsoft.Agents.AI` 1.8.0** (the version already in use). Our agent
  is a `ChatClientAgent`, which the framework requires for function-invocation middleware. The guardrail
  enforces an **explicit, caller-declared allow-list of tool names** (not derived from the passed toolset,
  so a future toolset-filtering regression cannot silently widen it) and **fails closed**: a disallowed
  call is not executed; the agent receives a blocked-result and a metric is recorded.

- **Honest framing — defense-in-depth, not the primary control.** The roadmap itself says the McpGateway
  "acts as the ultimate runtime guardrail." §3 already filters the toolset by `ReadOnlyHint` at
  *selection* time. The tool-call guardrail is a *second*, *invocation-time* assertion of the same
  invariant and the natural home for the tool-level hallucination metric. The plan states this plainly
  rather than overselling a redundant control.

- **Metric consolidation (deepening).** Two shallow counters
  (`infragate.planner.decision.invalid_operation`, `…invalid_arguments`) become one reason-tagged
  guardrail counter. *Deletion test:* the reason dimension concentrates the "rejection" concept that was
  previously smeared across two instruments. Since §4 only just wired metric export and these counters are
  effectively new, the blast radius is tiny (verify with `codegraph_impact`).

- **New ADR 0027** (next free number; latest is 0026) records: framework function-invocation middleware as
  the client-level interceptor; deterministic-only (no LLM judge — references ADR 0012); the two-layer
  distinction and the deliberate non-duplication of `ValidateExecutor`; the new module rationale; the
  guardrail metric taxonomy; the McpGateway as the ultimate control. References agent-framework ADR 0007
  and repo ADRs 0022, 0024, 0026.

### Guardrail metric taxonomy

All names live in `AgentGuardrailConventions` (no magic strings — `code-standards`). Meter
`InfraGate.AgentGuardrails` (v1.0), registered via §4's `TelemetryOptions.MeterNames` hook so it exports
with zero new wiring.

| Instrument | Type | Tags | Emitted by |
|---|---|---|---|
| `infragate.agentguardrails.tool_call.blocked` | `Counter<long>` | `agent.name`, `tool.name`, `guardrail.reason` | `ToolCallGuardrail` middleware (both agents) |
| `infragate.agentguardrails.decision` | `Counter<long>` | `guardrail.outcome` (`accepted`\|`rejected`), `guardrail.reason` | `ValidateExecutor` (Planner) |

`guardrail.reason` values: `tool_not_allowed` (tool layer); `invalid_operation`, `invalid_arguments`,
`dedupe_in_batch` (decision layer). **Hallucination rate** (decision layer) =
`rejected{reason∈{invalid_operation,invalid_arguments}}` / `(accepted+rejected)`; `dedupe_in_batch` is an
*operational* drop, tagged distinctly so dashboards exclude it from the numerator. Tool-block rate uses
§4's per-function spans as the denominator (every allowed call already emits a `chat`/function span).

### New module surface (the deep seam — tiny interface)

`src/InfraGate.AgentGuardrails/` (`internal` by default; `InternalsVisibleTo` its test project):

- `AgentGuardrailConventions` — `const`/`static` names: meter, instrument names, tag keys, reason values.
- `AgentGuardrailPolicy` — `record` holding `IReadOnlySet<string> AllowedToolNames`.
- `AgentGuardrailMetrics` — instance type over the module's static `Meter`; methods
  `RecordToolBlocked(string agentName, string toolName, string reason)` and
  `RecordDecision(GuardrailDecisionOutcome outcome, string reason)`. DI singleton.
- `ToolCallGuardrailExtensions.UseToolCallGuardrail(this AIAgentBuilder, AgentGuardrailPolicy, AgentGuardrailMetrics, string agentName)`
  — adds the function-invocation middleware (allow → `next`; deny → record + blocked result, skip `next`).
- `AgentGuardrailServiceCollectionExtensions.AddAgentGuardrails(this IServiceCollection)` — registers
  `AgentGuardrailMetrics`.

### Target seams (verified during research)

| Seam | File | Change |
|---|---|---|
| Guardrail module (new) | `src/InfraGate.AgentGuardrails/*` | new project: conventions, policy, metrics, middleware, DI ext |
| Agent construction (shared) | `src/InfraGate.AgentLlm/ToolCallingAgentFactory.cs:48` | add `AgentGuardrailMetrics` dep + `AgentGuardrailPolicy` param; insert `.UseToolCallGuardrail(...)` in the builder chain |
| Planner agent call site | `src/InfraGate.Planner/Cycle/Workflow/DecideExecutor.cs:55` | pass read-only allow-set policy |
| Observer agent call site | `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs:201` | pass read-only allow-set policy |
| Decision rejection metric | `src/InfraGate.Planner/Cycle/Workflow/ValidateExecutor.cs:26-43,56-57` | record guardrail decision outcome; drop the two bespoke counter params |
| Planner metrics | `src/InfraGate.Planner/Diagnostics/PlannerMetrics.cs:10-11,22-29` | remove subsumed `invalid_*` counters |
| Planner orchestrator | `src/InfraGate.Planner/Cycle/BatchProcessor.cs:35-56` | inject `AgentGuardrailMetrics`; pass to `ValidateExecutor`; drop the two counters |
| Observer host | `src/InfraGate.Observer/Program.cs:47` | `AddAgentGuardrails()`; add meter name to `AddInfraGateTelemetry` |
| Planner host | `src/InfraGate.Planner/Program.cs:43` | `AddAgentGuardrails()`; add meter name to `AddInfraGateTelemetry` |

The Planner agent's allow-set is the read-only subset of `PlannerConventions.ToolNames` (the 8
`get_*`/`describe_*` names) — it **excludes** `propose_plan` (deterministic, never an agent tool). The
Observer's allow-set is `ObserverConventions.ToolNames` (its 8 read-only names).

---

## Task List

### Phase 1: Guardrail module foundation (`InfraGate.AgentGuardrails`)

#### Task 1: Scaffold the module + conventions + policy
**Description:** Create `src/InfraGate.AgentGuardrails/InfraGate.AgentGuardrails.csproj` (net10.0,
references `Microsoft.Agents.AI` 1.8.0 + `Microsoft.Extensions.AI` 10.6.0) and add it to the solution.
Add `AgentGuardrailConventions` (meter name, the two instrument names, tag keys, reason `const`s — no
magic strings), `AgentGuardrailPolicy` (positional `record` with `IReadOnlySet<string> AllowedToolNames`),
and a `GuardrailDecisionOutcome` enum (`Accepted`, `Rejected`). Scaffold
`tests/InfraGate.AgentGuardrails.Tests` and add `InternalsVisibleTo`.

**Acceptance criteria:**
- [ ] New project builds and is in the solution; matching test project exists with `InternalsVisibleTo`.
- [ ] `AgentGuardrailConventions` holds every guardrail name/tag/reason as `const string`.
- [ ] `AgentGuardrailPolicy` is an immutable positional record.

**Verification:**
- [ ] `dotnet build src/InfraGate.AgentGuardrails/InfraGate.AgentGuardrails.csproj`
- [ ] `dotnet build` of the solution (project wired).

**Dependencies:** None
**Files likely touched:** `src/InfraGate.AgentGuardrails/InfraGate.AgentGuardrails.csproj`, `…/AgentGuardrailConventions.cs`, `…/AgentGuardrailPolicy.cs`, `…/GuardrailDecisionOutcome.cs`, `tests/InfraGate.AgentGuardrails.Tests/*`, `*.sln`
**Estimated scope:** S

#### Task 2: `AgentGuardrailMetrics` + `AddAgentGuardrails`
**Description:** Add `AgentGuardrailMetrics` (instance type over a static `Meter("InfraGate.AgentGuardrails", "1.0")`)
exposing `RecordToolBlocked(agentName, toolName, reason)` and `RecordDecision(outcome, reason)`, each
adding to its counter with the conventioned tags. Add `AddAgentGuardrails(this IServiceCollection)`
registering `AgentGuardrailMetrics` as a singleton.

**Acceptance criteria:**
- [ ] Counters created from the module meter with names/tags from `AgentGuardrailConventions`.
- [ ] `RecordDecision` records `guardrail.outcome` + `guardrail.reason`; `RecordToolBlocked` records `agent.name` + `tool.name` + `guardrail.reason`.
- [ ] `AddAgentGuardrails` registers a singleton.

**Verification:**
- [ ] `AgentGuardrailMetricsTests` using an in-memory `MetricCollector<long>` / `MeterListener` (no mocks — `writing-tests`): assert recorded measurements and tag values for each method, `Method_State_ExpectedResult` naming.
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.Tests/...`

**Dependencies:** Task 1
**Files likely touched:** `src/InfraGate.AgentGuardrails/AgentGuardrailMetrics.cs`, `…/AgentGuardrailServiceCollectionExtensions.cs`, tests
**Estimated scope:** M

#### Task 3: `ToolCallGuardrail` middleware + `UseToolCallGuardrail`
**Description:** Add the function-invocation middleware and the `UseToolCallGuardrail(this AIAgentBuilder,
AgentGuardrailPolicy, AgentGuardrailMetrics, string agentName)` extension. Behavior: if
`context.Function.Name` ∈ `policy.AllowedToolNames` → `await next(context, ct)`; else → record
`tool_call.blocked{reason=tool_not_allowed}` and return a blocked-result (a typed refusal string the
model can read) **without** invoking `next` (fail closed). Default = block-and-continue (do not set
`context.Terminate`), so an agent that strays can still finish with allowed tools; document the choice.

**Acceptance criteria:**
- [ ] Allowed tool name → underlying function executes exactly once; result passes through unchanged.
- [ ] Disallowed tool name → underlying function is **not** executed; a blocked result is returned; one `tool_call.blocked` measurement recorded with the right tags.
- [ ] Extension composes onto an `AIAgentBuilder` and returns it for chaining.

**Verification:**
- [ ] `ToolCallGuardrailTests` (no mocks): build a real `AIFunction` via `AIFunctionFactory.Create` whose body flips a flag; invoke the middleware delegate directly with a constructed `FunctionInvocationContext` for allowed vs disallowed names; assert the flag (executed/not) + the `MetricCollector` measurement.
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.Tests/...`

**Dependencies:** Task 1, Task 2
**Files likely touched:** `src/InfraGate.AgentGuardrails/ToolCallGuardrail.cs`, `…/ToolCallGuardrailExtensions.cs`, tests
**Estimated scope:** M

### Checkpoint: Module foundation
- [ ] `InfraGate.AgentGuardrails` builds; its tests pass (allow/deny behavior + both metrics).
- [ ] No host or factory wiring yet — system behavior unchanged.

---

### Phase 2: Wire the tool-call guardrail at the shared seam (both agents)

#### Task 4: Compose the guardrail into `ToolCallingAgentFactory`
**Description:** Add an `AgentGuardrailMetrics` constructor dependency to `ToolCallingAgentFactory` and an
`AgentGuardrailPolicy` parameter to `Create(...)`. Insert `.UseToolCallGuardrail(policy, guardrailMetrics,
name)` into the existing builder chain (currently `…AsBuilder().UseFunctionInvocation(...).…` and
`agent.AsBuilder().UseOpenTelemetry().Build()`), positioned so the guardrail wraps the actual function
invocation **and** §4's `UseOpenTelemetry` still records function spans. Add the
`InfraGate.AgentLlm → InfraGate.AgentGuardrails` project reference. Update both call sites to pass their
read-only allow-set: Planner `DecideExecutor.cs:55` (subset of `PlannerConventions.ToolNames`, excluding
`propose_plan`), Observer `ObservationCycleRunner.cs:201` (`ObserverConventions.ToolNames`).

**Acceptance criteria:**
- [ ] `Create(...)` takes an `AgentGuardrailPolicy`; both call sites compile and pass their allow-set.
- [ ] A run that attempts a tool **outside** the allow-set is blocked + metered; an allowed tool runs normally and still emits an OTel function span (`tool-call counting` from §1 and OTel from §4 both still work).
- [ ] `InfraGate.AgentLlm` references `InfraGate.AgentGuardrails`.

**Verification:**
- [ ] Extend `ToolCallingAgentFactoryTests` (hand-written `IChatClientFactory`/`IChatClient` returning a `FunctionCallContent` for a disallowed name — no mock library): assert the tool was not invoked, a `tool_call.blocked` measurement was recorded, and `GetToolCallCount` semantics are unchanged for allowed calls.
- [ ] `dotnet test tests/InfraGate.AgentLlm.Tests/...`
- [ ] `dotnet build` of `InfraGate.Observer` + `InfraGate.Planner` (consumers compile).

**Dependencies:** Task 3
**Files likely touched:** `src/InfraGate.AgentLlm/ToolCallingAgentFactory.cs`, `src/InfraGate.AgentLlm/InfraGate.AgentLlm.csproj`, `src/InfraGate.Planner/Cycle/Workflow/DecideExecutor.cs`, `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs`, `tests/InfraGate.AgentLlm.Tests/...`
**Estimated scope:** M

#### Task 5: Host registration + telemetry export
**Description:** In both `Program.cs`, call `services.AddAgentGuardrails()` and add
`AgentGuardrailConventions.MeterName` to that service's §4 `AddInfraGateTelemetry(o => o.MeterNames = [...])`
so the guardrail meter exports through the existing Serilog span bridge / opt-in OTLP. Register/resolve the
read-only `AgentGuardrailPolicy` per service (constructed from the service's `*Conventions.ToolNames`).

**Acceptance criteria:**
- [ ] Both hosts register `AgentGuardrailMetrics`; the `InfraGate.AgentGuardrails` meter is in each service's `MeterNames`.
- [ ] No behavioral change to existing startup; both hosts build.

**Verification:**
- [ ] `dotnet build src/InfraGate.Observer/... && dotnet build src/InfraGate.Planner/...`
- [ ] Local run (`/run` or compose dev profile) of the Planner: trigger a decision that names a non-allowed tool (or temporarily add one to the toolset) and observe a `tool_call.blocked` event in the Serilog span/metric output. Paste a sample log line.

**Dependencies:** Task 4
**Files likely touched:** `src/InfraGate.Observer/Program.cs`, `src/InfraGate.Planner/Program.cs`
**Estimated scope:** S

### Checkpoint: Tool-call guardrail live end-to-end
- [ ] Both agents run with the guardrail active; a disallowed tool call is blocked and appears as a guardrail metric in §4's export.
- [ ] Full suite green.

---

### Phase 3: Consolidate the decision-rejection (hallucination) metric

#### Task 6: Re-point `ValidateExecutor` to the guardrail metric; remove subsumed counters
**Description:** Replace `ValidateExecutor`'s two `Counter<long>?` params with an `AgentGuardrailMetrics`
dependency. On `invalid_operation` and `invalid_arguments` rejections call
`RecordDecision(Rejected, reason)`; on the in-batch dedupe drop call `RecordDecision(Rejected, dedupe_in_batch)`;
on the forward path (before `SendMessageAsync`) call `RecordDecision(Accepted, none)`. Inject
`AgentGuardrailMetrics` into `BatchProcessor` and pass it when constructing `ValidateExecutor` in
`BuildWorkflow`. Remove `DecisionInvalidOperationCounterName`/`DecisionInvalidArgumentsCounterName` +
`CreateDecisionInvalidOperationCounter`/`CreateDecisionInvalidArgumentsCounter` from `PlannerMetrics` and
their wiring in `BatchProcessor` (keep `timeout` and `propose.failed`). Run `codegraph_impact` on the
removed symbols first to confirm no other references.

**Acceptance criteria:**
- [ ] `codegraph_impact` shows the two removed counters have no remaining references.
- [ ] `ValidateExecutor` emits `decision{outcome,reason}` for accept + each reject path; existing drop/forward **behavior** is unchanged.
- [ ] `PlannerMetrics` no longer defines the two `invalid_*` counters; `dotnet build` clean.

**Verification:**
- [ ] Update `WorkflowExecutorTests` (`ValidateExecutor_*`) to construct with `AgentGuardrailMetrics` (real instance + `MetricCollector`, no mocks) and assert the recorded outcome/reason in addition to the existing forwarded/dropped assertions.
- [ ] `dotnet test tests/InfraGate.Planner.Tests/...`

**Dependencies:** Task 2 (metrics type). Independent of Tasks 4–5; can run in parallel with Phase 2.
**Files likely touched:** `src/InfraGate.Planner/Cycle/Workflow/ValidateExecutor.cs`, `src/InfraGate.Planner/Cycle/BatchProcessor.cs`, `src/InfraGate.Planner/Diagnostics/PlannerMetrics.cs`, `tests/InfraGate.Planner.Tests/UnitTests/WorkflowExecutorTests.cs`
**Estimated scope:** M

### Checkpoint: One hallucination signal
- [ ] Decision-layer rejections and tool-layer blocks both feed the `InfraGate.AgentGuardrails` meter; no duplicate counters remain.
- [ ] Hallucination rate (rejected/total decisions, excluding `dedupe_in_batch`) is computable from the export.
- [ ] Full `dotnet test` green.

---

### Phase 4: ADR + docs

#### Task 7: ADR 0027 + documentation
**Description:** Write `docs/adr/0027-framework-tool-call-guardrail-and-hallucination-metrics.md`. Add
`src/InfraGate.AgentGuardrails/README.md`. Update the Observer/Planner READMEs (guardrail section), the
root `README.md` project map, `AGENTS.md` Solution Map, and the `repo-onboarding` SKILL README table with
the new module. Add `Tool-Call Guardrail`, `Guardrail Metric`, and `Hallucination Rate` to `CONTEXT.md`
(new load-bearing domain terms — `improve-codebase-architecture` discipline). Mark roadmap §5 ✅ Done with
a short "Delivered" note matching §1–§4 style.

**Acceptance criteria:**
- [ ] ADR 0027 records the two-layer distinction, deterministic-only (refs ADR 0012), non-duplication of `ValidateExecutor`, the new-module rationale, the metric taxonomy, and the McpGateway-as-ultimate-control framing; references agent-framework ADR 0007 and repo ADRs 0022/0024/0026.
- [ ] New `CONTEXT.md` terms defined; module README accurate; roadmap §5 marked Done.

**Verification:**
- [ ] `verify-readme-docs` pass over touched READMEs (claims match code).
- [ ] Links/paths resolve; `review-mutation-approval-flow` not required (no approval-flow change).

**Dependencies:** Tasks 1–6
**Files likely touched:** `docs/adr/0027-*.md`, `src/InfraGate.AgentGuardrails/README.md`, `src/InfraGate.Observer/README.md`, `src/InfraGate.Planner/README.md`, `README.md`, `AGENTS.md`, `.agents/skills/repo-onboarding/SKILL.md`, `CONTEXT.md`, the roadmap file
**Estimated scope:** M

### Checkpoint: Complete
- [ ] All acceptance criteria met; full `dotnet test` green.
- [ ] Docs + ADR consistent; roadmap §5 closed.
- [ ] Ready for review.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Builder-chain ordering: guardrail middleware suppresses §4 OTel function spans or vice-versa | Med | Task 4 asserts (in test) that an allowed call still emits an OTel function activity and that a blocked call records the guardrail metric; adjust `.Use(...)` position if needed. |
| Tool-call guardrail reads as redundant (toolset already filtered in §3) | Low | Framed explicitly as invocation-time defense-in-depth + metric emission point, behind the Gateway (the ultimate control); the user-stated roadmap requests it. |
| Removing `PlannerMetrics.invalid_*` counters breaks a dashboard/test/ref | Low | `codegraph_impact` before removal (Task 6); counters are effectively new (export only landed in §4); Planner tests updated in the same task. |
| Fail-closed blocking aborts a legitimate run if a hint regresses and a needed tool is mis-listed | Med | Default is block-and-continue (no `Terminate`); allow-set is the explicit read-only convention list both agents already enumerate; metric makes any block visible immediately. |
| Agent-run middleware overload assumed but absent in 1.8.0 | None | Not used — only the function-invocation `.Use(...)` overload, **confirmed in 1.8.0**; deterministic-only means no output-redaction run middleware is needed. |
| New project adds CI/build surface (Docker csproj discovery, coverage) | Low | ADR 0025 dynamic csproj discovery already handles new projects; add the test project to coverage config in Task 1 if required. |

## Open Questions

- **Resolved (user, 2026-05-30):** treat `ValidateExecutor` as the existing deterministic gate — **add a
  layer, do not duplicate**; **deterministic only**, no LLM judge; **new `InfraGate.AgentGuardrails`**
  module.
- **Deferred (not in scope):** a secondary lightweight **LLM judge** (revisit only if rejection metrics
  show well-formed-but-wrong plans slipping through); argument-level guardrails on read tools (namespace
  allow-listing is already enforced by the Gateway/McpServer); applying the guardrail to any future
  mutating agent tool (today `propose_plan` is deterministic and never an agent tool).

## Parallelization

- **Sequential:** Task 1 → 2 → 3 (module builds on itself); Task 4 → 5 (host wiring needs the factory seam).
- **Parallel-safe:** **Phase 3 (Task 6)** depends only on Task 2 and can proceed alongside Phase 2; the two
  call-site edits in Task 4 are independent; doc subtasks in Task 7 are independent.

## Verification (pre-implementation gate)

- [x] Every task has acceptance criteria and a verification step.
- [x] Dependencies identified and ordered (module foundation first; version risk retired up front).
- [x] No task touches more than ~5 files.
- [x] Checkpoints between phases.
- [x] Human has reviewed and approved this plan.
