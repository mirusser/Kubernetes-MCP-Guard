# Approval Notification via MCP Resource Subscriptions

**Status:** Planned, not started  
**Date:** 2026-05-19

## Problem

After a user approves a plan challenge in the browser, they currently must manually tell the AI agent "the plan is approved" before the agent can call `execute_approved_plan` again. The gateway records the approval but has no mechanism to relay that information back to the AI agent's MCP session. The Plan Envelope already captures `Requester.Subject` and the Challenge records `RequesterSubject`, but this identity is never used to route information back to the MCP client.

## Approach

Use the MCP resource subscription protocol — the most MCP-standard-compliant server-to-client notification path:

1. Expose a resource `plan://{planId}/status` that returns `{ status: "approved", planId: "abc123" }`
2. When `execute_approved_plan` creates a challenge, implicitly subscribe the current MCP session to that plan's resource
3. When the browser approves, send `notifications/resources/updated` to subscribed sessions
4. The AI host receives the standard notification, reads the resource, and the LLM knows to call `execute_approved_plan`

The existing `execute_approved_plan` fallback path is preserved unchanged — if the AI host doesn't support resource notifications, the user can still manually tell the agent.

## Architecture Decision Record

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | MCP `resources/subscribe` + `notifications/resources/updated` | Most MCP-standard-compliant; AI hosts that support the resource protocol handle it natively. SDK 1.3.0 exposes `McpSession.SendNotificationAsync`, `ResourcesCapability.Subscribe`, `SubscribeRequestParams`, `ResourceUpdatedNotificationParams`, `McpServerHandlers.SubscribeToResourcesHandler`. |
| 2 | Implicit subscription on challenge creation | LLM doesn't need to reason about subscriptions. When `execute_approved_plan` creates a challenge, the gateway silently subscribes the current session to `plan://{planId}/status`. |
| 3 | In-memory `ISubscriptionRegistry` | Subscriptions are ephemeral by nature — MCP connections are stateful and all sessions die on restart. Persistence noted as future extension. |
| 4 | `OnSessionInitialized` for session tracking | SDK's intended hook: `StreamableHttpServerTransport.OnSessionInitialized` fires when a new MCP session is established. |
| 5 | Separate `ApprovalNotificationDispatcher` | Clean seam; independently testable; doesn't bloat `GatewayApprovalService`. |
| 6 | Only `"approved"` fires notification in v1 | Deliberate scope. Deny/expire/cancel can be added later. |
| 7 | `GatewayToolDispatcher` binds `RequesterSubject` to session on first `request_*` call | For implicit subscription, we need to route the subscription to the right session after approval. Since the session that calls `execute_approved_plan` is the one that created the challenge, we track the session-to-subject binding at that point. |
| 8 | Auto-unsubscribe on challenge resolution AND session disconnect | On approval, the notification fires and the subscription is cleaned up. On session disconnect, all subscriptions for that session are removed. |
| 9 | Route notification to all sessions for the RequesterSubject | If Alice has two AI agent sessions running, both should be notified when her plan is approved. |

## New CONTEXT.md Terms

**Notification Registry** — The in-memory mapping from Requester subjects to active MCP sessions and from plan URIs to subscribed sessions, used to route approval notifications.
_Avoid_: Session store, connection pool

**Approval Notification** — A server-to-client MCP `notifications/resources/updated` message sent when a challenge is approved, carrying the plan URI so the client can read the updated plan status resource.
_Avoid_: Push event, callback

## New Project: `src/InfraGate.Notifications/`

```
src/InfraGate.Notifications/
├── InfraGate.Notifications.csproj    # net10.0, references ModelContextProtocol 1.3.0
├── NotificationsConventions.cs       # URI scheme "plan://", resource name/mime
├── ISubscriptionRegistry.cs          # Interface: session/subject/plan tracking
├── SubscriptionRegistry.cs           # In-memory impl (ConcurrentDictionary)
├── ApprovalNotificationDispatcher.cs # Resolves sessions, dispatches notifications
└── README.md
```

### `ISubscriptionRegistry` Interface

```csharp
public interface ISubscriptionRegistry
{
    // Called from OnSessionInitialized callback
    void RegisterSession(McpSession session);

    // Called on first authenticated tool call (request_*) to bind subject to session
    void BindSubject(string sessionId, string requesterSubject);

    // Called when execute_approved_plan creates a challenge
    void SubscribeToPlan(string sessionId, string planId);

    // Called after notification is sent (challenge resolved) or on session disconnect
    void UnsubscribeFromPlan(string sessionId, string planId);

    // Clean up all subscriptions for a disconnected session
    void RemoveSession(string sessionId);

    // Used by dispatcher to find sessions to notify
    IReadOnlyList<McpSession> GetSessionsForPlan(string planId);
}
```

### `IApprovalNotificationDispatcher` Interface

```csharp
public interface IApprovalNotificationDispatcher
{
    // Called by GatewayApprovalService after grant is created + challenge status updated
    Task NotifyPlanApprovedAsync(string planId, CancellationToken ct);
}
```

### Resource Handler

Registered in `Program.cs` via `McpServerResource.Create`:

- **URI template:** `plan://{planId}/status`
- **Read handler:** loads plan from `ApprovalStore`, resolves current status, returns `{ status: "approved"|"pending_approval"|..., planId: "..." }`
- **MIME type:** `application/json`

## Integration Points

| File | Change |
|------|--------|
| `Program.cs` | Register `ISubscriptionRegistry` (singleton), `IApprovalNotificationDispatcher`, hook `OnSessionInitialized` callback |
| `Program.cs` | Declare `ResourcesCapability { Subscribe = true }` in `ServerCapabilities` |
| `Program.cs` | Register resource handler for `plan://{planId}/status` |
| `GatewayToolDispatcher` | After challenge creation in `HandleApplyApprovedPlanAsync`, call `registry.SubscribeToPlan(sessionId, planId)` and `registry.BindSubject(sessionId, requesterSubject)` |
| `GatewayApprovalService.ApproveChallengeAsync` | After approval recorded (line ~220), call `dispatcher.NotifyPlanApprovedAsync(challenge.PlanId)` |
| `GatewayApprovalService` constructor | Accept `IApprovalNotificationDispatcher` |
| `GatewayToolDispatcher` constructor | Accept `ISubscriptionRegistry` |
| `GatewayApprovalEndpoints` | No changes — approval through browser is unchanged |

### `ApprovalNotificationDispatcher` Implementation Flow

```
NotifyPlanApprovedAsync(planId):
  1. Look up all subscribed sessions: registry.GetSessionsForPlan(planId)
  2. For each session:
     a. session.SendNotificationAsync("notifications/resources/updated",
           new ResourceUpdatedNotificationParams { Uri = $"plan://{planId}/status" })
     b. registry.UnsubscribeFromPlan(session.SessionId, planId)
```

### `GatewayApprovalService.ApproveChallengeAsync` Changes

After `challengeStore.SaveAsync(updated, ...)` (line 220), add:
```csharp
await notificationDispatcher.NotifyPlanApprovedAsync(updated.PlanId, cancellationToken);
```

## Fallback

The existing `execute_approved_plan` manual flow is completely unaffected. The user can always:

1. Approve in browser
2. Tell the AI agent "approved"
3. AI agent calls `execute_approved_plan(planId)`
4. Gateway finds the grant, gates pass, plan executes

The notification is a convenience layer — if the AI host ignores `notifications/resources/updated`, the old path still works.

## Implementation Steps

1. Create `src/InfraGate.Notifications/` project — `.csproj`, reference `ModelContextProtocol`
2. Define `NotificationsConventions.cs` — URI scheme `plan://`, resource name, MIME type
3. Implement `ISubscriptionRegistry` + `SubscriptionRegistry` — concurrent dictionaries
4. Implement `IApprovalNotificationDispatcher` + `ApprovalNotificationDispatcher`
5. Register services and resource handler in `Program.cs`
6. Wire `GatewayToolDispatcher` — subscribe on challenge creation, bind subject
7. Wire `GatewayApprovalService` — dispatch on approval
8. Add test project `tests/InfraGate.Notifications.Tests/`
9. Write unit tests: registry add/remove/subscribe/unsubscribe, dispatcher resolves sessions, notification payload shape
10. Write opt-in integration test: verify notification received by MCP client after browser approval

## Future Extensions

- **Persistent subscription storage** — survive gateway restarts (noted as deliberate skip for v1)
- **Notify on deny/expire/cancel** — fire `notifications/resources/updated` for all terminal challenge outcomes
- **Per-session notification routing** — route notification only to the session that created the plan (vs. all sessions for subject)
- **Multi-gateway replica support** — shared subscription store for multi-instance deployments
