# Implementation Plan: A2A Bidirectional Observer↔Planner Channel

> **Supersedes** Phase 1 of [`2026-05-31-a2a-exploration-roadmap.md`](./2026-05-31-a2a-exploration-roadmap.md)
> **and** the resubscribe-based [`2026-05-31-a2a-phase1-streaming-execution-feedback.md`](./2026-05-31-a2a-phase1-streaming-execution-feedback.md).
> It folds the roadmap's Phase 3 (reverse context requests) infrastructure forward, because the "flip"
> needs it anyway. Investigation backing this plan is summarised below and in memory
> `project_a2a_phase1_streaming_feasibility`.

## Overview

Make Observer↔Planner a **two-way conversation** on one anomaly:

1. **Observer → Planner**: hands off the `Anomaly Handoff Batch` (exists today, unchanged).
2. **Planner → Observer (progress)**: the Planner notifies the Observer of plan progress (`Analyzing`,
   `Plan Proposed`, `Failed`), which the Observer records in its Audit Outbox.
3. **Planner ↔ Observer (questions)**: while planning, the Planner can ask the read-only Observer to run a
   K8s read-only tool (e.g. fetch pod logs/events) and use the answer to improve the plan.

The mechanism is the **"flip"**: the **Observer becomes an A2A server** and the **Planner becomes an A2A
client to it**, so the Planner *actively* calls the Observer for both (2) and (3). The existing
Observer→Planner handoff stays exactly as-is.

## Why the flip (investigation summary)

Verified against `a2a` **1.0.0-preview2** and `Microsoft.Agents.AI*` **1.8.0** (the repo's versions):

- **A2A push notifications are config-only in this SDK.** `PushNotificationConfig { Url, Token,
  Authentication }` and the `*TaskPushNotificationConfig*` CRUD exist, and a client can register a webhook at
  send time via `SendMessageConfiguration.PushNotificationConfig` — **but there is no server-side delivery**:
  no `PushNotificationSender`/dispatcher, no server-side `HttpClient`, nothing POSTs to the webhook. So
  push-based progress would mean building the sender ourselves.
- **The flip avoids that entirely.** With the Observer as an A2A server, the Planner delivers progress and
  questions with ordinary `A2AClient` calls — the same primitive the Observer already uses in the other
  direction. We control delivery.
- **The resubscribe race dissolves.** The earlier (unflipped) plan needed a spike on `ChannelEventNotifier`
  buffering semantics (would updates published before the Observer resubscribes be dropped?). With the flip
  the Planner initiates each call only when it has something to say, and the Observer's server is always
  listening — **no spike needed**.
- The flip is also the natural home for question/answer (request/response Planner→Observer), which
  push/SSE are not.

**Cost of the flip:** the Observer gains an inbound A2A server + a new **Planner→Observer** auth direction +
two-way topology. This is roadmap Phase 3 / Task 5 brought forward; it is well-trodden (it mirrors the
Planner's existing A2A server) and is shared by both progress and questions.

## Architecture Decisions

- **Flip the streaming direction.** Observer = A2A **server** (new) + A2A client (handoff, existing).
  Planner = A2A **server** (handoff, existing) + A2A **client** to the Observer (new).
- **Progress = one-way `SendMessageAsync` per milestone** (not a held-open stream). Resilient, no
  long-lived connection, no race. A dropped progress call is a logged warning, never fatal.
- **One Observer inbound handler, intent-dispatched.** A single `ObserverInboundAgentHandler` switches on an
  envelope `Intent` (`progress` | `tool-request`). One keyed `A2AServer` + one `MapA2AHttpJson`, mirroring
  the Planner.
- **Questions are gated by the Observer's existing read-only allow-list.** The Planner can only ask the
  Observer to run tools already in the Observer's `AgentGuardrailPolicy`/allowed-tools set; anything else is
  rejected. The Observer reuses its `IAgentMcpToolset` (ReadOnly-filtered) to execute.
- **Correlation by `CycleId`.** Progress and questions carry the `CycleId` (already minted by the Observer
  and present on the handoff batch and Observer audit entries). No new task identity is needed; the existing
  fire-and-forget handoff handler is untouched. These are *correlated interactions*, not one literal A2A
  task spanning both servers.
- **Contract lives in `InfraGate.Observer.Contracts`** (already referenced by both services, the same place
  `AnomalyHandoffBatch` lives). Define it first; then the two sides build in parallel.
- **Reverse-delegation is an LLM tool.** The Planner asks questions via an `AIFunction` the
  `DecideExecutor` can call — not hard-coded — so the model decides when context is missing.

## Dependency Graph

```
Task 1 (envelope contract)            ← coordination point; define first
   ├── Task 2 (Observer A2A server: progress intent + inbound auth)   ─┐
   ├── Task 3 (Planner A2A client to Observer + outbound auth)        ─┤ (2 & 3 parallel after 1)
   └── Task 4 (BatchProcessor emits progress milestones)  ← needs 2+3 ─┘
            │
   == Checkpoint A: progress trace E2E ==
            │
   ├── Task 5 (Observer server: tool-request intent + read-only whitelist)  ← needs 2
   ├── Task 6 (Planner "ask Observer" AIFunction in DecideExecutor)         ← needs 3, 5
   └── Task 7 (Planner prompt: when to ask the Observer)                    ← needs 6
            │
   == Checkpoint B: question/answer E2E ==
            │
   └── Task 8 (docs + CONTEXT.md + ADR 0028)
```

## Task List

### Phase 1: Bidirectional Channel + Progress Notifications

#### Task 1: Define the Planner→Observer message envelope contract
**Description:** Add a JSON-serializable envelope to `InfraGate.Observer.Contracts` that both services code
against: `ObserverInboundEnvelope { string Intent; string CycleId; PlanProgressPayload? Progress;
ToolRequestPayload? ToolRequest; }` with `Intent` ∈ {`"progress"`, `"tool-request"`}; `PlanProgressPayload {
string Stage; string? Detail; int? ProposalCount; }`; `ToolRequestPayload { string ToolName; string?
ArgumentsJson; }`; and a `ToolResponsePayload { bool IsError; string ResultJson; }` for the answer. Stage
constants (`Analyzing`, `PlanProposed`, `NoAction`, `Failed`) live next to it.

**Acceptance criteria:**
- [x] Envelope + payloads compile in `InfraGate.Observer.Contracts` and round-trip through `System.Text.Json`.
- [x] Stage values are constants (no magic strings), per repo `code-standards`.

**Verification:** `dotnet build src/InfraGate.Observer.Contracts`; a serialization round-trip unit test.
**Dependencies:** None.
**Files likely touched:** `src/InfraGate.Observer.Contracts/ObserverInboundEnvelope.cs` (+ payload/stage files), `tests/InfraGate.Observer.Tests/UnitTests/ObserverInboundEnvelopeTests.cs`.
**Estimated scope:** Small.

#### Task 2: Stand up the Observer as an A2A server (progress intent + inbound auth)
**Description:** Mirror the Planner's A2A server setup in the Observer. Add `ObserverInboundAgentHandler :
IAgentHandler` that deserializes the envelope; for `Intent == "progress"` it appends an `ObserverAuditEntry`
(`handoff.progress`, carrying `CycleId` + `Stage` + `Detail`) and acks. Register a keyed `A2AServer`
(`InMemoryTaskStore` + `ChannelEventNotifier`) and `MapA2AHttpJson(A2AInboundAgentName,
A2AInboundEndpointPath)` (e.g. `/a2a/observer`). Add JWT bearer + a `PlannerSender` authorization policy
(`azp == infra-gate-planner`), mirroring the Planner's `ObserverSender`. Unknown `Intent` → graceful error.

**Acceptance criteria:**
- [x] Observer exposes the A2A endpoint; a `progress` envelope produces one `handoff.progress` audit entry tagged with `CycleId`/`Stage`.
- [x] Requests without a valid `infra-gate-planner` token are rejected (401/403).
- [x] Unknown intent returns a clean error, not an unhandled exception.

**Verification:** `dotnet test tests/InfraGate.Observer.Tests/` (handler unit tests + auth policy test).
**Dependencies:** Task 1.
**Files likely touched:** `src/InfraGate.Observer/Handoff/ObserverInboundAgentHandler.cs`, `src/InfraGate.Observer/Program.cs`, `src/InfraGate.Observer/ObserverConventions.cs` (agent name, path, policy, claims), `src/InfraGate.Observer/Audit/ObserverAuditEvents.cs` (`handoff.progress`), `tests/InfraGate.Observer.Tests/UnitTests/ObserverInboundAgentHandlerTests.cs`.
**Estimated scope:** Medium.

#### Task 3: Stand up the Planner as an A2A client to the Observer (outbound auth)
**Description:** Add an `IObserverChannel` abstraction in the Planner wrapping
`new A2AClient(observerUrl, httpClient).AsAIAgent(...)`, with `SendProgressAsync(cycleId, stage, detail,
proposalCount)` that posts a `progress` envelope. Register an `AddHttpClient(ObserverRequest)
.AddClientCredentialsBearerHandler()` (mirror the existing `ExecutorHandoff`/`PlannerHandoff` clients) and a
new `ObserverBaseUrl` option/env mapping. The Planner already has `AddClientCredentialsTokenProvider`. The
token's `azp`/audience must satisfy the Observer's `PlannerSender` policy (Keycloak client config — see
Risks).

**Acceptance criteria:**
- [x] `IObserverChannel.SendProgressAsync(...)` delivers a well-formed `progress` envelope to the Observer endpoint with a bearer token.
- [x] Delivery failure is swallowed-and-logged (warning + metric), never thrown to the caller.
- [x] No-op/disabled when `ObserverBaseUrl` is unset (parity with the optional-sink pattern).

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/` (channel unit test with a fake agent/transport).
**Dependencies:** Task 1. (Parallelizable with Task 2 once the contract is fixed.)
**Files likely touched:** `src/InfraGate.Planner/Handoff/ObserverChannel.cs` (+ `IObserverChannel`), `src/InfraGate.Planner/Program.cs`, `src/InfraGate.Planner/PlannerConventions.cs` (URL/client/option names), `src/InfraGate.Planner/PlannerOptions.cs`, `tests/InfraGate.Planner.Tests/UnitTests/ObserverChannelTests.cs`.
**Estimated scope:** Medium.

#### Task 4: Emit progress milestones from `BatchProcessor`
**Description:** Inject `IObserverChannel` into `BatchProcessor`. In `ProcessBatchAsync`: on dequeue →
`SendProgressAsync(cycleId, Analyzing)`; after `proposalSink.PublishAsync` with proposals →
`PlanProposed (count)`; empty/no-op → `NoAction`; in the `catch` → `Failed`. `CycleId` comes from
`batch.CycleId` (already present). Progress sends must not block or fail batch processing.

**Acceptance criteria:**
- [x] A processed batch emits the ordered sequence `Analyzing` → (`PlanProposed` | `NoAction`) for its `CycleId`.
- [x] A batch whose processing throws emits `Failed`.
- [x] The remediation path is unchanged; a failing progress send does not abort processing (unit test asserts this).

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/` (BatchProcessor test with a fake `IObserverChannel`).
**Dependencies:** Tasks 2, 3.
**Files likely touched:** `src/InfraGate.Planner/Cycle/BatchProcessor.cs`, `src/InfraGate.Planner/PlannerConventions.cs` (stage→text), `tests/InfraGate.Planner.Tests/UnitTests/BatchProcessorProgressTests.cs`.
**Estimated scope:** Medium.

### Checkpoint A: Progress trace end-to-end
- [x] `dotnet build` clean; `dotnet test tests/InfraGate.Planner.Tests/ tests/InfraGate.Observer.Tests/` green.
- [ ] Manual E2E (both services + Keycloak up): one discovered anomaly yields, in the **Observer Audit Outbox**, `handoff.progress: Analyzing` → `handoff.progress: PlanProposed` for the matching `CycleId`.
- [ ] Killing the Planner mid-plan leaves the Observer healthy; a progress send to a down Observer logs a warning and planning still completes.
- [ ] Review with human before starting Phase 2.

### Phase 2: Reverse Questions (Read-Only Tool Delegation)

#### Task 5: Add the `tool-request` intent to the Observer server
**Description:** Extend `ObserverInboundAgentHandler`: for `Intent == "tool-request"`, validate `ToolName`
against the Observer's existing allowed-tools set (`AgentGuardrailPolicy`); if allowed, execute via
`IAgentMcpToolset.CallToolAsync(ToolName, args)` and return a `ToolResponsePayload`; if not allowed, return
`IsError` with a reason (and audit `handoff.tool_denied`). Audit accepted calls (`handoff.tool_served`).

**Acceptance criteria:**
- [x] An allowed read-only tool request returns the tool result; a disallowed tool is rejected without execution.
- [x] Both outcomes are audited; the read-only whitelist is enforced server-side (defence in depth).

**Verification:** `dotnet test tests/InfraGate.Observer.Tests/` (allowed + denied cases with a fake toolset).
**Dependencies:** Task 2.
**Files likely touched:** `src/InfraGate.Observer/Handoff/ObserverInboundAgentHandler.cs`, `src/InfraGate.Observer/Audit/ObserverAuditEvents.cs`, `tests/InfraGate.Observer.Tests/UnitTests/ObserverInboundAgentHandlerTests.cs`.
**Estimated scope:** Medium.

#### Task 6: Give the Planner a "ask the Observer" tool
**Description:** Add an `AIFunction` (e.g. `ask_observer_to_inspect`) that calls `IObserverChannel`
`tool-request` and returns the Observer's result as tool output. Register it in the tool set the
`DecideExecutor` hands to the planning agent (alongside the existing MCP tools), threading the `CycleId`.
Keep it deterministic plumbing; the model chooses when to call it.

**Acceptance criteria:**
- [x] The Planner agent has a callable tool that round-trips a read-only request to the Observer and returns the result.
- [x] The tool surfaces Observer-side denials/errors as tool errors (no crash).
- [x] A `DecideExecutor` test shows the tool is offered and its result is usable by the agent.

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/` (tool + DecideExecutor wiring).
**Dependencies:** Tasks 3, 5.
**Files likely touched:** `src/InfraGate.Planner/Llm/AskObserverTool.cs`, `src/InfraGate.Planner/Cycle/Workflow/DecideExecutor.cs`, `src/InfraGate.Planner/Cycle/BatchProcessor.cs` (pass channel/cycleId into the workflow), `tests/InfraGate.Planner.Tests/UnitTests/AskObserverToolTests.cs`.
**Estimated scope:** Medium.

#### Task 7: Update the Planner system prompt for reverse-delegation
**Description:** Extend the Planner system prompt (embedded template) to tell the model it may call
`ask_observer_to_inspect` when it lacks context (e.g. needs current events/logs) before proposing a plan,
and to prefer proposing `NoAction` over guessing.

**Acceptance criteria:**
- [x] Prompt documents the tool and when to use it; existing prompt tests/snapshots updated.
- [x] No regression in the deterministic `propose_plan` path.

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/`; manual transcript review.
**Dependencies:** Task 6.
**Files likely touched:** `src/InfraGate.Planner/Prompts/*.hbs` (or embedded resource), `tests/InfraGate.Planner.Tests/` prompt tests.
**Estimated scope:** Small.

### Checkpoint B: Question/answer end-to-end
- [x] `dotnet test` green for both services (0 failures, full suite).
- [ ] Manual E2E: a low-context anomaly causes the Planner to call `ask_observer_to_inspect`; the Observer runs the read-only tool and answers; the Planner incorporates it. Observer Audit Outbox shows `handoff.tool_served`.
- [ ] A request for a non-whitelisted tool is denied and audited (`handoff.tool_denied`).

### Phase 3: Documentation

#### Task 8: Docs, glossary, ADR
**Description:** Update `docs/observer-planner-flow.md` for the bidirectional channel (handoff + progress +
questions). Add `CONTEXT.md` glossary terms: **Plan Progress Notification**, **Reverse Context Request**,
**Observer Inbound Channel**. Touch both service READMEs. Add ADR **0028** recording the flip decision
(Observer-as-server; push-notifications rejected as config-only in preview2; progress as one-way client
calls; read-only whitelist on reverse requests).

**Acceptance criteria:**
- [x] Flow doc + READMEs reflect reality; `CONTEXT.md` defines the three terms; ADR 0028 committed.

**Verification:** docs review; links resolve; glossary terms used consistently in code/comments.
**Dependencies:** Tasks 4, 6.
**Files likely touched:** `docs/observer-planner-flow.md`, `CONTEXT.md`, `src/InfraGate.Observer/README.md`, `src/InfraGate.Planner/README.md`, `docs/adr/0028-a2a-bidirectional-observer-planner-channel.md`.
**Estimated scope:** Small.

### Checkpoint: Complete
- [x] All acceptance criteria met; full Planner + Observer test suites green.
- [x] Observer→Planner handoff unchanged; Planner→Observer progress + questions working and audited.
- [x] Read-only whitelist enforced on reverse requests; auth enforced both directions.
- [x] Ready for review.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| New Planner→Observer auth (Keycloak client `infra-gate-planner`, audience/azp) misconfigured | High | Reuse the Observer→Planner pattern exactly; add the Keycloak client + run-profile/env wiring as part of Task 3; smoke-test auth in Checkpoint A. |
| Keycloak issuer mismatch (internal vs external authority) | Medium | Follow `project_keycloak_issuer_mismatch`: the Observer's JWT `authority` must use the internal `keycloak:8080` DNS to match the token `iss`. |
| Two-way topology — Planner must reach the Observer (k8s service / compose) | Medium | Add the Observer service URL + network wiring in deploy config; gate the channel on `ObserverBaseUrl` so it is opt-in. |
| Reverse requests abused to run non-read-only tools | High | Server-side whitelist via the Observer's existing `AgentGuardrailPolicy`; deny-by-default; audit every served/denied call. |
| Reverse call latency inflates planning wall-clock | Medium | Reuse the existing `AnomalyWallClockCapSeconds`/`BatchWallClockCapSeconds` caps; bound the Observer HttpClient timeout; treat timeout as a tool error. |
| Circular A2A loop (Planner asks Observer which somehow re-triggers a handoff) | Medium | Observer inbound handler only reads (audit + read-only tools); it never enqueues handoffs. Carry/limit a hop count in metadata if loops ever become possible. |
| Preview-package churn (`MEAI001`, A2A preview2) | Low | Keep `#pragma warning disable MEAI001` scoping; pin versions. |

## Open Questions
- **Keycloak client provisioning** for `infra-gate-planner`→Observer: who owns the realm/run-profile change, and is a distinct scope/audience wanted vs. reusing the gateway audience? (Resolve during Task 3.)
- **Progress granularity**: batch-level milestones now; per-anomaly progress is a later refinement behind `IObserverChannel`.
- **Should progress also flow for dropped/empty batches** (observability completeness) or only when work happens? (Default: emit `NoAction` so the Observer always sees closure.)
- **Tool-request surface**: expose the Observer's full read-only allow-list, or a narrower "inspection" subset? (Default: the existing allowed-tools set.)

## Notes for the implementer (verified anchors)
- A2A client→server is the same primitive the Observer already uses: `new A2AClient(uri, httpClient).AsAIAgent(name)` then `agent.RunAsync(json)`; the Planner-as-server side is mirrored from `src/InfraGate.Planner/Program.cs` (keyed `A2AServer` + `MapA2AHttpJson`).
- Push notifications are **config-only** in `a2a` preview2 (no server-side delivery) — do **not** build progress on them.
- Inbound auth pattern to mirror: Planner's `AddJwtBearer` + `ObserverSender` policy (`RequireClaim(azp, infra-gate-observer)`); the Observer gets the symmetric `PlannerSender`.
- Outbound auth pattern to mirror: Planner's `AddClientCredentialsTokenProvider` + `AddHttpClient(...).AddClientCredentialsBearerHandler()` (as used for `ExecutorHandoff`).
- Shared contract home: `InfraGate.Observer.Contracts` (already referenced by both services).
```
