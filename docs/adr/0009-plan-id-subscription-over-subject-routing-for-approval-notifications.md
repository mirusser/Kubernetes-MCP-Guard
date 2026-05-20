# ADR-0009: Plan-ID Subscription Over Subject Routing for Approval Notifications

**Date:** 2026-05-19  
**Status:** Accepted

---

## Context

When a human approves a plan challenge in the browser, the gateway sends a
`notifications/resources/updated` MCP message to the AI agent session that is waiting for that
approval. The question is: how does the gateway identify which session(s) to notify?

Two routing strategies were considered:

**Subject-based routing**: The gateway records the requester's OAuth subject at plan-creation time
and, on approval, fans out to all active sessions whose bound subject matches the requester. This
requires mapping `sessionId → subject` and `subject → set<sessionId>`.

**Plan-ID subscription**: The session that calls `execute_approved_plan` and receives a pending
challenge subscribes itself to that specific plan ID at call time. On approval, the gateway fans out
to all sessions subscribed to that plan ID. No subject mapping is required.

---

## Decision

**v1 uses plan-ID subscription.**

When `execute_approved_plan` is called and the pre-execution gate returns a pending challenge (not a
hard refusal), `GatewayToolDispatcher` calls `ISubscriptionRegistry.SubscribeToPlan(sessionId, planId)`.
On approval, `GatewayApprovalService` calls `IApprovalNotificationDispatcher.NotifyPlanApprovedAsync(planId, ct)`,
which fans out to all sessions subscribed to that plan ID and unsubscribes each after the send.

`ISubscriptionRegistry.BindSubject(sessionId, requesterSubject)` exists on the interface as a reserved
extension point but is a **no-op in v1**. It is called from the mutation-request path so the call site
is in place; the implementation simply discards the argument.

---

## Rationale

- **Correctness for the common case**: The session that is waiting for approval is the session that
  called `execute_approved_plan`. Subscribing that session to the plan ID at call time routes the
  notification exactly where it needs to go without any subject inference.

- **Simpler state**: No subject→session index is needed. A single `planId → set<sessionId>` map
  suffices.

- **No cross-session leakage**: Subject-based routing would notify all sessions for a user, including
  unrelated background sessions that happened to share the same OAuth subject. Plan-ID subscription
  scopes the notification to the session that explicitly requested it.

- **Automatic cleanup**: The subscription is removed immediately after the notification is sent, so
  no stale entries accumulate. Session disconnect (`RemoveSession`) also clears all plan subscriptions
  for that session.

---

## Consequences

- `BindSubject` is intentionally a no-op. Future implementors should not assume it has an effect.
  If subject-based routing becomes necessary (e.g., to notify the original requester when the
  `execute_approved_plan` call came from a different session), `BindSubject` is the named extension
  point. Its interface position and call site are already wired; only the implementation needs to
  change.

- A session that never calls `execute_approved_plan` will never receive an approval notification for
  that plan, even if the same user is authenticated in that session. This is acceptable because the
  manual fallback (the user telling the agent "the plan is approved") remains unchanged.

- If the waiting session disconnects before the approval arrives, `RemoveSession` clears its plan
  subscriptions. The notification is silently dropped. The agent must re-subscribe on reconnect by
  calling `execute_approved_plan` again.

---

## Alternatives Considered

**Subject-based routing (original ADR #9 intent)**  
Would notify all sessions for the requester's subject. Adds complexity (subject→session index,
lifetime management across session churn) and risks over-notification. Deferred to a future ADR if
a concrete need arises.

**Explicit subscription via MCP `resources/subscribe` handler**  
The MCP protocol supports client-initiated `resources/subscribe` calls. In practice, most AI hosts
do not send these proactively. Using implicit subscription at `execute_approved_plan` call time
achieves the same result without relying on client cooperation.
