# Implementation Plan: A2A Phase 1 — Streaming Execution Feedback (Task-Lifecycle Handoff)

> **⚠️ SUPERSEDED (2026-05-31) BY [`2026-05-31-a2a-bidirectional-observer-planner-channel.md`](./2026-05-31-a2a-bidirectional-observer-planner-channel.md).**
> This resubscribe-based design (Observer subscribes to the Planner) was set aside in favour of the
> **flip** (Observer becomes an A2A server; the Planner pushes progress and asks questions as a client),
> which removes the buffering spike and supports reverse questions. Kept for the investigation record
> (push-notifications-are-config-only finding; `ChannelEventNotifier` mechanics).

> Supersedes Phase 1 of [`2026-05-31-a2a-exploration-roadmap.md`](./2026-05-31-a2a-exploration-roadmap.md).
> The roadmap's original Phase 1 (Tasks 1–2) is **re-scoped** here after a feasibility study: the
> original "emit chunks as the batch moves through the `BatchProcessor` pipeline" cannot work as
> written, because the A2A handler does not drive the pipeline — it enqueues and returns. See
> **Feasibility Findings** below.

## Overview

Upgrade the fire-and-forget Observer→Planner A2A handoff into a **task-lifecycle stream**: the Planner
returns an A2A **Task** (`Working`) the moment it accepts an `Anomaly Handoff Batch`, and then publishes
status updates (`Analyzing` → `Plan Proposed`/`Completed`, or `Failed`) as its decoupled `BatchProcessor`
works. The Observer subscribes to that task by id and appends each update to its **Audit Outbox**, so the
full anomaly→plan lifecycle is traceable from the Observer side.

Crucially, this is achieved **without collapsing** the existing `AnomalyBatchQueue` + `BatchProcessor`
`BackgroundService` decoupling: the handler still returns fast. The seam between "handoff intake" and
"batch processing" — today a bare `Channel<AnomalyHandoffBatch>` with no lifecycle contract — is deepened
into a **task-identified** channel whose progress is reported through the A2A `ChannelEventNotifier`.

## Feasibility Findings (verified against the versions in this repo)

Verified against `a2a` **1.0.0-preview2** (transitive) and `Microsoft.Agents.AI*` **1.8.0 / 1.8.0-preview.260528.1**
— the exact versions referenced by `src/InfraGate.Planner` and `src/InfraGate.Observer`.

**What works out of the box:**
- The HTTP+JSON binding the Planner already maps (`MapA2AHttpJson` → A2A SDK `MapHttpA2A`) serves SSE
  (`ServerSentEvents`/`SseItem`). The transport supports streaming today.
- `A2AServer` (the raw type registered in `Program.cs`) exposes `SendStreamingMessageAsync` **and**
  `SubscribeToTaskAsync` (resubscribe). `AgentEventQueue` exposes `EnqueueMessageAsync`,
  `EnqueueStatusUpdateAsync(TaskStatusUpdateEvent)`, `EnqueueArtifactUpdateAsync`, `EnqueueTaskAsync`.
- `TaskUpdater(AgentEventQueue, taskId, contextId)` provides `SubmitAsync`/`StartWorkAsync`/`CompleteAsync`/
  `FailAsync`/`RejectAsync`.
- **`ChannelEventNotifier.Notify(string taskId, StreamResponse streamEvent)`** is a public, server-level
  **out-of-band publish API keyed by task id**, plus `AcquireTaskLockAsync(taskId)`. This is the mechanism
  that lets a decoupled producer (`BatchProcessor`) push updates to a task after the originating request
  returned.
- Client side: `A2AAgent` (the `AsAIAgent()` wrapper the Observer already uses) implements
  `RunCoreStreamingAsync` → `SendStreamingMessageAsync`, yielding an `AgentResponseUpdate` per
  `Message`/`Task`/`StatusUpdate`/`ArtifactUpdate`, and `SubscribeToTaskWithFallbackAsync` (resubscribe via
  continuation token, falling back to `GetTaskAsync` on terminal states).

**Why the roadmap's original Phase 1 is mis-scoped:**
- `PlannerHandoffAgentHandler.ExecuteAsync` deserializes the batch, calls `batchQueue.TryEnqueue(batch)`
  (an **unbounded `Channel`**), and enqueues a single `"accepted"` `Message`, then returns. It creates **no
  Task** and emits **no status updates**.
- The real planning runs later in `BatchProcessor : BackgroundService.ExecuteAsync`, fully decoupled, via
  `InProcessExecution.RunAsync` over the Filter→Dedupe→Decide→Validate→Propose workflow. By the time
  "Analyzing"/"Plan Proposed" happen, the originating A2A request and its SSE stream are closed.
- The Observer also calls the **non-streaming** `agent.RunAsync(json)` (`message:send`), so it cannot
  receive a stream regardless.

**Net:** feasible — but it requires minting a Task and reporting progress out-of-band through the notifier,
not editing the handler to "watch" the pipeline. Sizing is **S–M per task**, not the roadmap's flat "Small".

## Architecture Decisions

- **Task lifecycle, not bare messages.** The handler mints an A2A **Task** (`Submitted`→`Working`) and
  returns immediately. This keeps the `AnomalyBatchQueue` + `BackgroundService` decoupling intact while
  giving the Observer a `taskId` to follow. (Bare streamed `Message`s would require the handler to stay
  alive for the whole planning duration — re-coupling request lifetime to LLM latency. Rejected.)
- **`ChannelEventNotifier` becomes a shared seam.** Today it is `new ChannelEventNotifier()` inline inside
  the keyed `A2AServer` factory. Promote it to a DI singleton injected into both the `A2AServer` factory and
  `BatchProcessor`, so the background producer publishes to the same notifier the SSE/resubscribe endpoint
  reads from. **One adapter today (the A2AServer) → a second consumer (BatchProcessor) makes it a real seam.**
- **Coarse milestones for Phase 1.** Stream batch-level stages (`Working/Received` → `Analyzing` →
  `Completed/Plan Proposed` | `Completed/No action` | `Failed`). Per-anomaly or per-executor
  (`Decide`/`Validate`/`Propose`) granularity via `Microsoft.Agents.AI.Workflows` streaming events is
  **deferred** — it is a later refinement behind the same seam.
- **Observer is the trace sink.** Each `AgentResponseUpdate` becomes an `ObserverAuditEntry`
  (`handoff.progress`), reusing the existing Audit Outbox. Terminal `handoff.published`/`handoff.failed`
  events are preserved.
- **Resilience over completeness.** A dropped/timed-out stream must **not** fail the observation cycle or
  the remediation: the proposal still lands through the existing `IRemediationProposalSink`. Streaming is a
  trace enrichment, not the critical path.

### Deepening opportunity (architecture framing)

- **Module:** the *handoff-intake → batch-processing* seam.
- **Today:** `AnomalyBatchQueue` is a bare `Channel<AnomalyHandoffBatch>`. Its interface carries the payload
  but **no lifecycle identity and no progress contract** — "where is this batch in its lifecycle?" is
  invisible. **Deletion test:** delete the queue and the decoupling (backpressure, BackgroundService
  resilience) complexity reappears across the handler and processor → it earns its keep, so we *deepen* it
  rather than remove it.
- **After:** the channel carries a **task identity** (`taskId`/`contextId`), and `BatchProcessor` reports
  lifecycle transitions through the `ChannelEventNotifier` adapter. **Leverage:** the Observer gets a
  live trace; later roadmap phases (Executor-as-agent, capability negotiation, reverse context requests)
  reuse the same task machinery. **Locality:** "what stage is this batch at" is concentrated in the task,
  not scattered as implicit log lines.

## Dependency Graph

```
Task 0 (Spike: notifier delivery semantics)   ← resolves held-open vs. resubscribe + buffering
    │
    └── Task 1 (DI singleton notifier + task identity on the queue)   [Planner foundation]
            │
            ├── Task 2 (Handler mints a Task)            [Planner]
            │       │
            │       └── Task 3 (BatchProcessor publishes status updates)   [Planner producer]
            │               │
            │   == Checkpoint A: Planner ==
            │               │
            │               └── Task 4 (Observer consumes stream → Audit Outbox)   [Observer consumer]
            │                       │
            │           == Checkpoint B: Observer↔Planner E2E ==
            │                       │
            └────────────────────── Task 5 (docs + CONTEXT.md glossary)   [Polish]
```

## Task List

### Phase 1a: Spike & Foundation

#### Task 0: Spike — confirm `ChannelEventNotifier` out-of-band delivery semantics
**Description:** Throwaway probe (no production code) to pin down the one remaining unknown: when
`ChannelEventNotifier.Notify(taskId, statusUpdate)` is called **after** the originating handler returned, is
the event delivered to a subsequent `A2AServer.SubscribeToTaskAsync(taskId)` consumer? And are events
published **before** a subscriber attaches **buffered or dropped**? Resolve two design choices: (a) held-open
single stream vs. send-then-resubscribe; (b) whether `BatchProcessor` must also `ITaskStore.SaveTaskAsync`
the terminal state so a late/reconnecting Observer can fall back to `GetTaskAsync` (the client's
`SubscribeToTaskWithFallbackAsync` relies on this).

**Acceptance criteria:**
- [ ] A runnable harness demonstrates a `Notify(...)` issued after the initiating call returned is received by a `SubscribeToTaskAsync(taskId)` consumer (or proves it is not).
- [ ] Buffering/replay behavior for pre-subscription events is documented (buffered | dropped | requires lock via `AcquireTaskLockAsync`).
- [ ] A 4–6 line "Decision" note is appended to this plan (held-open vs. resubscribe; task-store fallback yes/no), and Tasks 2–4 are adjusted if the semantics differ from the assumed resubscribe model.

**Verification:** harness output pasted into the Decision note. No production code merged.
**Dependencies:** None.
**Files likely touched:** `/tmp` probe only (delete after); append Decision note to this plan.
**Estimated scope:** Small.

#### Task 1: Promote `ChannelEventNotifier` to a singleton and carry task identity on the queue
**Description:** Register `ChannelEventNotifier` as a DI singleton in `Program.cs`; inject the same instance
into the keyed `A2AServer` factory (replacing the inline `new ChannelEventNotifier()`) and into
`BatchProcessor`. Change `AnomalyBatchQueue` to carry a `QueuedAnomalyBatch(AnomalyHandoffBatch Batch,
string TaskId, string ContextId)` (new internal record) instead of a bare `AnomalyHandoffBatch`. Pure
plumbing — no behavior change yet (handler still returns "accepted").

**Acceptance criteria:**
- [ ] `ChannelEventNotifier` is a singleton resolved by both the `A2AServer` factory and `BatchProcessor`.
- [ ] `AnomalyBatchQueue` enqueues/dequeues `QueuedAnomalyBatch` (taskId + contextId + batch).
- [ ] Build is clean; existing Planner tests pass unchanged (behavior preserved).

**Verification:** `dotnet build` + `dotnet test tests/InfraGate.Planner.Tests/`.
**Dependencies:** Task 0.
**Files likely touched:** `src/InfraGate.Planner/Program.cs`, `src/InfraGate.Planner/Cycle/AnomalyBatchQueue.cs`, `src/InfraGate.Planner/Cycle/BatchProcessor.cs` (ctor), `src/InfraGate.Planner/Handoff/PlannerHandoffAgentHandler.cs` (enqueue shape).
**Estimated scope:** Small.

#### Task 2: Handler mints an A2A Task instead of a bare "accepted" message
**Description:** In `PlannerHandoffAgentHandler.ExecuteAsync`, derive `taskId`/`contextId`
(`context.TaskId`/`context.ContextId` or freshly generated), and use `TaskUpdater(eventQueue, taskId,
contextId)` to emit `SubmitAsync()` then `StartWorkAsync(message: "Received")` so the initial A2A response is
a **Task** in `Working` (the client then has a `taskId`/continuation token). Enqueue the `QueuedAnomalyBatch`
carrying that `taskId`. On backpressure (`TryEnqueue` returns false), emit a terminal `FailAsync`/`RejectAsync`
instead. Keep the existing `auditOutbox` "received" entry.

**Acceptance criteria:**
- [ ] On accept, the handler returns a Task in `Working` with a stable `taskId`, and enqueues that `taskId` with the batch.
- [ ] On backpressure, the handler emits a terminal `Failed`/`Rejected` status (no silent drop).
- [ ] `PlannerHandoffAgentHandlerTests` updated to assert the Task/status events (replacing the "accepted" message assertion).

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/UnitTests/PlannerHandoffAgentHandlerTests.cs`.
**Dependencies:** Task 1.
**Files likely touched:** `src/InfraGate.Planner/Handoff/PlannerHandoffAgentHandler.cs`, `tests/InfraGate.Planner.Tests/UnitTests/PlannerHandoffAgentHandlerTests.cs`.
**Estimated scope:** Small–Medium.

#### Task 3: `BatchProcessor` publishes lifecycle status updates via the notifier
**Description:** Inject `ChannelEventNotifier` into `BatchProcessor`. In `ProcessBatchAsync` (now receiving a
`QueuedAnomalyBatch`), publish coarse milestones via `notifier.Notify(taskId, new StreamResponse {
StatusUpdate = new TaskStatusUpdateEvent { TaskId = taskId, ContextId = contextId, Status = new
AgentTaskStatus { State = …, Message = … }, Final = … } })`:
on dequeue → `Working`/"Analyzing"; after `proposalSink.PublishAsync` → `Completed`/"Plan Proposed (n)";
empty/no-op batch → `Completed`/"No action"; in the `catch` → `Failed`. Per the Task 0 Decision, also
`ITaskStore.SaveTaskAsync` the terminal state if resubscribe-fallback requires it. (Exact member names —
`AgentTaskStatus`, `TaskStatusUpdateEvent.Final` — confirmed against the SDK during Task 0/here.)

**Acceptance criteria:**
- [ ] A processed batch publishes the ordered sequence `Analyzing` → (`Plan Proposed` | `No action`) for that `taskId`.
- [ ] A batch whose processing throws publishes a terminal `Failed`.
- [ ] A unit test injects a fake/real notifier and asserts the published sequence for both success and failure paths.
- [ ] The remediation path (`proposalSink.PublishAsync`) is unchanged and still runs.

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/` (new `BatchProcessor` status-update test).
**Dependencies:** Task 2.
**Files likely touched:** `src/InfraGate.Planner/Cycle/BatchProcessor.cs`, `src/InfraGate.Planner/PlannerConventions.cs` (status text constants), `tests/InfraGate.Planner.Tests/UnitTests/BatchProcessor*Tests.cs`.
**Estimated scope:** Medium.

### Checkpoint A: Planner emits a task lifecycle
- [ ] `dotnet build` clean; `dotnet test tests/InfraGate.Planner.Tests/` green.
- [ ] Manual: POST a handoff batch to the Planner's A2A endpoint with a streaming client and observe `Working` → `Analyzing` → terminal updates for one `taskId`.

### Phase 1b: Observer Consumption

#### Task 4: Observer consumes the stream and writes progress to the Audit Outbox
**Description:** Change `A2AAnomalyHandoffSink.PublishAsync` from `agent.RunAsync(json)` to the streaming
model chosen in Task 0 — either `await foreach (var u in agent.RunStreamingAsync(json, …))` (held-open) or
send-then-`RunStreamingAsync(session, options: new(){ ContinuationToken = … })` (resubscribe). For each
`AgentResponseUpdate`, append an `ObserverAuditEntry` with a new `ObserverAuditEvents.HandoffProgress =
"handoff.progress"` (carrying stage text + `CycleId` + `taskId`). Preserve the terminal
`handoff.published`/`handoff.failed` entries and the existing failure counter. Guard the stream with a
bounded `HttpClient`/cancellation timeout and treat a dropped/incomplete stream as a non-fatal
warning (the cycle and the proposal must still succeed).

**Acceptance criteria:**
- [ ] Observer streams Planner updates and appends one `handoff.progress` audit entry per update.
- [ ] A mid-stream disconnect/timeout logs a warning and does **not** throw out of the observation cycle.
- [ ] `A2AAnomalyHandoffSinkTests` updated to assert progress entries are written from a faked streaming agent; the empty-batch short-circuit is preserved.

**Verification:** `dotnet test tests/InfraGate.Observer.Tests/UnitTests/A2AAnomalyHandoffSinkTests.cs`.
**Dependencies:** Task 3 (server must emit updates), Task 0 (consumption pattern).
**Files likely touched:** `src/InfraGate.Observer/Handoff/A2AAnomalyHandoffSink.cs`, `src/InfraGate.Observer/Audit/ObserverAuditEvents.cs`, `src/InfraGate.Observer/Program.cs` (A2A client timeout, if changed), `tests/InfraGate.Observer.Tests/UnitTests/A2AAnomalyHandoffSinkTests.cs`.
**Estimated scope:** Medium.

### Checkpoint B: Observer↔Planner end-to-end
- [ ] `dotnet test tests/InfraGate.Observer.Tests/` green.
- [ ] Manual E2E (both services up): a discovered anomaly produces, in the **Observer Audit Outbox**, an ordered trace `handoff.published`(or progress: Received) → `handoff.progress: Analyzing` → `handoff.progress: Plan Proposed` for the matching `CycleId`/`taskId`.
- [ ] Killing the Planner mid-plan leaves the Observer cycle healthy (degraded trace, no exception).

### Phase 1c: Documentation

#### Task 5: Update flow docs and the canonical glossary
**Description:** Update `docs/observer-planner-flow.md` to describe the task-lifecycle handoff (Task minted on
accept; status updates via `ChannelEventNotifier`; Observer trace). Add glossary terms to `CONTEXT.md`:
**Handoff Task** (the A2A Task representing one accepted `Anomaly Handoff Batch`) and **Plan Lifecycle
Stream** (the ordered status updates the Observer records). Touch the Planner/Observer READMEs where they
describe the handoff. Consider a short ADR (next id **0028**) recording "A2A task-lifecycle handoff over
fire-and-forget message; decoupling preserved via out-of-band `ChannelEventNotifier`".

**Acceptance criteria:**
- [ ] `docs/observer-planner-flow.md` reflects the new lifecycle.
- [ ] `CONTEXT.md` defines **Handoff Task** and **Plan Lifecycle Stream**.
- [ ] (Optional) ADR `docs/adr/0028-*.md` records the decision.

**Verification:** docs review; `grep` confirms glossary terms resolve; links valid.
**Dependencies:** Tasks 2–4 (describe what was built).
**Files likely touched:** `docs/observer-planner-flow.md`, `CONTEXT.md`, `src/InfraGate.Planner/README.md`, `src/InfraGate.Observer/README.md`, optionally `docs/adr/0028-*.md`.
**Estimated scope:** Small.

### Checkpoint: Phase 1 complete
- [ ] All acceptance criteria met; full `dotnet test` for Planner + Observer green.
- [ ] Observer Audit Outbox shows the live anomaly→plan lifecycle trace.
- [ ] Decoupling preserved: handler returns fast; `BatchProcessor` remains the execution engine; a slow/absent Observer never blocks planning.
- [ ] Ready for review.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Notifier drops events published before the Observer subscribes (late-subscriber race) | High | Resolved by **Task 0**; mitigate with `ITaskStore.SaveTaskAsync` terminal state + client `GetTaskAsync` fallback, and have the Observer resubscribe using the `taskId` from the initial response. |
| Streaming/SSE timeout during long LLM planning | Medium | Bounded `HttpClient`/cancellation timeout on the A2A client; treat stream end as non-fatal; rely on terminal `GetTaskAsync` fallback for the final state. |
| Re-coupling request lifetime to planning latency | Medium | Task model keeps the handler fast-returning; **do not** adopt held-open streaming unless Task 0 proves it is required and acceptable. |
| `taskId` ↔ `CycleId` correlation lost in the Audit Outbox | Medium | Carry both on `QueuedAnomalyBatch` and stamp both on every `handoff.progress` entry. |
| Preview-package churn (`MEAI001`, A2A preview2 API shape) | Low | Keep the existing `#pragma warning disable MEAI001` scoping; pin versions; member names (`AgentTaskStatus`, `TaskStatusUpdateEvent.Final`) confirmed in Task 0. |
| Observer handoff now spends time streaming, delaying the next cycle | Low | If measured as a problem, make the Observer-side consume fire-and-forget (background) — listed as an Open Question, not in scope. |

## Open Questions
- **Held-open stream vs. resubscribe** — decided by Task 0. (Resubscribe is assumed and preferred for the decoupled design.)
- **Per-executor granularity** — should `Decide`/`Validate`/`Propose` emit per-stage updates via `Microsoft.Agents.AI.Workflows` streaming events? Deferred; coarse milestones first.
- **Observer consumption placement** — keep streaming inline in `PublishAsync`, or move to a background consumer so it never delays the observation cycle? Decide if Checkpoint B shows cycle delay.
- **Correlation key** — is `taskId == CycleId` acceptable, or keep them distinct with an explicit map? (Distinct is safer; CycleId is domain, taskId is transport.)

## Notes for the implementer (verified API anchors)
- Server publish (out-of-band): `ChannelEventNotifier.Notify(string taskId, StreamResponse streamEvent)`; lock via `AcquireTaskLockAsync(taskId)`.
- In-handler task events: `new TaskUpdater(eventQueue, taskId, contextId)` → `SubmitAsync` / `StartWorkAsync(message)` / `CompleteAsync(message)` / `FailAsync(message)`; or `AgentEventQueue.EnqueueStatusUpdateAsync(TaskStatusUpdateEvent)` / `EnqueueTaskAsync(AgentTask)`.
- `StreamResponse` carries one of `Message` / `Task` / `StatusUpdate` (`TaskStatusUpdateEvent`) / `ArtifactUpdate`.
- Client consume: `AIAgent.RunStreamingAsync(string, …)` → `AgentResponseUpdate` (`RawRepresentation` is the `TaskStatusUpdateEvent`/`Message`/`AgentTask`); resubscribe via `AgentRunOptions.ContinuationToken`; server endpoint `A2AServer.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = taskId })`.
- DI today: `Program.cs` registers `AddKeyedSingleton<A2AServer>(PlannerConventions.A2AHandoffAgentName, …)` with `new A2AServer(handler, new InMemoryTaskStore(), new ChannelEventNotifier(), logger)`; endpoint `MapA2AHttpJson(A2AHandoffAgentName, A2AHandoffEndpointPath)` (`/a2a/planner`). Promote the notifier (and likely the task store) to singletons in Task 1.
```
