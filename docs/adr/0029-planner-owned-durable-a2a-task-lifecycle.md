# 29. Planner-Owned Durable A2A Task Lifecycle

Date: 2026-06-01

## Status

Accepted

## Context

The autonomous remediation path had three open-loop in-memory dedupe layers and two bespoke handoff shapes:

- Observer sent anomaly batches to Planner over A2A but did not receive a durable work handle.
- Planner pushed `RemediationProposalBatch` to Executor over an HTTP `202 Accepted` endpoint.
- Planner emitted fire-and-forget progress callbacks to Observer.

Those callbacks reported activity but did not answer the load-bearing question: is this anomaly already being handled
after a Planner restart? The approval plan remains the source of truth for the remediation decision, but the system
also needs one durable unit of work for the agent lifecycle.

The A2A protocol and SDK provide the required primitives: `contextId`, `TaskState`, artifacts, `ITaskStore`,
`TaskUpdater`, and synchronous `SendMessage`.

## Decision

### 1. Planner owns one durable A2A task per anomaly

Observer sends one `AnomalyReport` per A2A message with `contextId = AnomalyId`. Planner creates one task for that
context atomically before queueing work. A duplicate handoff returns a no-op acknowledgement and does not enqueue a
second remediation attempt.

Planner persists tasks in PostgreSQL when its audit connection string is configured. The in-memory task store remains
the local fallback. The task is the authoritative work-in-flight idempotency layer; the existing Observer, Planner,
and Executor dedupe stores remain as defense-in-depth.

### 2. Task state describes remediation progress

Planner drives the task lifecycle:

| Domain state | A2A `TaskState` |
| --- | --- |
| received | `Submitted` |
| planning | `Working` |
| plan proposed | `Working` plus `planId` artifact |
| waiting for approval | `AuthRequired` |
| no action required | `Completed` |
| execution outcome | `Completed`, `Failed`, or `Rejected` |

The task artifact is a `planId` reference. It does not duplicate the approval-core `PlanEnvelope`.

### 3. Planner dispatches synchronously to Executor over A2A

Planner sends the `planId` to `/a2a/executor` with a synchronous A2A message after persisting `AuthRequired`.
Executor keeps its approval-watch role: it calls `wait_for_plan_approval`, applies only an approved plan through
`execute_approved_plan`, and returns an applied, failed, or rejected outcome message. The Executor watch timeout is
one hour; Planner's HTTP client timeout is longer than that bound.

### 4. Planner reconciles waiting tasks after restart

At startup Planner lists persisted `AuthRequired` tasks with artifacts, checks `get_plan_status` first, and:

- completes tasks whose plans are already applied;
- fails tasks whose plans are expired or missing;
- re-dispatches plans that remain approval-required or approved.

Gateway execution is idempotent, so re-dispatch after a lost connection does not bypass single-execution enforcement.

### 5. Observer progress callbacks are retired

Planner task state replaces one-way progress messages. Observer keeps `/a2a/observer` only for reverse-context
`tool-request` calls. Its in-memory anomaly dedupe remains responsible only for flapping suppression and resolution
emission.

## Consequences

- Remediation work-in-flight idempotency survives Planner restarts when PostgreSQL is configured.
- The approval plan stays authoritative for the decision; Planner tasks stay authoritative for agent work tracking.
- Planner-to-Executor transport has one request/response contract instead of HTTP queue handoff plus callbacks.
- Existing dedupe stores remain useful secondary guards.
- A persistent anomaly gets one attempt in v1. Re-attempt policy after a terminal task remains deferred.

## Supersedes

- ADR-0017's HTTP Planner-to-Executor handoff transport.
- ADR-0028's fire-and-forget progress callback path. ADR-0028 reverse-context requests remain active.

## References

- `.agents/Plans/Roadmap/2026-06-01-a2a-task-lifecycle-roadmap.md`
- A2A specification clone: `~/OtherRepos/a2a/A2A`
- A2A .NET SDK clone: `~/OtherRepos/a2a/a2a-dotnet`
- Microsoft Agent Framework clone: `~/OtherRepos/agent-framework`
