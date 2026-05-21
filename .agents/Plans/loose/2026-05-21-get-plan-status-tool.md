# Implementation Plan: `get_plan_status` Tool

## Overview

After calling `execute_approved_plan` and receiving an `ApprovalRequired` response, Claude has no way to detect when the user approves the plan in the browser — it must wait for a manual "approved, go ahead" message. The server already sends a `notifications/resources/updated` MCP notification on approval, but Claude Code (the MCP client) has no hook to re-invoke Claude while it is waiting for user input — the notification arrives and is silently dropped.

The pragmatic fix is a read-only `get_plan_status` MCP tool. Claude can call it in a polling loop (via `/loop`), detect when status transitions to `Approved`, and then call `execute_approved_plan` automatically without any manual relay from the user.

## Architecture Decisions

- **Read-only, no side effects.** Unlike `execute_approved_plan`, this tool must never create a challenge, update state, or subscribe the session. It only reads.
- **New `GetPlanStatusAsync` on `ApprovalStore`.** Add a dedicated read path that checks the pending-file, grant-file, and applied-file in order. This keeps the status logic colocated with storage layout rather than scattered across the dispatcher.
- **Tool definition in `GatewayToolDispatcher`.** Follows the exact same pattern as `CreateApplyApprovedPlanTool()` — a private factory method, a const tool name in `McpGatewayConventions.ToolNames`, and dispatch in `CallToolAsync`.
- **Tool description drives Claude's polling behavior.** The description must explicitly say: call this in a loop after `execute_approved_plan` returns `ApprovalRequired`; call `execute_approved_plan` once status is `Approved`.

---

## Task List

### Phase 1: Storage read path

- [ ] **Task 1 — `ApprovalStore.GetPlanStatusAsync`** (S: 1–2 files)

  Add a new public method to `src/InfraGate.Approvals/ApprovalStore.cs`:

  ```csharp
  public async Task<PlanStatusResult> GetPlanStatusAsync(string planId, CancellationToken cancellationToken)
  ```

  Logic (in order):
  1. Check if the applied-file exists (`{root}/applied/{planId}.json`) → `Applied`
  2. Check if a grant-file exists (`GetGrantPath(planId)`) — reuse existing `GetGrantAsync`:
     - Grant exists and not expired → `Approved`
     - Grant exists and expired → `Expired`
  3. Check if the pending-file exists (`{root}/pending/{planId}.json`) → `ApprovalRequired`
  4. Otherwise → `NotFound`

  Add a new value type in the same file (or a companion file if it grows beyond a few lines):

  ```csharp
  internal sealed record PlanStatusResult(PlanStatus Status, string? ApprovalUrl = null);

  internal enum PlanStatus { NotFound, ApprovalRequired, Approved, Applied, Expired }
  ```

  > **Note:** `PlanStatus` shares its name with the notification system's
  > `NotificationsConventions.Resources.PlanStatusUri()` — this is intentional, not coincidental.
  > The notification URI scheme (`plan://{planId}/status`) identifies the plan-status resource at the
  > MCP protocol layer; the `PlanStatus` enum defines the domain values that resource carries.
  > They live in separate projects (`InfraGate.McpGateway.Notifications` vs `InfraGate.Approvals`)
  > because the gateway owns protocol concerns while the approvals project owns domain types.

  Add a constant for the status enum string values in `ApprovalConventions` (or a new `PlanStatusConventions` nested class) — no raw string literals in the tool response.

  **Acceptance criteria:**
  - [ ] Returns `Applied` when the applied-file exists, regardless of grant state.
  - [ ] Returns `Approved` when a valid (non-expired) grant-file exists and no applied-file exists.
  - [ ] Returns `Expired` when a grant-file exists but `ExpiresAtUtc <= UtcNow`.
  - [ ] Returns `ApprovalRequired` when a pending-file exists with no grant.
  - [ ] Returns `NotFound` when no file exists for the planId.

  **Files:**
  - `src/InfraGate.Approvals/ApprovalStore.cs`
  - `src/InfraGate.Approvals/PlanStatusResult.cs` (if record needs its own file)

  **Verification:** Unit tests in Task 3.

---

### Phase 2: Tool wiring

- [ ] **Task 2 — Add tool name constant + tool definition + dispatch** (S: 1 file)

  **`src/InfraGate.McpGateway/McpGatewayConventions.cs`** — inside `ToolNames`:

  ```csharp
  public const string GetPlanStatus = "get_plan_status";
  ```

  **`src/InfraGate.McpGateway/GatewayToolDispatcher.cs`**:

  1. Add `HandleGetPlanStatusAsync(CallToolRequestParams, CancellationToken)` — mirrors `HandleApplyApprovedPlanAsync` structure:
     - Extract `planId` from arguments (same null/whitespace guard as the existing method).
     - Call `approvalStore.GetPlanStatusAsync(planId, ct)`.
     - Return JSON: `{ "planId": "...", "status": "..." }` — use `System.Text.Json.JsonSerializer.Serialize`.
     - Status string values must come from the convention constants, not inline literals.

  2. In `CallToolAsync`, add a branch between the `execute_approved_plan` check and the `RequestToolPrefix` check:

     ```csharp
     if (toolName.Equals(McpGatewayConventions.ToolNames.GetPlanStatus, StringComparison.Ordinal))
         return await HandleGetPlanStatusAsync(request, ct).ConfigureAwait(false);
     ```

  3. Add `CreateGetPlanStatusTool()` factory (same style as `CreateApplyApprovedPlanTool`):

     ```csharp
     private static Tool CreateGetPlanStatusTool() => new Tool
     {
         Name = McpGatewayConventions.ToolNames.GetPlanStatus,
          Description = "Returns the current status of a pending approval plan (NotFound | ApprovalRequired | Approved | Applied | Expired). " +
                        "Call this in a polling loop after execute_approved_plan returns ApprovalRequired. " +
                        "When status is Approved, call execute_approved_plan to apply the plan. " +
                        "When status is Expired, call execute_approved_plan to create a new approval challenge.",
         InputSchema = JsonSerializer.SerializeToElement(new
         {
             type = "object",
             properties = new
             {
                 planId = new { type = "string", description = "PlanId returned by one of the request_* tools." }
             },
             required = (string[])["planId"]
         })
     };
     ```

  4. Add `tools.Add(CreateGetPlanStatusTool());` in `ListToolsAsync` immediately after `CreateApplyApprovedPlanTool`.

  **Acceptance criteria:**
  - [ ] `get_plan_status` appears in the tool list.
  - [ ] Returns `{ "planId": "...", "status": "NotFound" }` for an unknown planId.
  - [ ] Returns `{ "planId": "...", "status": "ApprovalRequired" }` for a pending plan without a grant.
  - [ ] Returns `{ "planId": "...", "status": "Approved" }` after browser approval, before execution.
  - [ ] Returns `{ "planId": "...", "status": "Applied" }` after `execute_approved_plan` succeeds.
  - [ ] Missing/blank `planId` returns an error result (same guard as `execute_approved_plan`).

  **Files:**
  - `src/InfraGate.McpGateway/McpGatewayConventions.cs`
  - `src/InfraGate.McpGateway/GatewayToolDispatcher.cs`

---

### Checkpoint: After Tasks 1–2

- [ ] `dotnet build` clean (no warnings, no analyzer suppressions added)
- [ ] `get_plan_status` appears in `tools/list` response
- [ ] Manual smoke: create a plan, poll status, approve, poll again — correct transitions

---

### Phase 3: Tests

- [ ] **Task 3 — Unit tests for `ApprovalStore.GetPlanStatusAsync`** (S: 1 file)

  **File:** `tests/InfraGate.McpGateway.Tests/UnitTests/ApprovalStoreGetPlanStatusTests.cs`
  (or extend the existing `ApprovalStore` test file if one exists)

  Check `InternalsVisibleTo` in `src/InfraGate.Approvals/InfraGate.Approvals.csproj` — add if missing:
  ```xml
  <InternalsVisibleTo Include="InfraGate.McpGateway.Tests" />
  ```

  Test cases (naming: `GetPlanStatusAsync_State_ExpectedResult`):
  - `GetPlanStatusAsync_NoFiles_ReturnsNotFound`
  - `GetPlanStatusAsync_PendingFileOnly_ReturnsApprovalRequired`
  - `GetPlanStatusAsync_ValidGrant_ReturnsApproved`
  - `GetPlanStatusAsync_ExpiredGrant_ReturnsExpired`
  - `GetPlanStatusAsync_AppliedFile_ReturnsApplied`
  - `GetPlanStatusAsync_AppliedFileAndGrant_ReturnsApplied` (applied wins over grant)

  Use temp directories (no external dependencies — unit test, not integration).

- [ ] **Task 4 — Unit tests for `HandleGetPlanStatusAsync` dispatch** (S: 1 file)

  **File:** `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayToolDispatcherGetPlanStatusTests.cs`
  (or extend `DownstreamMcpClientTests.cs` if that file already covers the dispatcher)

  Test cases:
  - `CallToolAsync_GetPlanStatus_MissingPlanId_ReturnsError`
  - `CallToolAsync_GetPlanStatus_UnknownPlan_ReturnsNotFoundJson`
  - `CallToolAsync_GetPlanStatus_ApprovalRequired_ReturnsStatusJson`
  - `CallToolAsync_GetPlanStatus_Approved_ReturnsStatusJson`
  - `CallToolAsync_GetPlanStatus_Applied_ReturnsStatusJson`

  Assert on the JSON `status` field value using convention constants, not string literals.

- [ ] **Task 5 — Update `src/InfraGate.McpGateway/README.md`** (XS)

  Add `get_plan_status` to the tool table alongside `execute_approved_plan`. Use `verify-readme-docs` workflow: grep for tool names in README, patch only the stale row, preserve existing style.

  **Verification:**
  ```bash
  rg 'get_plan_status\|execute_approved_plan' src/InfraGate.McpGateway/README.md
  ```

---

### Checkpoint: Complete

- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/` — all pass
- [ ] `dotnet test` — full suite green
- [ ] `get_plan_status` in README tool table
- [ ] No new analyzer suppressions or `NoWarn` entries

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `GetGrantAsync` already reads from disk; `GetPlanStatusAsync` adds another file read per poll | Low — polling interval is human-paced | Acceptable; no caching needed at this scale |
| `PlanStatus` enum values become part of the tool contract — renaming breaks Claude's polling logic | Medium | Define string values as constants in `ApprovalConventions` or a dedicated convention class; never serialize the enum name directly |
| Applied-file check before grant-file check is important — an applied plan may still have a grant file | Low | Test `AppliedFileAndGrant_ReturnsApplied` covers this |

## Open Questions

- None — scope is clear and self-contained.
