# Approval Notification via MCP Resource Subscriptions

**Status:** Planned, implementation-ready  
**Date:** 2026-05-19  
**Review:** SDK source verified against ModelContextProtocol .NET SDK 1.3.0

---

## Problem

After a user approves a plan challenge in the browser, they currently must manually tell the AI agent "the plan is approved" before the agent can call `execute_approved_plan` again. The gateway records the approval but has no mechanism to relay that information back to the AI agent's MCP session. The Plan Envelope already captures `Requester.Subject` and the Challenge records `RequesterSubject`, but this identity is never used to route information back to the MCP client.

## Approach

Use the MCP resource subscription protocol — the most MCP-standard-compliant server-to-client notification path:

1. Expose a resource `plan://{planId}/status` that returns `{ status: "approved", planId: "abc123" }`
2. When `execute_approved_plan` creates a challenge, implicitly subscribe the current MCP session to that plan's resource
3. When the browser approves, send `notifications/resources/updated` to subscribed sessions
4. The AI host receives the standard notification, reads the resource, and the LLM knows to call `execute_approved_plan`

The existing `execute_approved_plan` fallback path is preserved unchanged — if the AI host doesn't support resource notifications, the user can still manually tell the agent.

---

## SDK APIs — All Confirmed ✅

| API | Status |
|-----|--------|
| `McpSession.SendNotificationAsync` | ✅ three overloads |
| `ResourcesCapability.Subscribe` | ✅ bool get/set property |
| `SubscribeRequestParams` | ✅ class with `Uri` property |
| `ResourceUpdatedNotificationParams` | ✅ class with `Uri` property |
| `McpServerHandlers.SubscribeToResourcesHandler` | ✅ `McpRequestHandler<SubscribeRequestParams, EmptyResult>` |
| `StreamableHttpServerTransport.OnSessionInitialized` | ✅ exists (but NOT used — see Gap 1 below) |
| `McpSession.SessionId` | ✅ `public abstract string? SessionId { get; }` (nullable) |

---

## Gap Resolutions (SDK Source Findings)

### Gap 1 — `OnSessionInitialized` does NOT provide McpSession

**Finding:**
```csharp
// Actual signature:
public Func<InitializeRequestParams, CancellationToken, ValueTask>? OnSessionInitialized { get; init; }
```
The callback receives only `InitializeRequestParams` + `CancellationToken`. No `McpSession` is passed.
The original plan's `RegisterSession(McpSession session)` wiring via `OnSessionInitialized` is not viable.

**Resolution — use `RunSessionHandler` instead:**

`HttpServerTransportOptions.RunSessionHandler` (experimental):
```csharp
Func<HttpContext, McpServer, CancellationToken, Task>?
```
It runs before a session starts and after it completes. `McpServer` is passed (which extends `McpSession`
and has `SessionId` + `SendNotificationAsync`). The `CancellationToken` is cancelled when the
session ends — this is also the disconnect signal (resolves Gap 2).

Wired via:
```csharp
.WithHttpTransport(options =>
    options.RunSessionHandler = async (httpContext, server, ct) =>
    {
        var id = server.SessionId;
        if (id is not null)
            registry.RegisterSession(id, server);
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
        finally { if (id is not null) registry.RemoveSession(id); }
    })
```

> **Note:** `RunSessionHandler` is marked experimental in the SDK. Acceptable for v1; flag in code.

---

### Gap 2 — No explicit disconnect hook; solved by RunSessionHandler CancellationToken

**Finding:** No `OnSessionDisconnected` or equivalent exists in the SDK.
`StatefulSessionManager` manages session lifecycle internally with no exposed events.

**Resolution:** The `ct` in `RunSessionHandler` is cancelled on disconnect. The `try/finally`
pattern above handles both registration and cleanup — no TTL sweep needed as the primary path.
Retain TTL as a defensive backstop only if leak risk is high (e.g., a crash before `finally` runs).

---

### Gap 3 — RequestContext<T> session access

**Finding:**
- `RequestContext<T>` inherits `Server` (`McpServer`) from `MessageContext`
- `McpServer.SessionId` → `string?` (nullable)
- There is **no** `Session` or `SessionId` property directly on `RequestContext<T>`

**Resolution — scoped CurrentMcpSession initialized in Program.cs handler:**

```csharp
.WithCallToolHandler((RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
{
    // Initialize the scoped service before the dispatcher is called
    var ctx = request.Services!.GetRequiredService<CurrentMcpSession>();
    ctx.Initialize(request.Server.SessionId);

    var dispatcher = request.Services!.GetRequiredService<IGatewayToolDispatcher>();
    return new ValueTask<CallToolResult>(dispatcher.CallToolAsync(request.Params, ct));
})
```

`CurrentMcpSession` is a mutable scoped class. Because DI resolves the same instance within a scope,
the dispatcher's constructor-injected `ICurrentMcpSession` will see the populated value when its
methods are called (after initialization above).

Same pattern for `WithListToolsHandler` if list-tools ever needs session context.

---

### Gap 4 — WithHttpTransport options overload ✅ confirmed

**Finding:**
```csharp
public static IMcpServerBuilder WithHttpTransport(
    this IMcpServerBuilder builder,
    Action<HttpServerTransportOptions>? configureOptions = null)
```
`HttpServerTransportOptions` exposes:
- `RunSessionHandler` — `Func<HttpContext, McpServer, CancellationToken, Task>?` (used above)
- `ConfigureSessionOptions` — `Func<HttpContext, McpServerOptions, CancellationToken, Task>?`
- `SessionMigrationHandler` — `ISessionMigrationHandler?`

`OnSessionInitialized` is **not** on `HttpServerTransportOptions` — it is a property of
`StreamableHttpServerTransport` directly (not accessible via this options API).

---

### Gap 5 — McpSession.SessionId ✅ confirmed

```csharp
public abstract string? SessionId { get; }
```
Name is correct. Nullable — null when transport doesn't support multiple sessions (e.g., STDIO)
or before initialization. Guard with `if (id is not null)` everywhere.

---

## Architecture Decision Record

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | MCP `resources/subscribe` + `notifications/resources/updated` | Most MCP-standard-compliant; AI hosts that support the resource protocol handle it natively. SDK 1.3.0 exposes `McpSession.SendNotificationAsync`, `ResourcesCapability.Subscribe`, `SubscribeRequestParams`, `ResourceUpdatedNotificationParams`, `McpServerHandlers.SubscribeToResourcesHandler`. |
| 2 | Implicit subscription on challenge creation | LLM doesn't need to reason about subscriptions. When `execute_approved_plan` creates a challenge, the gateway silently subscribes the current session to `plan://{planId}/status`. |
| 3 | In-memory `ISubscriptionRegistry` | Subscriptions are ephemeral by nature — MCP connections are stateful and all sessions die on restart. Persistence noted as future extension. |
| 4 | `RunSessionHandler` for session tracking (replaces `OnSessionInitialized`) | `OnSessionInitialized` does not expose `McpSession`. `HttpServerTransportOptions.RunSessionHandler` is the correct hook: provides `McpServer` + a `CancellationToken` that cancels on disconnect. Marked experimental in the SDK — isolate to one place in `Program.cs`. |
| 5 | Separate `ApprovalNotificationDispatcher` | Clean seam; independently testable; doesn't bloat `GatewayApprovalService`. |
| 6 | Only `"approved"` fires notification in v1 | Deliberate scope. Deny/expire/cancel can be added later. |
| 7 | Scoped `CurrentMcpSession` initialized in Program.cs handler | `RequestContext<T>` has no direct `SessionId`; `request.Server.SessionId` is the source. Scoped DI guarantees the dispatcher sees the same populated instance within a request. |
| 8 | Auto-unsubscribe on challenge resolution AND session disconnect | On approval, notification fires and subscription is cleaned up. On session disconnect, `RunSessionHandler` `finally` calls `RemoveSession`, which cleans all subscriptions for that session. |
| 9 | Route notification to all sessions for the RequesterSubject | If Alice has two AI agent sessions running, both should be notified when her plan is approved. |
| 10 | `Notifications/` subfolder inside `InfraGate.McpGateway/` (no new project) | Avoids new `.csproj`, new solution entry, and unnecessary indirection. Consistent with AGENTS.md simplicity-first principle. |

---

## New CONTEXT.md Terms

**Notification Registry** — The in-memory mapping from Requester subjects to active MCP sessions and from plan URIs to subscribed sessions, used to route approval notifications.
_Avoid_: Session store, connection pool

**Approval Notification** — A server-to-client MCP `notifications/resources/updated` message sent when a challenge is approved, carrying the plan URI so the client can read the updated plan status resource.
_Avoid_: Push event, callback

---

## File Structure (inside `InfraGate.McpGateway/`)

```
src/InfraGate.McpGateway/
└── Notifications/
    ├── NotificationsConventions.cs          # URI scheme "plan://", MIME type
    ├── ICurrentMcpSession.cs                # Scoped: string? SessionId (read); + Initialize()
    ├── CurrentMcpSession.cs                 # Mutable scoped impl
    ├── ISubscriptionRegistry.cs
    ├── SubscriptionRegistry.cs              # ConcurrentDictionary: sessionId→McpServer, planId→set<sessionId>
    ├── IApprovalNotificationDispatcher.cs
    └── ApprovalNotificationDispatcher.cs
```

No new `.csproj` or solution file changes needed.

---

## Interfaces

### `ISubscriptionRegistry`

```csharp
public interface ISubscriptionRegistry
{
    // Called from RunSessionHandler (session start)
    void RegisterSession(string sessionId, McpServer server);

    // Called from RunSessionHandler finally (session end / disconnect)
    void RemoveSession(string sessionId);

    // Called from dispatcher on first request_* to bind subject to session
    void BindSubject(string sessionId, string requesterSubject);

    // Called from dispatcher when execute_approved_plan creates a challenge
    void SubscribeToPlan(string sessionId, string planId);

    // Called after notification sent (or on session disconnect via RemoveSession)
    void UnsubscribeFromPlan(string sessionId, string planId);

    // Used by dispatcher to find servers to notify
    IReadOnlyList<McpServer> GetSessionsForPlan(string planId);
}
```

### `IApprovalNotificationDispatcher`

```csharp
public interface IApprovalNotificationDispatcher
{
    // Called by GatewayApprovalService after grant is created + challenge status updated
    Task NotifyPlanApprovedAsync(string planId, CancellationToken ct);
}
```

### `ICurrentMcpSession`

```csharp
public interface ICurrentMcpSession
{
    string? SessionId { get; }
    void Initialize(string? sessionId);
}
```

---

## Resource Handler

Registered in `Program.cs` via `McpServerResource.Create`:

- **URI template:** `plan://{planId}/status`
- **Read handler:** loads plan from `ApprovalStore`, resolves current status, returns `{ status: "approved"|"pending_approval"|..., planId: "..." }`
- **MIME type:** `application/json`

---

## Integration Points

| File | Change |
|------|--------|
| `Program.cs` | `.WithHttpTransport(options => options.RunSessionHandler = ...)` for session registration + disconnect cleanup |
| `Program.cs` | Declare `ResourcesCapability { Subscribe = true }` in `McpServerOptions` |
| `Program.cs` | Register `plan://{planId}/status` resource handler |
| `Program.cs` | Register `McpServerHandlers.SubscribeToResourcesHandler` |
| `Program.cs` | `WithCallToolHandler` initializes scoped `CurrentMcpSession` from `request.Server.SessionId` before resolving dispatcher |
| `Program.cs` | Register `ISubscriptionRegistry` (singleton), `IApprovalNotificationDispatcher` (singleton), `ICurrentMcpSession`/`CurrentMcpSession` (scoped) |
| `GatewayToolDispatcher` | Accept `ISubscriptionRegistry`, `ICurrentMcpSession` in constructor; call `SubscribeToPlan` and `BindSubject` in challenge-creation path |
| `GatewayApprovalService` | Accept `IApprovalNotificationDispatcher` in constructor; call `NotifyPlanApprovedAsync` after `challengeStore.SaveAsync` |
| `CONTEXT.md` | Add "Notification Registry" and "Approval Notification" terms |

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

After `challengeStore.SaveAsync(updated, ...)` (line ~220), add:
```csharp
await notificationDispatcher.NotifyPlanApprovedAsync(updated.PlanId, cancellationToken);
```

---

## Fallback

The existing `execute_approved_plan` manual flow is completely unaffected. The user can always:

1. Approve in browser
2. Tell the AI agent "approved"
3. AI agent calls `execute_approved_plan(planId)`
4. Gateway finds the grant, gates pass, plan executes

The notification is a convenience layer — if the AI host ignores `notifications/resources/updated`, the old path still works.

---

## Implementation Steps

### Phase 1: Foundations

**Task 1 — `NotificationsConventions` + `ICurrentMcpSession`**
- Files: `Notifications/NotificationsConventions.cs`, `Notifications/ICurrentMcpSession.cs`, `Notifications/CurrentMcpSession.cs`
- Acceptance: Constants compile; `CurrentMcpSession.Initialize(string?)` sets `SessionId`; registered as scoped in a spike Program.cs call compiles
- Size: XS

**Task 2 — `ISubscriptionRegistry` + `SubscriptionRegistry`**
- Files: `Notifications/ISubscriptionRegistry.cs`, `Notifications/SubscriptionRegistry.cs`
- Acceptance: `RegisterSession` / `RemoveSession` maintain session map; `SubscribeToPlan` / `UnsubscribeFromPlan` maintain plan→sessions map; `GetSessionsForPlan` returns correct servers; thread-safe under concurrent access
- Verification: unit tests (Task 7 covers this)
- Size: S

**Task 3 — `IApprovalNotificationDispatcher` + `ApprovalNotificationDispatcher`**
- Files: `Notifications/IApprovalNotificationDispatcher.cs`, `Notifications/ApprovalNotificationDispatcher.cs`
- Acceptance: `NotifyPlanApprovedAsync` resolves sessions via registry, sends `notifications/resources/updated` with correct URI, calls `UnsubscribeFromPlan` after send; handles zero-session case without error
- Size: S

**Checkpoint A**
- [ ] `dotnet build InfraGate.slnx` passes
- [ ] No compile errors in Notifications/ types

### Phase 2: Wiring

**Task 4 — Wire `Program.cs`**
- Files: `src/InfraGate.McpGateway/Program.cs`
- Changes:
  - Register `ISubscriptionRegistry` (singleton), `IApprovalNotificationDispatcher` (singleton), `CurrentMcpSession`/`ICurrentMcpSession` (scoped)
  - `.WithHttpTransport(options => options.RunSessionHandler = ...)` for session registration + disconnect cleanup
  - Declare `ResourcesCapability { Subscribe = true }` in `McpServerOptions`
  - Register `McpServerHandlers.SubscribeToResourcesHandler`
  - Register `plan://{planId}/status` resource handler (reads plan from `ApprovalStore`, returns `{ status, planId }`)
  - `WithCallToolHandler` initializes `CurrentMcpSession` from `request.Server.SessionId` before resolving dispatcher
- Acceptance: Gateway starts; `/mcp` endpoint responds; existing tool calls work unchanged
- Size: M

**Task 5 — Wire `GatewayToolDispatcher` and `GatewayApprovalService`**
- Files: `src/InfraGate.McpGateway/GatewayToolDispatcher.cs`, `src/InfraGate.McpGateway/GatewayApprovalService.cs`
- Changes:
  - `GatewayToolDispatcher` constructor: add `ISubscriptionRegistry`, `ICurrentMcpSession`
  - In challenge-creation path of `HandleApplyApprovedPlanAsync`: call `registry.BindSubject(sessionId, requesterSubject)` and `registry.SubscribeToPlan(sessionId, planId)` (guard: `sessionId is not null`)
  - `GatewayApprovalService` constructor: add `IApprovalNotificationDispatcher`
  - After `challengeStore.SaveAsync(updated, ct)`: call `await dispatcher.NotifyPlanApprovedAsync(updated.PlanId, ct)`
- Acceptance: Challenge creation subscribes session; approval triggers notification dispatch
- Size: S

**Task 6 — Update CONTEXT.md**
- File: `CONTEXT.md`
- Add canonical terms: "Notification Registry", "Approval Notification" (as defined above)
- Size: XS

**Checkpoint B**
- [ ] `dotnet build InfraGate.slnx` passes
- [ ] `dotnet run --project src/InfraGate.McpGateway` starts without error
- [ ] Existing `dotnet test InfraGate.slnx --filter "Category!=Keycloak"` still passes (no regressions from constructor changes)

### Phase 3: Tests

**Task 7 — Unit tests: SubscriptionRegistry**
- File: `tests/InfraGate.McpGateway.Tests/Notifications/SubscriptionRegistryTests.cs`
- Cases: register/remove session; subscribe/unsubscribe plan; get sessions for plan; concurrent operations; null session ID guarded
- Size: S

**Task 8 — Unit tests: ApprovalNotificationDispatcher**
- File: `tests/InfraGate.McpGateway.Tests/Notifications/ApprovalNotificationDispatcherTests.cs`
- Cases: notification sent with correct URI; `UnsubscribeFromPlan` called after send; zero subscribers no-ops; `SendNotificationAsync` failure propagates or is logged
- Size: S

**Task 9 — Fix existing constructor tests**
- Files: any test that constructs `GatewayApprovalService` or `GatewayToolDispatcher` directly
- Acceptance: All tests compile and pass after adding mock/null for new constructor params
- Size: S

**Task 10 — Opt-in integration test (stretch)**
- Notes: Requires a purpose-built MCP test client that subscribes to `plan://{planId}/status`.
  Most real AI hosts do not subscribe to resource notifications. Flag as stretch; skip if a suitable
  test client harness is not available in the existing test suite.
- Size: L (and uncertain)

**Checkpoint C — Done**
- [ ] `dotnet test InfraGate.slnx --filter "Category!=Keycloak"` passes
- [ ] `INFRA_GATE_RUN_INTEGRATION=1 dotnet test` passes
- [ ] Manual smoke: approve a plan in browser → AI agent session receives `notifications/resources/updated`

---

## Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| `RunSessionHandler` is experimental API | Medium — may change in SDK 2.x | Isolate in one place (`Program.cs`); leave comment citing experimental status |
| `SessionId` is null in stateless or pre-init | Low — tool calls always post-init | Guard with `if (id is not null)` in all callers |
| AI hosts ignore `notifications/resources/updated` | Low — fallback path unchanged | Manual flow still works; notification is additive |
| `CurrentMcpSession.Initialize` called after dispatcher resolves | Low — same DI scope guarantees ordering | Handler always initializes before resolving dispatcher |
| Crash between `RegisterSession` and `RemoveSession` | Very Low — gateway restart clears in-memory store | Acceptable for v1; note in code |

---

## Future Extensions

- **Persistent subscription storage** — survive gateway restarts (deliberate skip for v1)
- **Notify on deny/expire/cancel** — fire `notifications/resources/updated` for all terminal challenge outcomes
- **Per-session notification routing** — route notification only to the session that created the plan (vs. all sessions for subject)
- **Multi-gateway replica support** — shared subscription store for multi-instance deployments
