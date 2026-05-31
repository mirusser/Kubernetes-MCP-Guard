# Implementation Plan: Agent Framework Migration §4 — Upgrading AI Observability and Telemetry

**Date:** 2026-05-30
**Roadmap item:** `.agents/Plans/Roadmap/2026-05-29-agent-framework-migration-roadmap.md` §4
**Maps to:** *"Implementing AI observability practices, including usage tracking, cost monitoring, and output quality evaluation."*

## Overview

Today `InfraGate.Observer` and `InfraGate.Planner` record metrics through hand-rolled
`System.Diagnostics.Metrics` meters (`ObserverMetrics`, `PlannerMetrics`), but **nothing exports
them** — there is no `MeterProvider`, no exporter, no `AddOpenTelemetry` anywhere in the repo. The
meters are *dark*. Worse, the only LLM **token-usage** counter (`LlmTokensCounter`) is wired
exclusively inside `CreateAnthropicClient`, which is **unreachable** (the Anthropic provider throws;
OpenRouter is the sole live provider). So the system currently emits **zero** token/cost telemetry
on its real path.

This plan adopts the Microsoft Agent Framework's built-in OpenTelemetry instrumentation and routes it
**into the existing Serilog stack** (`InfraGate.Observability`), with an **opt-in OTLP** export path
for a real backend later. Concretely:

1. Turn `InfraGate.Observability` into the single, deep telemetry seam: an OTel `TracerProvider` +
   `MeterProvider`, a **Serilog span bridge** (each completed span becomes a structured log event
   carrying `gen_ai.*` token/latency tags), and **TraceId/SpanId enrichment** on every log line.
   OTLP export switches on only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
2. Enable framework telemetry at the three seams established in §1–§3:
   - **Agent** level via `AIAgentBuilder.UseOpenTelemetry()` in the shared `ToolCallingAgentFactory`
     (in 1.8.0 this auto-wires the chat client, so one call yields `invoke_agent` + `chat` spans and
     the GenAI token/duration metrics).
   - **Workflow** level via `WorkflowBuilder.WithOpenTelemetry()` at the two build sites
     (`ObservationCycleRunner`, `BatchProcessor`) for per-executor spans.
3. Register the existing custom meters with the new `MeterProvider` 
   and **prune** the dead `LlmTokensCounter` wiring once the framework's `gen_ai.client.token.usage`
   is verified to populate from OpenRouter.

The result: a full trace graph — *workflow → executor → invoke_agent → chat* — with token usage and
latency, visible in the existing structured logs with no new infrastructure, and OTLP-ready.

## Architecture Decisions

These follow the *deepening* lens from `.agents/skills/improve-codebase-architecture` and the repo's
domain language (`CONTEXT.md`).

- **One deep telemetry module, tiny interface.** `InfraGate.Observability` already owns Serilog.
  We extend it to own the whole telemetry pipeline behind a single new seam
  `AddInfraGateTelemetry(this IHostApplicationBuilder, Action<TelemetryOptions>)`. The interface is
  one method; behind it sit resource config, `TracerProvider`, `MeterProvider`, the Serilog bridge,
  enrichment, and the env-gated OTLP exporter. **Leverage:** both `Program.cs` files get full
  observability from one line. **Locality:** all telemetry wiring lives in one module.

- **Telemetry enabled at framework seams, not re-implemented.** We do **not** hand-write spans for
  LLM calls. We call `UseOpenTelemetry()` / `WithOpenTelemetry()` (the framework's
  `OpenTelemetryAgent` / `WorkflowTelemetryContext`) and let them emit GenAI-semconv telemetry. This
  reuses the framework's `OpenTelemetryChatClient` rather than duplicating the convention (cf.
  framework ADR `docs/decisions/0003-agent-opentelemetry-instrumentation.md`).

- **Serilog-first, OTLP-optional (hybrid).** Default deployments need **no new infra**: a
  `BaseProcessor<Activity>` writes each completed span as a structured Serilog event, and a small
  enricher stamps `TraceId`/`SpanId` on every log. When `OTEL_EXPORTER_OTLP_ENDPOINT` is configured,
  the same pipeline also exports OTLP to an Aspire dashboard / Jaeger / Collector — zero code change.

- **Default framework source names; distinguish services by Resource.** `ToolCallingAgentFactory`
  is shared, so we do **not** thread a per-service source name through it. Agent/chat telemetry stays
  on the standard `Experimental.Microsoft.Agents.AI` source; services are told apart by the OTel
  Resource `service.name` (set per `Program.cs`) and the `gen_ai.agent.name` span tag (already
  `observer-{ns}` / `planner-{id}`). Names are centralized as constants in a `TelemetryConventions`
  class (no magic strings — `code-standards`).

- **Sensitive data OFF by default.** Token counts and metadata flow; prompts, responses, tool
  arguments, and executor payloads do **not**. The framework reads
  `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` natively, so capture is opt-in per environment
  and we pass no flags. This matches the repo's redaction/guardrail posture.

- **Prune the dead token counter (deletion test).** Once `gen_ai.client.token.usage` is confirmed
  from OpenRouter, the unreachable `LlmTokensCounter` is deleted. Deleting it concentrates token
  tracking in one place (the framework instrumentation) instead of leaving duplicated dead code — a
  shallow construct removed.

- **New ADR 0026** records: framework OTel via agent/workflow seams; Serilog bridge + trace-context
  correlation; opt-in OTLP; sensitive-data default off; manual-token-counter removal. References
  framework ADR 0003 and repo ADR 0022 (`hidden-agent-seam-vs-bind-as-executor`).

### Telemetry source / meter registry

| Concern | Source / Meter name | Registered on |
|---|---|---|
| Agent + chat spans, GenAI token/duration metrics | `Experimental.Microsoft.Agents.AI` | Tracer + Meter |
| Chat client / function-invocation spans (belt-and-suspenders) | `Experimental.Microsoft.Extensions.AI` | Tracer + Meter |
| Workflow + executor spans | `Microsoft.Agents.AI.Workflows` | Tracer |
| Existing business counters | `InfraGate.Observer` / `InfraGate.Planner` | Meter (per service) |
| Outbound LLM HTTP (OpenRouter) | HttpClient instrumentation | Tracer + Meter |

### Target seams (verified during research)

| Seam | File | Change |
|---|---|---|
| Agent construction (shared) | `src/InfraGate.AgentLlm/ToolCallingAgentFactory.cs:17` | `…new ChatClientAgent(...).AsBuilder().UseOpenTelemetry().Build()`; tuple type `ChatClientAgent` → `AIAgent` |
| Observer workflow | `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs:231` | `.WithOpenTelemetry()` before `.Build()` |
| Planner workflow | `src/InfraGate.Planner/Cycle/BatchProcessor.cs:157` | `.WithOpenTelemetry()` before `.Build()` |
| Pipeline module | `src/InfraGate.Observability/*` | new `AddInfraGateTelemetry`, options, conventions, span processor, enricher |
| Observer host | `src/InfraGate.Observer/Program.cs:47` | add `AddInfraGateTelemetry(...)`, register `ObserverMetrics.Meter` |
| Planner host | `src/InfraGate.Planner/Program.cs:43` | add `AddInfraGateTelemetry(...)`, register `PlannerMetrics.Meter` |

Both agent consumers — `ObservationCycleRunner` (`agent.BindAsExecutor(...)`, line 202) and
`DecideExecutor` (`agent.RunAsync(...)`, line 57) — use only the `AIAgent` surface, so widening the
return type is non-breaking **pending the ADR-0022 `BindAsExecutor` check in Task 4**.

---

## Task List

### Phase 1: Telemetry pipeline foundation (`InfraGate.Observability`)

#### Task 1: Add OpenTelemetry packages + telemetry conventions
**Description:** Add the OTel SDK/exporter/instrumentation packages to `InfraGate.Observability` and
introduce a `TelemetryConventions` static class holding the source/meter name constants and the
env-var names (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`).

**Acceptance criteria:**
- [ ] Packages added (versions aligned with `net10.0` / `Microsoft.Extensions.AI` 10.5.1; current stable OpenTelemetry 1.x): `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`.
- [ ] `TelemetryConventions` (internal) holds the five registry names + env-var keys as `const string`.
- [ ] `InternalsVisibleTo InfraGate.Observability.Tests` added to the csproj.

**Verification:**
- [ ] `dotnet build src/InfraGate.Observability/InfraGate.Observability.csproj`
- [ ] `dotnet test tests/InfraGate.Observability.Tests/...` (existing tests still pass)

**Dependencies:** None
**Files likely touched:** `src/InfraGate.Observability/InfraGate.Observability.csproj`, `src/InfraGate.Observability/TelemetryConventions.cs`
**Estimated scope:** S

#### Task 2: Serilog span bridge + trace-context enricher
**Description:** Add `SerilogSpanProcessor : BaseProcessor<Activity>` that, on `OnEnd`, writes a
structured log event (span name, `gen_ai.operation.name`, provider, model, agent name, input/output
tokens, finish reason, duration ms, status, trace/span/parent IDs). Add `TraceContextEnricher :
ILogEventEnricher` that stamps `TraceId`/`SpanId` from `Activity.Current`. Wire the enricher into
`AddInfraGateObservability` (the existing Serilog config) so all logs correlate. Bridge writes at
**Debug** by default and filters to the GenAI/workflow sources to bound volume from fan-out.

**Acceptance criteria:**
- [ ] `SerilogSpanProcessor` emits one structured event per relevant completed span with token/latency/trace fields as message-template properties (no string interpolation — `code-standards`).
- [ ] `TraceContextEnricher` adds `TraceId`/`SpanId` when an `Activity` is current; adds nothing otherwise.
- [ ] `AddInfraGateObservability` enriches with `TraceContextEnricher`; existing console/file behavior unchanged.

**Verification:**
- [ ] `SerilogSpanProcessorTests`: feed a synthetic `Activity` with `gen_ai.*` tags → captured log event (in-memory Serilog sink) has the expected properties. No mocks.
- [ ] `TraceContextEnricherTests`: `Method_State_ExpectedResult` for present/absent activity.
- [ ] `dotnet test tests/InfraGate.Observability.Tests/...`

**Dependencies:** Task 1
**Files likely touched:** `src/InfraGate.Observability/SerilogSpanProcessor.cs`, `src/InfraGate.Observability/TraceContextEnricher.cs`, `src/InfraGate.Observability/ObservabilityExtensions.cs`, tests
**Estimated scope:** M

#### Task 3: `AddInfraGateTelemetry` + `TelemetryOptions`
**Description:** Add `TelemetryOptions` (`ServiceName`, `ServiceVersion`, `IReadOnlyList<string> MeterNames`, `OtlpEndpoint?`) and `AddInfraGateTelemetry(this IHostApplicationBuilder, Action<TelemetryOptions>)`. It builds the OTel Resource (`service.name`, `service.version`, `service.instance.id`) and registers `AddOpenTelemetry().WithTracing(...).WithMetrics(...)`: tracer adds the three sources + HttpClient instrumentation + the `SerilogSpanProcessor`; meter adds the GenAI meters + caller `MeterNames` + HttpClient + Runtime instrumentation; both add `AddOtlpExporter()` **only** when `OtlpEndpoint`/`OTEL_EXPORTER_OTLP_ENDPOINT` is non-empty.

**Acceptance criteria:**
- [ ] `AddInfraGateTelemetry` registers tracer + meter providers via `OpenTelemetry.Extensions.Hosting`.
- [ ] OTLP exporter present iff endpoint configured (env or option); absent by default.
- [ ] All registered source/meter names come from `TelemetryConventions` + caller `MeterNames`.

**Verification:**
- [ ] `AddInfraGateTelemetryTests` using `AddInMemoryExporter`: a manually emitted Activity on `Experimental.Microsoft.Agents.AI` is captured; a meter recording is captured. No mocks.
- [ ] Test: endpoint unset → no OTLP exporter; endpoint set → OTLP registered.
- [ ] `dotnet test tests/InfraGate.Observability.Tests/...`

**Dependencies:** Task 1, Task 2
**Files likely touched:** `src/InfraGate.Observability/TelemetryOptions.cs`, `src/InfraGate.Observability/ObservabilityExtensions.cs` (or new `TelemetryExtensions.cs`), tests
**Estimated scope:** M

### Checkpoint: Foundation
- [ ] `InfraGate.Observability` builds; all its tests pass.
- [ ] Pipeline verified in isolation (in-memory exporters capture spans + metrics; OTLP toggles on env).
- [ ] No agent/host wiring yet — system behavior unchanged.

---

### Phase 2: Wire agents + hosts (first end-to-end value)

#### Task 4: Agent-level OpenTelemetry in `ToolCallingAgentFactory`
**Description:** Wrap the constructed agent with `.AsBuilder().UseOpenTelemetry().Build()` (default
source name; sensitive-data via env). Widen the tuple return type `ChatClientAgent` → `AIAgent`.
Confirm `BindAsExecutor` accepts the delegating `AIAgent` (ADR 0022); if it requires `ChatClientAgent`,
fall back to wrapping at the call site and record the constraint.

**Acceptance criteria:**
- [ ] `Create(...)` returns `(AIAgent Agent, Func<int> GetToolCallCount)`; tool-call counting still works (counting decorator is below the OTel wrapper).
- [ ] Running the agent emits `invoke_agent` + `chat` Activities on `Experimental.Microsoft.Agents.AI`.
- [ ] `gen_ai.client.token.usage` recorded when the chat response carries `UsageDetails`.

**Verification:**
- [ ] `ToolCallingAgentFactoryTests` with a hand-written fake `IChatClientFactory`/`IChatClient` returning a `ChatResponse` with `UsageDetails` (no mock library); assert spans (via `ActivityListener`/in-memory exporter) and that `GetToolCallCount` still increments.
- [ ] `dotnet test tests/InfraGate.AgentLlm.Tests/...`
- [ ] `dotnet build` of `InfraGate.Observer` + `InfraGate.Planner` (confirms consumers compile with `AIAgent`).

**Dependencies:** None (pipeline-independent), but land after Phase 1 so it can be observed.
**Files likely touched:** `src/InfraGate.AgentLlm/ToolCallingAgentFactory.cs`, `tests/InfraGate.AgentLlm.Tests/...`
**Estimated scope:** S

#### Task 5: Observer host wiring
**Description:** In `src/InfraGate.Observer/Program.cs`, call `builder.AddInfraGateTelemetry(o => { o.ServiceName = "infragate-observer"; o.ServiceVersion = …; o.MeterNames = [ObserverMetrics.MeterName]; })` after `AddInfraGateObservability`. `ObserverMetrics.MeterName` is in-assembly, so accessible.

**Acceptance criteria:**
- [ ] Observer registers the telemetry pipeline; `InfraGate.Observer` meter is exported.
- [ ] Logs carry `TraceId`/`SpanId`; completed agent spans appear as structured log events.

**Verification:**
- [ ] `dotnet build src/InfraGate.Observer/...`
- [ ] Local run (`/run` or compose dev profile): observe `invoke_agent`/`chat` span log events with token usage during a cycle. Paste a sample log line.

**Dependencies:** Task 3, Task 4
**Files likely touched:** `src/InfraGate.Observer/Program.cs`
**Estimated scope:** S

#### Task 6: Planner host wiring
**Description:** Same as Task 5 for `src/InfraGate.Planner/Program.cs` with `service.name = infragate-planner` and `PlannerMetrics.MeterName`.

**Acceptance criteria / Verification:** mirror Task 5 (Planner).
**Dependencies:** Task 3, Task 4
**Files likely touched:** `src/InfraGate.Planner/Program.cs`
**Estimated scope:** S

### Checkpoint: Agents observable end-to-end
- [ ] Running Observer and Planner locally shows agent/chat spans **in the Serilog logs** with token usage + latency, correlated by trace id.
- [ ] Setting `OTEL_EXPORTER_OTLP_ENDPOINT` exports the same telemetry via OTLP (verified against any OTLP listener, e.g. a throwaway collector/Aspire).
- [ ] Full suite green.

---

### Phase 3: Workflow spans, meter export, prune

#### Task 7: Workflow-level OpenTelemetry
**Description:** Add `.WithOpenTelemetry()` before `.Build()` in `ObservationCycleRunner.BuildWorkflow`
and `BatchProcessor.BuildWorkflow`. Sensitive data off (default). This produces
`workflow`/`executor.process`/`message.send` spans on `Microsoft.Agents.AI.Workflows`, nesting the
agent spans beneath the executor that ran them.

**Acceptance criteria:**
- [ ] Both workflows emit executor spans when run; agent spans nest under `executor.process`.
- [ ] No raw report/prompt content in spans (sensitive data off).

**Verification:**
- [ ] Targeted test in `InfraGate.Observer.Tests` / `InfraGate.Planner.Tests`: run a minimal workflow with an `ActivityListener`/in-memory exporter; assert `Microsoft.Agents.AI.Workflows` spans are produced.
- [ ] `dotnet test` for both projects.

**Dependencies:** Task 3 (sources registered); independent of Task 4 but best observed with it.
**Files likely touched:** `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs`, `src/InfraGate.Planner/Cycle/BatchProcessor.cs`, tests
**Estimated scope:** M

#### Task 8: Verify GenAI token metric, then prune dead `LlmTokensCounter`
**Description:** Confirm `gen_ai.client.token.usage` populates from OpenRouter on a live cycle (via the
Serilog span bridge or an OTLP listener). Then delete `LlmTokensCounterName` + `CreateLlmTokensCounter`
from `ObserverMetrics` and `PlannerMetrics`, drop the (dead) calls in both `CreateAnthropicClient`
methods, and remove the two `CreateLlmTokensCounter_ReturnsNonNull` tests. *(Optional follow-up, not
this task: drop the now-unused `Counter<long>?` param on `AnthropicChatClient`.)*

**Acceptance criteria:**
- [ ] Evidence captured that token usage is recorded on the OpenRouter path (log line or OTLP sample).
- [ ] `LlmTokens*` symbols removed from both metrics classes and their dead call sites; nothing else references them (`codegraph_impact` clean).
- [ ] Removed tests deleted; no dangling references.

**Verification:**
- [ ] `dotnet build` (no unresolved symbols).
- [ ] `dotnet test tests/InfraGate.Observer.Tests/... tests/InfraGate.Planner.Tests/...`

**Dependencies:** Task 5, Task 6 (need token metric flowing before deleting the fallback)
**Files likely touched:** `src/InfraGate.Observer/Diagnostics/ObserverMetrics.cs`, `src/InfraGate.Planner/Diagnostics/PlannerMetrics.cs`, both `Llm/ChatClientFactory.cs`, the two metrics test files
**Estimated scope:** S

### Checkpoint: Full trace graph + clean metrics
- [ ] Trace graph *workflow → executor → invoke_agent → chat* visible (logs and/or OTLP).
- [ ] Custom business counters export; token usage present; dead counter gone.
- [ ] Full suite green.

---

### Phase 4: Docs + ADR

#### Task 9: ADR 0026 + documentation
**Description:** Write `docs/adr/0026-opentelemetry-agent-observability-and-serilog-bridge.md`. Update
`src/InfraGate.Observability/README.md` (now owns the telemetry pipeline, not just Serilog), the
Observer/Planner READMEs (telemetry section), and `docs/devs-readme.md` (new env vars:
`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`). Mark roadmap §4
done. Add `Telemetry Pipeline` / `Serilog span bridge` terms to `CONTEXT.md` if used as load-bearing
names.

**Acceptance criteria:**
- [ ] ADR 0026 records the decisions above and references framework ADR 0003 + repo ADR 0022.
- [ ] READMEs and `devs-readme.md` reflect the new pipeline and env vars.
- [ ] Roadmap §4 marked ✅ Done with a short "Delivered" note (matching §1–§3 style).

**Verification:**
- [ ] `verify-readme-docs` pass over touched READMEs (claims match code).
- [ ] Links/paths resolve.

**Dependencies:** Tasks 1–8
**Files likely touched:** `docs/adr/0026-*.md`, `src/InfraGate.Observability/README.md`, `src/InfraGate.Observer/README.md`, `src/InfraGate.Planner/README.md`, `docs/devs-readme.md`, the roadmap file, `CONTEXT.md`
**Estimated scope:** M

### Checkpoint: Complete
- [ ] All acceptance criteria met; full `dotnet test` green.
- [ ] Docs + ADR consistent; roadmap §4 closed.
- [ ] Ready for review.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| OTel package versions incompatible with `net10.0` / M.E.AI 10.5.1 | Med | Pin current stable OpenTelemetry 1.x at impl time; build early in Task 1. |
| OpenRouter doesn't return `usage` for some models → token metric empty | Med | Verify in Task 8 **before** pruning; the Serilog span bridge stays regardless; keep model-level note in docs. |
| `BindAsExecutor` requires concrete `ChatClientAgent`, breaking the `AIAgent` widening | Med | Task 4 verifies against ADR 0022; fallback = wrap at call site / keep concrete return + separate instrumented handle. |
| Span volume from per-report fan-out (Planner) floods logs | Med | Bridge logs at Debug, filtered to GenAI + workflow sources; rely on OTLP for high-fidelity; document sampling knob. |
| Token metric double-counted (`invoke_agent` vs `chat` both emit `gen_ai.client.token.usage`) | Low | Distinguished by `gen_ai.operation.name`; document query guidance in ADR/README. |
| Sensitive content leaking into telemetry | High | Default off; capture only via explicit `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`; assert defaults in tests; call out in ADR. |

## Open Questions

- **Resolved:** export strategy = **hybrid** (Serilog bridge default + opt-in OTLP).
- **Resolved:** manual metrics = **register existing meters + prune dead token counter**.
- **Deferred (not in scope):** standing up a local OTLP backend (Aspire dashboard / Jaeger) in
  `deploy/compose/*` — the hybrid pipeline is backend-ready; add the dashboard service in a follow-up
  if desired. A periodic metric→Serilog exporter is likewise deferred (token/latency already reach
  Serilog via the span bridge).

## Parallelization

- **Sequential:** Task 1 → 2 → 3 (pipeline foundation builds on itself); Tasks 5/6 depend on 3+4;
  Task 8 depends on 5+6.
- **Parallel-safe:** Task 4 (agent seam) can proceed alongside Phase 1; Tasks 5 and 6 are independent
  of each other; Task 7's two edits are independent; doc subtasks in Task 9 are independent.

## Verification (pre-implementation gate)

- [x] Every task has acceptance criteria and a verification step.
- [x] Dependencies identified and ordered (foundation first, high-risk `BindAsExecutor`/version checks early).
- [x] No task touches more than ~5 files.
- [x] Checkpoints between phases.
- [x] Human has reviewed and approved this plan.
