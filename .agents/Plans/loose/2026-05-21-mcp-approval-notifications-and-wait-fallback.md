# MCP Approval Notifications and Wait Fallback Plan

## Summary

Implement the protocol-correct notification path first, then add a Codex-friendly wait fallback.

- Option 1: expose approval plan status as an MCP resource and send `notifications/resources/updated` only to sessions that explicitly subscribe to `plan://{planId}/status`.
- Option 2: add a read-only `wait_for_plan_approval` tool that blocks briefly and returns the current plan status without applying the plan.
- Option 3, for future reference: Codex needs native MCP resource notification display or hook support; current Codex hooks are lifecycle/tool hooks, not background MCP notification listeners.

## References

- MCP schema: <https://modelcontextprotocol.io/specification/2025-11-25/schema>
- .NET MCP resources: <https://csharp.sdk.modelcontextprotocol.io/concepts/resources/resources.html>
- .NET stateful mode: <https://csharp.sdk.modelcontextprotocol.io/concepts/stateless/stateless.html>
- Codex MCP: <https://developers.openai.com/codex/mcp>
- Codex hooks: <https://developers.openai.com/codex/hooks>

## Key Changes

### Option 1: Protocol-Correct Resource Notifications

Add a plan status resource in `InfraGate.McpGateway.Notifications`.

- Resource template: `plan://{planId}/status`
- Reads return JSON matching `get_plan_status`: `{"planId":"...","status":"..."}`
- Unknown safe plan IDs return a `NotFound` status JSON payload.
- Malformed or unsupported resource URIs are rejected with `McpException`.

Wire the MCP resource handlers in `Program.cs`.

- Run the gateway in stateful mode.
- Add `WithListResourceTemplatesHandler`.
- Add `WithReadResourceHandler`.
- Add `WithSubscribeToResourcesHandler`.
- Add `WithUnsubscribeFromResourcesHandler`.
- Parse `plan://{planId}/status` and call the existing plan-id based `ISubscriptionRegistry`.

Make notification routing protocol-correct.

- Remove the implicit subscription created when `execute_approved_plan` returns `ApprovalRequired`.
- Keep `ISubscriptionRegistry` plan-id based internally.
- Keep `ApprovalNotificationDispatcher` sending `notifications/resources/updated`.

### Option 2: Codex-Friendly Wait Tool

Add a read-only `wait_for_plan_approval` tool.

- Required argument: `planId`
- Optional argument: `timeoutSeconds`
- Default timeout: 55 seconds
- Bounds: 1 to 300 seconds
- Poll interval: 250 milliseconds
- Returns `planId`, `status`, and `timedOut`
- Stops immediately for `Approved`, `Applied`, `Expired`, and `NotFound`
- Returns `ApprovalRequired` with `timedOut: true` when the plan remains pending through the timeout
- Never mutates or applies a plan

Add a shared formatter for plan status JSON.

- Reuse existing constants for `planId` and `status`.
- Add `timedOut` under `McpGatewayConventions.ToolResponseFields`.
- Move enum-to-wire status mapping out of `GatewayToolDispatcher` so the resource and wait tool share the same contract.

### Option 3: Future Client Support

Keep a short future note in docs that Codex-side support would require one of these client capabilities:

- Displaying server-to-client MCP notifications in chat.
- Running a long-lived MCP resource subscription listener.
- Offering a hook that can react to background MCP notifications, not only local lifecycle/tool events.

Do not implement this in the repo.

## Implementation Tasks

1. Plan-status JSON foundation
   - Add shared plan-status response rendering.
   - Verify `get_plan_status` still emits the same JSON fields and status values.

2. MCP plan-status resource
   - Add the resource handler.
   - Add URI parsing and validation.
   - Add resource template listing.
   - Verify read behavior for approved, applied, expired, pending, unknown, and malformed IDs.

3. Explicit resource subscriptions
   - Wire subscribe and unsubscribe handlers.
   - Remove implicit subscription in `execute_approved_plan`.
   - Verify dispatcher only notifies explicitly subscribed sessions.

4. Wait fallback tool
   - Add tool metadata and argument parsing.
   - Implement bounded polling without mutation.
   - Verify immediate terminal statuses, timeout, and approval during wait.

5. Docs and ADRs
   - Update gateway README/tool docs to include the plan resource and wait tool.
   - Mention that resource notifications are best-effort and require client subscription/stateful transport support.
   - Add only a brief future-reference note for Codex-native notification support.

## Test Plan

Use vertical TDD slices.

1. Add one focused test for the existing `get_plan_status` JSON contract before refactoring.
2. Add `PlanStatusResourceHandlerTests` for resource read behavior.
3. Add subscribe/unsubscribe behavior tests around the real gateway resource handlers where practical.
4. Add `GatewayToolDispatcherTests` coverage for `wait_for_plan_approval`.
5. Add or update an integration test with a real .NET MCP client and TestServer if the existing test harness can do this without Docker, Keycloak, or a live Kubernetes cluster.

Specific wait scenarios:

- Unknown plan returns `NotFound` immediately.
- Pending plan times out with `timedOut: true`.
- Pending plan that becomes approved during the wait returns `Approved`.
- Applied and expired plans return immediately.

Verification commands:

```bash
dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj
git diff --check
```

## Assumptions

- `wait_for_plan_approval` defaults to 55 seconds so it fits Codex's typical 60 second MCP tool timeout.
- Longer waits require the client to increase its MCP tool timeout, for example Codex `tool_timeout_sec`.
- MCP notifications are best-effort from the gateway's perspective. If the client does not keep a stateful streamable HTTP session or does not surface `notifications/resources/updated`, the gateway cannot force a chat push.
- Option 3 is a Codex client feature request and is intentionally out of scope for this repository implementation.
