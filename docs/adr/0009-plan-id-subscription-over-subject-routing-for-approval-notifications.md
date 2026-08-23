# ADR-0009: Explicit Plan-Status Resource Subscription for Approval Notifications

**Date:** 2026-05-19
**Amended:** 2026-08-09
**Status:** Accepted

---

## Context

When a human approves a plan challenge in the browser, the gateway can notify an MCP client that the plan status changed. The notification payload is `notifications/resources/updated` for the plan-status resource URI:

```text
plan://{planId}/status
```

The routing question is how the gateway identifies which MCP subscription stream should receive that notification.

Three strategies were considered:

- Subject-based routing: map an OAuth subject to all active sessions for that subject.
- Implicit plan-id subscription: subscribe the session that calls `execute_approved_plan` and receives an approval URL.
- Explicit resource subscription: require the client to include `plan://{planId}/status` in an MCP `subscriptions/listen` request.

The original ADR accepted implicit plan-id subscription. After testing against Codex and the MCP resource subscription model, the gateway now uses explicit MCP resource subscription for protocol correctness and keeps a read-only wait tool for clients that do not surface resource notifications.

## Decision

The gateway exposes a plan-status resource template:

```text
plan://{planId}/status
```

Clients can read this resource to receive JSON with the same fields as `get_plan_status`:

```json
{"planId":"...","status":"ApprovalRequired"}
```

Clients that want push-style approval updates must explicitly include that URI in `subscriptions/listen.notifications.resourceSubscriptions`. `PlanStatusSubscriptionsListenHandler` acknowledges the supported URI, extracts its plan id, and registers the held-open response stream in `ISubscriptionRegistry` with `SubscribeToPlan(registrationId, planId)`.

When browser approval issues an Approval Grant, `GatewayApprovalService` calls `IApprovalNotificationDispatcher.NotifyPlanApprovedAsync(planId, ct)`. The dispatcher sends `notifications/resources/updated` tagged with `_meta.subscriptionId` to streams subscribed to that plan id and then removes each plan subscription.

`execute_approved_plan` no longer creates an implicit subscription when it returns `ApprovalRequired`.

## Rationale

- MCP resource update notifications are tied to resource subscriptions. Requiring `subscriptions/listen` matches MCP 2026-07-28 instead of relying on side effects from a tool call.
- The resource URI carries the plan id, so the internal registry can stay plan-id based without adding subject routing.
- Explicit subscription avoids notifying unrelated sessions for the same OAuth subject.
- Clients that do not support or display MCP resource notifications still have a deterministic fallback: call `get_plan_status` in a loop or call `wait_for_plan_approval`.

## Consequences

- A listen stream receives approval notifications only after subscribing to `plan://{planId}/status`.
- If the waiting client disconnects before browser approval, `RemoveSubscriber` clears its plan subscriptions and the notification is dropped.
- `wait_for_plan_approval(planId, timeoutSeconds)` is intentionally read-only. It returns status JSON with `timedOut` and never applies a plan.
- Clients such as Codex may still need polling or the wait tool if they do not surface background MCP resource notifications in chat.

## Alternatives Considered

**Subject-based routing**
Rejected for v1 because it adds subject-to-session indexes, lifetime management across session churn, and a risk of notifying unrelated sessions for the same user.

**Implicit plan-id subscription at `execute_approved_plan` time**
This was the original accepted design. It works for hosts that never open `subscriptions/listen`, but it makes a tool call mutate notification routing outside the resource subscription protocol. It has been replaced by explicit resource subscriptions plus the wait fallback.

**Client-native notification display or hooks**
This is outside the gateway. A client could display `notifications/resources/updated` directly, maintain a background subscription listener, or offer a hook that reacts to background MCP notifications. Until that exists in a client, `get_plan_status` and `wait_for_plan_approval` remain the supported fallback paths.
