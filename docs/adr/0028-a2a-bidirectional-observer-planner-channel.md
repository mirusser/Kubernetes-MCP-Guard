# ADR 0028: A2A Bidirectional Observer↔Planner Channel

**Status:** Superseded for progress transport by [ADR-0029](0029-planner-owned-durable-a2a-task-lifecycle.md); reverse context requests remain accepted
**Date:** 2026-05-31  
**Context:** `feat/a2a` branch — extends `8f844cb` (Observer→Planner A2A handoff)

## Context

The Observer→Planner handoff (ADR 0017, 0018) uses A2A one-way: Observer POSTs an `AnomalyHandoffBatch` and gets a 202. Two capabilities are now needed:

1. **Progress visibility**: the Observer should know when the Planner starts analyzing, proposes a plan, or fails — so the Audit Outbox has a complete trace per `CycleId`.
2. **Reverse context requests**: the Planner LLM may need current cluster state (pod events, container restart history) not present in the anomaly report. Fetching it by calling the Observer's existing read-only MCP toolset is cheaper and safer than giving the Planner direct gateway access for ad-hoc queries.

The natural transport for both is A2A — the same primitive already used in the forward direction.

## Decision Drivers

- Keep transport and auth topology uniform (A2A + JWT Bearer both ways).
- Avoid building a custom webhook push mechanism in a preview SDK where push notification delivery is not implemented server-side.
- Enforce read-only tool access server-side regardless of what the Planner requests.
- Keep progress sends fire-and-forget (a failed progress send must never abort planning).

## Options Considered

### Option A: Planner subscribes to Observer via A2A streaming / `ChannelEventNotifier`

Have the Observer push progress events over a long-lived SSE stream.

**Rejected**: `a2a` 1.0.0-preview2's `PushNotificationConfig` is config-only — there is no server-side delivery implementation. Building the push sender ourselves introduces custom infrastructure with no SDK support.

### Option B: Observer subscribes to Planner progress via `ChannelEventNotifier`

Have the Observer resubscribe after sending the handoff and receive progress events from the Planner's `ChannelEventNotifier`.

**Rejected**: buffering semantics of `ChannelEventNotifier` for late subscribers are undefined in preview2 (events published before resubscription may be dropped). A spike would be required and the approach is fragile.

### Option C (chosen): "Flip" — Observer becomes A2A server; Planner becomes A2A client to it

The Observer hosts an A2A server at `/a2a/observer`. The Planner calls it:
- For progress: `POST /a2a/observer` with `{"Intent":"progress", ...}` → Planner-initiated, no subscription race.
- For context questions: `POST /a2a/observer` with `{"Intent":"tool-request", ...}` → request/response, natural fit for A2A.

**Accepted**.

## Architecture

```
Observer                            Planner
  A2A client ─── /a2a/planner ───>  A2A server   (handoff, unchanged)
  A2A server <── /a2a/observer ───  A2A client   (progress + questions, new)
```

### Auth

- **Planner → Observer**: Planner's `ObserverRequest` HTTP client adds a `ClientCredentials` bearer token. Observer validates JWT + enforces `PlannerSender` policy (`azp == infra-gate-planner`).
- **Observer → Planner** (handoff, unchanged): Observer's `PlannerHandoff` HTTP client adds a bearer token. Planner validates + enforces `ObserverSender` policy (`azp == infra-gate-observer`).

### Intent dispatch

`ObserverInboundAgentHandler` switches on `ObserverInboundEnvelope.Intent`:

| Intent | Handler | Audit event |
|---|---|---|
| `"progress"` | Records stage in Audit Outbox | `handoff.progress` |
| `"tool-request"` | Validates against `AgentGuardrailPolicy`, calls `IAgentMcpToolset.CallToolAsync`, returns `ToolResponsePayload` | `handoff.tool_served` or `handoff.tool_denied` |

### Read-only whitelist

The Observer enforces `AgentGuardrailPolicy.AllowedToolNames` server-side on every `tool-request`. Tools not in the whitelist are denied regardless of what the Planner asks. This is defence-in-depth: the Planner's own guardrail policy already gates tool calls, but the Observer cannot trust that.

### Progress milestones

`BatchProcessor` emits progress to `IObserverChannel` at four points:

| Milestone | When |
|---|---|
| `Analyzing` | On dequeue, before any work |
| `PlanProposed` | After `proposalSink.PublishAsync` succeeds |
| `NoAction` | Empty batch or no proposals after LLM run |
| `Failed` | In `ExecuteAsync` catch block |

Progress sends use `ObserverChannel.SendProgressAsync` which swallows all exceptions internally (logs a warning). `BatchProcessor.SendProgressSafeAsync` adds a null-guard (no-op when channel is unregistered).

### Planner reverse context tool

`AskObserverTool.Create(channel, cycleId)` produces an `AIFunction` named `ask_observer_to_inspect` that is added to the agent tools list in `BatchProcessor.BuildWorkflow` when `IObserverChannel` is registered. The `DecideExecutor` agent can call it to fetch live K8s data mid-reasoning.

## Consequences

**Good:**
- A single `ObserverInboundAgentHandler` + one `MapA2AHttpJson` call handles both progress and questions (open/closed principle — new intents just add cases).
- The Observer's Audit Outbox gets a complete per-`CycleId` trace: handoff published → analyzing → plan proposed (or no-action / failed), plus any tool interactions.
- Planner LLM has access to live cluster state without direct gateway access for ad-hoc queries.

**Bad / risks:**
- Two-way topology: both services must be able to reach each other (compose network or K8s service). Mitigated by gating the channel on `ObserverBaseUrl` (opt-in).
- Reverse call latency inflates planning wall-clock. Mitigated by existing `AnomalyWallClockCapSeconds` / `BatchWallClockCapSeconds` caps and per-request cancellation.
- Circular A2A loop (Planner asks Observer which triggers another handoff) is structurally impossible because `ObserverInboundAgentHandler` only reads; it never enqueues handoffs.
- `a2a` preview2 experimental warnings (`MEAI001`) — suppressed with `#pragma warning disable` as in existing code; version is pinned.
