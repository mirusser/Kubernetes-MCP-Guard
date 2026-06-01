# Roadmap: Remediation Idempotency via a Planner-Owned A2A Task

> **Supersedes** [`2026-05-31-a2a-exploration-roadmap.md`](./2026-05-31-a2a-exploration-roadmap.md).
> - Phase 1 (streaming) + Phase 3 (reverse tool-requests) already shipped via the bidirectional flip (`feat/a2a`).
> - Phase 2 (capability negotiation) **dropped** — A2A Agent Cards are inert advertisement, not negotiation.
> - Phase 4 (stateful Executor) **absorbed**: the durable *task* lives on the **Planner**; the **Executor keeps its
>   approval-watch + execute + report** role (its whole purpose).
>
> Verified against the protocol spec (`~/OtherRepos/a2a/A2A`), the protocol SDK (`~/OtherRepos/a2a/a2a-dotnet`,
> `v1.0.0-preview2`), and the Microsoft Agent Framework (`~/OtherRepos/agent-framework`, `Microsoft.Agents.AI* 1.8.0`).

## What this is really about (and what it is not)

Adopting the A2A Task model is **conceptually a no-op** for the domain: the remediation lifecycle is unchanged.
Renaming `CycleId`→`contextId`, bespoke states→Task states, HTTP handoff→`SendMessage` is just re-expressing what
exists in standard conventions — *not* a redesign.

The **one thing that genuinely improves** is **remediation idempotency**. Today "is this anomaly already being handled?"
is answered three separate times by three open-loop, in-memory heuristics with no shared truth (see Appendix). This
roadmap adds **one Planner-owned, durable Task per anomaly** as the *authoritative* source of truth for work-in-flight,
**keeping the existing dedupe stores as defense-in-depth** beneath it. Everything else is mechanical plumbing.

## Decided architecture

- **Approval plan = source of truth; Task = unit of work.** The concrete approval plan (`planId` from `propose_plan`,
  persisted in the existing challenge/grant approval core) is authoritative for the *decision*. The **Planner-owned
  Task** is the agent's "unit of work" associated with it, carrying the plan as an A2A **artifact** (a `planId`
  reference, not a competing copy). One Task per anomaly, `contextId = AnomalyId` (stable 12-char hash).
- **Roles:**
  - **Observer** *notifies* the Planner of an anomaly (quick handoff → Planner returns a task handle; the Observer does
    not block on the remediation).
  - **Planner** owns the Task + its lifecycle, runs the LLM, calls `propose_plan`, dispatches to the Executor, **and then
    just waits** for the result.
  - **Executor** *watches whether the plan was approved or rejected, executes accordingly, and returns the result.* This
    is its purpose — it is **not** stateless.
- **Planner→Executor is one synchronous call — no asynchronicity either way.** After submitting the plan the Planner's
  task sits in **`waiting`** and blocks on a synchronous `SendMessage` to the Executor; the Executor watches approval +
  applies and **returns the outcome as the response `Message`** (no Executor-side task tracking, no callbacks).
  **Timeout = 1 hour** (`WatchTimeoutSeconds = 3600`, current max); the Planner's A2A client timeout matches. Approval
  not granted within the hour → task `failed` (timeout).
- **Task lifecycle (domain → A2A `TaskState`):**

  | Domain state | A2A `TaskState` | Meaning |
  |---|---|---|
  | received | `Submitted` | anomaly handed off; task created |
  | planning | `Working` | LLM analysis |
  | submitted | `Working` (+ plan artifact) | `propose_plan` done; plan attached |
  | unremediable | `Completed` (no-action) *(terminal)* | nothing actionable |
  | waiting | `AuthRequired` | awaiting approval (durable; survives restart) |
  | completed / failed / rejected | `Completed` / `Failed` / `Rejected` | terminal |

  (**A2A `TaskState` names are canonical**; the domain terms are glosses carried in `status.message`/metadata. The
  Planner drives `AuthRequired → terminal` itself from the Executor's returned result — a valid descriptive use of
  `AuthRequired`, not the client-message resume pattern.)
- **Durable `ITaskStore` lives on the Planner.** A `PostgresTaskStore` (on the existing `AuditOutbox.Postgres` infra)
  lets the Planner reconcile `waiting` tasks after a restart. The **Executor needs no durable store** (the plan is the
  durable truth; a crashed synchronous call → the Planner re-dispatches; apply is idempotent at the gateway).
- **Idempotency = layered, 1 attempt:**
  - **Authoritative/primary:** the durable Planner Task — **one per `contextId`**, enforced on handoff (duplicate
    handoff = no-op ack).
  - **Defense-in-depth (retained):** `PlannerDedupeStore` (in-memory, fast) and `ExecutorDedupeStore` (double-apply
    guard) stay as secondary layers.
  - **Observer:** keeps **only its flapping debounce** (`DedupeSuppressionWindow` / resolution-absence).
  - **One execution attempt** per anomaly (cooldown + max-attempts not built). **Deferred (known v1 limitation):**
    re-attempting an anomaly that persists/recurs after a terminal Task — a persistent symptom stays visible in Observer reports.

### Lifecycle mapping (who drives each transition)

```
Observer ── notify(anomaly, contextId=AnomalyId) ─▶ PLANNER creates Task (idempotent: 1 per contextId)
  Planner: received(Submitted) ─▶ planning(Working)
  Planner: propose_plan ⇒ submitted(Working + plan artifact)   |   or ─▶ unremediable(Completed) [terminal]
  Planner: ─▶ waiting(AuthRequired)  and dispatch(planId) SYNCHRONOUSLY (≤ 1h) ─▶ EXECUTOR watches approval ─▶ apply ─▶ result
  Planner: ◀── result Message ── ─▶ completed | failed | rejected
```

## Capability use (verified)

`SendMessage` (notify + synchronous dispatch carrying the result), `AuthRequired` (the `waiting` state), `GetTask`/
`ListTasks` (task-state observability + Planner reconciliation), `TaskState`, artifacts (plan reference), `contextId`
grouping, durable `ITaskStore` on the Planner. Not used: streaming (optional later), push notifications (no SDK sender),
Agent Cards (inert).

## Phasing & dependency graph

```
A  spike — Planner-owned task through A2AAgent; background-task updates; synchronous Executor dispatch returning the result (go/no-go)
│
B  contextId = AnomalyId; Planner mints + persists the Task on handoff (authoritative 1-per-contextId) + PostgresTaskStore
│
C  Planner task lifecycle (received→planning→submitted/unremediable→waiting→terminal) + plan-as-artifact
│
D  Executor → A2A server; synchronous Planner→Executor dispatch (watch approval + execute + return result)
│
E  Observer cleanup — flapping debounce only; optional GetTask observability
│
F  (optional) streaming progress + cancellation of superseded tasks
```

> Defense-in-depth note: `PlannerDedupeStore` and `ExecutorDedupeStore` are **retained** throughout — the durable Task
> is layered *above* them as the authoritative check, not a replacement.

## Task list

### Phase A — Spike (de-risk; gates everything)
- [ ] **A1:** Prove a Planner-owned Task end-to-end through the *experimental* `A2AAgent`/`IAgentHandler` paths. Validate
  the two mechanical unknowns: (1) the **background-task pattern** — the handoff handler returns a task handle quickly,
  then the `BatchProcessor` drives `Working → waiting(AuthRequired) → terminal` on that task *after* the request returns
  (via the task store + notifier, not the original request's event queue); (2) the **synchronous Planner→Executor**
  call returns the outcome and tolerates the 1-hour window.
  - **Scope:** Medium. **Output:** findings appended here + go/no-go.

### Checkpoint A — findings documented; human go/no-go.

### Phase B — `contextId` + durable Task minted on handoff
- [x] **B1:** Thread `contextId = AnomalyId`; the Planner **creates + persists the Task synchronously on handoff** (one
  per `contextId` — duplicate handoff = no-op ack), then works async.
  - **Files:** `src/InfraGate.Observer/Handoff/*`, `src/InfraGate.Planner/Handoff/PlannerHandoffAgentHandler.cs`, `InfraGate.Observer.Contracts`.
- [x] **B2:** `PostgresTaskStore : ITaskStore` (`Get/Save/Delete/ListTasks`) on `AuditOutbox.Postgres`; swap into the
  Planner's keyed `new A2AServer(handler, PostgresTaskStore, ChannelEventNotifier, …)`; re-key audits on `contextId`/`taskId`.
  - **Files:** new `src/InfraGate.Planner/Tasks/PostgresTaskStore.cs` (+ migration), `src/InfraGate.Planner/Program.cs`.
  - **Note:** `PlannerDedupeStore` stays (defense-in-depth) — the Task check is layered above it.
  - **Scope:** Large.

### Phase C — Planner task lifecycle + plan-as-artifact
- [x] **C1:** Drive the Task via `TaskUpdater`: `StartWorkAsync` (planning) → `AddArtifactAsync(planId)` (submitted) →
  `RequireAuthAsync` (waiting) → `CompleteAsync`/`FailAsync`/`RejectAsync` from the Executor result; `Completed`
  (no-action) for unremediable. Retire one-way `IObserverChannel.SendProgressAsync` (progress = task states); keep the
  reverse `tool-request` path.
  - **Files:** `src/InfraGate.Planner/Cycle/BatchProcessor.cs`, `Cycle/Workflow/{ProposeExecutor,DecideExecutor}.cs`, `Handoff/ObserverChannel.cs`.
  - **Scope:** Large.

### Phase D — Executor as A2A server (watch + execute + report), synchronous
- [x] **D1:** Executor becomes an A2A server; the Planner dispatches the approved `planId` via a **synchronous**
  `SendMessage` (replaces `HttpRemediationProposalSink` + the 202-accept handoff). The Executor keeps its
  **watch-for-approval** (`wait_for_plan_approval`, 1h) and **apply** (`apply_approved_plan`) logic and **returns the
  outcome** as the response. `ExecutorDedupeStore` **retained** (double-apply defense-in-depth).
  - **Files:** `src/InfraGate.Executor/Program.cs`, `Watch/*` (kept; handoff endpoint replaced), new `ExecutorAgentHandler.cs`; `src/InfraGate.Planner/Handoff/HttpRemediationProposalSink.cs` → A2A dispatch.
  - **Note:** set Executor `WatchTimeoutSeconds = 3600`; Planner A2A client timeout ≥ 1h.
  - **Scope:** Large.

### Checkpoint D — one anomaly flows end-to-end as a Planner-owned Task (artifact = plan reference); task sits in
`waiting` while the Executor watches approval + applies + returns the outcome synchronously; a Planner restart
reconciles `waiting` tasks from Postgres; both dedupe stores still present as defense-in-depth.

### Phase E — Observer cleanup
- [x] **E1:** Observer keeps only its flapping debounce; optional `GetTask(contextId)` for closed-loop observability.
  - **Files:** `src/InfraGate.Observer/Cycle/Workflow/CycleAggregateExecutor.cs`.
  - **Scope:** Small.

### Phase F — (optional) streaming + cancellation
- [ ] Live task tracking (subscribe) and cancel superseded/stale tasks.

### Checkpoint: Complete — durable Planner-owned Task is the authoritative idempotency layer (dedupe stores retained
beneath it); flow doc + `CONTEXT.md` glossary updated; ADR recorded.

## Risks and mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `A2AAgent` task paths are experimental (stale "messages only" remark); background-task update pattern unproven | High | **Phase A gates everything**; fall back to raw `a2a-dotnet` client if the wrapper misbehaves. |
| 1-hour synchronous Planner→Executor call — dropped connections / Planner restart mid-wait | Med-High | Bound at 1h; Planner A2A client timeout matches; on loss the Planner reconciles from its durable `waiting` Task + re-dispatches (apply idempotent). Approvals routinely > 1h → revisit with async task-tracking (Phase F). |
| Planner becomes stateful/durable | Med | Reuse `AuditOutbox.Postgres` patterns; restart-reconcile + concurrent-handoff (idempotency) tests. |
| 1-attempt simplification leaves persistent/recurring anomalies un-retried | Med | Accepted for v1; symptom stays visible in Observer reports; revisit later (deferred). |

## Decisions (resolved) & deferred
- **Terminal-state mapping (resolved):** A2A `TaskState` names are canonical — `unremediable → Completed`(no-action),
  human-reject → `Rejected`, apply-error / approval-timeout → `Failed`.
- **Restart reconciliation (resolved):** on Planner restart, for `AuthRequired` (waiting) tasks, check plan status via
  the approval core first, then re-dispatch only if still pending.
- **One execution attempt (resolved):** one attempt to execute the plan per anomaly; cooldown/max-attempts not built.
- **Recurrence after resolution (deferred):** whether a resolved-then-recurring anomaly earns a fresh attempt — later.

## Notes for the implementer (verified SDK anchors)
- **Server (a2a-dotnet):** `IAgentHandler.ExecuteAsync(RequestContext, AgentEventQueue, ct)`;
  `TaskUpdater.{StartWorkAsync, AddArtifactAsync, RequireAuthAsync, CompleteAsync, FailAsync, RejectAsync}`;
  `AgentEventQueue.{EnqueueTaskAsync, EnqueueStatusUpdateAsync, EnqueueArtifactUpdateAsync}`;
  `new A2AServer(IAgentHandler, ITaskStore, ChannelEventNotifier, ILogger)` (Planner already registers this keyed).
- **Durability:** implement `ITaskStore.{GetTaskAsync, SaveTaskAsync, DeleteTaskAsync, ListTasksAsync}` on the **Planner**
  — only `InMemoryTaskStore` ships; `ListTasksAsync` filters by contextId/status (reconciliation + observability).
- **Client (agent-framework):** `new A2AClient(uri, http).AsAIAgent(name)`; `RunAsync` returns a `Message`/`Task`.
  Configure the Executor-dispatch `HttpClient` timeout ≥ 1h.
- **Defense-in-depth (kept):** `PlannerDedupeStore` (1h TTL, anomalyId), `ExecutorDedupeStore` (planId, double-apply).
- **Gaps:** no push-notification sender; `A2AAgent` task support is preview — validate in Phase A.

## Appendix — current dedup layers (now defense-in-depth beneath the durable Task)

| Layer | Key | Mechanism | Role going forward |
|---|---|---|---|
| Observer `AnomalyDedupeStore` | `(Kind,target)` | cycle throttle (suppress `DedupeSuppressionWindow`(5) cycles; `Resolved` after absent 2) | kept — flapping debounce |
| Planner `PlannerDedupeStore` | `AnomalyId` | in-memory, `ActivePlanTtl = 1h` | kept — fast secondary guard |
| Executor `ExecutorDedupeStore` | `planId` | in-memory `TryTrack` | kept — double-apply guard |
| **Planner Task (new)** | **`contextId`** | **durable, one per anomaly, reconciled on restart** | **authoritative/primary** |

The new durable Task closes the gap the three in-memory layers couldn't (cross-service, restart-surviving, state-aware);
the three remain as cheap secondary defenses.

## Verified versions
- `a2a-dotnet`: `v1.0.0-preview2` (clone at `v1.0.0-preview2-26-g455b7af`).
- Microsoft Agent Framework: `Microsoft.Agents.AI*` `1.8.0`, A2A `1.0.0-preview2`.
