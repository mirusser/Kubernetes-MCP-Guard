# Plan: Concurrent Apply — TOCTOU Mitigation

## Overview

`ApprovalStore` has no synchronization — the `GetApprovedPlanAsync` → `ApplyPlanAsync` → `MarkAppliedAsync` chain suffers from a TOCTOU race. Two concurrent `apply_approved_plan(planId)` calls can both pass the `File.Exists(appliedPath)` check in `GetApprovedPlanAsync` (line 70-73), both apply side effects to Kubernetes, and both race to write the applied file. Mitigation: add an atomic check-then-create gate in `MarkAppliedAsync` using a `SemaphoreSlim`, matching the existing pattern in `ApprovalChallengeStore` (line 11: `storeLock = new(1, 1)`).

## Architecture Decisions

- **Gate in `MarkAppliedAsync`, not in `GetApprovedPlanAsync`.** The check-before-create atomicity must be at the write point, because `GetApprovedPlanAsync` is called separately from the apply and gap exists between them. Two callers can pass `GetApprovedPlanAsync`, but only the first through `MarkAppliedAsync` succeeds; the second finds the applied file already present under the lock and returns a refusal.
- **No lock scoped to the full apply flow.** Locking the entire `ApplyApprovedPlanAsync` (including the Kubernetes API call) would serialize all applies across different plans unnecessarily. The lock should only protect the applied-file write for a given plan.
- **Per-plan locking via concurrent dictionary.** A single `SemaphoreSlim` would serialize ALL applies (even for different plans). Instead, use a `ConcurrentDictionary<string, SemaphoreSlim>` keyed by `planId` to scope locks to individual plans. This matches best practices for granular locking.
- **Test at both levels.** Unit test for the store-level atomicity (`ApprovalStoreTests`), E2E test for concurrent gateway-path applies (`Safety.E2E.Tests/Workflows`).

## Task List

### Phase 1: Store-level fix

#### Task 1: Add atomic check-then-create to `ApprovalStore.MarkAppliedAsync`

**Description:** Add a `private readonly ConcurrentDictionary<string, SemaphoreSlim> applyLocks` field to `ApprovalStore`. In `MarkAppliedAsync`, acquire a per-plan lock, check if the applied file already exists, and if not, write the file and audit event. If the file already exists under the lock, return `false` to indicate the plan was already applied by another caller. The caller (`K8sManager.Apply.cs`) checks the return value and returns a refusal message.

**Acceptance criteria:**
- [ ] `MarkAppliedAsync` returns `Task<bool>` (true = first write, false = already applied)
- [ ] Per-plan lock acquired via `ConcurrentDictionary.GetOrAdd` pattern
- [ ] Lock is released in a `finally` block (not left in the dictionary if taken by a single caller)
- [ ] If `File.Exists(appliedPath)` under lock, returns `false` without writing
- [ ] If file does not exist, writes file + audit event, returns `true`
- [ ] Existing calls to `MarkAppliedAsync` in `K8sManager.Apply.cs` check the return value and return a refusal when false

**Verification:**
- [ ] `dotnet build InfraGate.slnx` — clean
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj` — all tests pass

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Approvals/ApprovalStore.cs` (+`applyLocks`, modify `MarkAppliedAsync`)
- `src/InfraGate.McpServer/K8sManager.Apply.cs` (handle `false` return from `MarkAppliedAsync`)

**Estimated scope:** Small (2 files)

---

### Phase 2: Unit test

#### Task 2: Add unit test for concurrent `MarkAppliedAsync` calls

**Description:** Add a test to `tests/InfraGate.McpServer.Tests/UnitTests/ApprovalStoreTests.cs` that spawns 5 parallel `Task.Run` calls to `MarkAppliedAsync` for the same plan. Assert that exactly one returns `true` and the other four return `false`. Use a `CountdownEvent` or `Task.WhenAll` barrier to ensure all tasks launch simultaneously.

**Acceptance criteria:**
- [ ] 5 concurrent tasks call `MarkAppliedAsync(plan, hash, ct)` for the same plan
- [ ] Exactly 1 returns `true`; 4 return `false`
- [ ] Applied file exists on disk with correct content
- [ ] Test name follows `Method_State_ExpectedResult`: `MarkAppliedAsync_ConcurrentCalls_OnlyOneSucceeds`

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/ --filter "FullyQualifiedName~Concurrent"` — green
- [ ] Full server test suite passes

**Dependencies:** Task 1

**Files likely touched:**
- `tests/InfraGate.McpServer.Tests/UnitTests/ApprovalStoreTests.cs`

**Estimated scope:** XS (1 file)

---

### Phase 3: E2E test

#### Task 3: Add E2E test for concurrent `apply_approved_plan` through the gateway

**Description:** Add `ConcurrentPlanApplyTests.cs` to `tests/InfraGate.Safety.E2E.Tests/Workflows/`. The test creates a plan, approves it through the browser, then fires 5 parallel `Task.Run` calls via HTTP MCP clients calling `apply_approved_plan` with the same `planId`. Asserts exactly one call contains `Applied plan:`; the other four contain `Refused:` with `already applied`. The test uses a `CountdownEvent` to synchronize launch and `Task.WhenAll` to wait for completion.

**Acceptance criteria:**
- [ ] Plan is created and approved via browser endpoint (full gateway flow)
- [ ] 5 concurrent `apply_approved_plan` calls are launched simultaneously
- [ ] Exactly 1 response contains `"Applied plan:"`
- [ ] The other 4 responses contain `"Refused:"` with `"already applied"`
- [ ] Audit log shows exactly one `plan_applied` event and four `apply_denied` events
- [ ] Test decorated with `[Trait("Category", "SafetyE2E")]` and `[Collection(SafetyE2ECollection.Name)]`

**Verification:**
- [ ] `INFRA_GATE_RUN_SAFETY_E2E=1 dotnet test tests/InfraGate.Safety.E2E.Tests/ --filter "FullyQualifiedName~ConcurrentPlanApply"` — green
- [ ] `dotnet test tests/InfraGate.Safety.E2E.Tests/ --filter "Category=SafetyE2E"` — 16 tests pass (15 existing + 1 new)

**Dependencies:** Task 1 (store fix needed for concurrent safety)

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/Workflows/ConcurrentPlanApplyTests.cs` (new)

**Estimated scope:** Small (1 new file)

---

### Phase 4: Documentation

#### Task 4: Update README with concurrent-safety notes

**Description:** Add `ConcurrentPlanApplyTests.cs` to the "what it covers" table. Add a note in "Test architecture" about per-plan locking in `MarkAppliedAsync` preventing TOCTOU races.

**Acceptance criteria:**
- [ ] README table includes new test file
- [ ] Architecture section mentions concurrent-safety fix

**Verification:**
- [ ] `git diff --check` clean

**Dependencies:** Task 3

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/README.md`

**Estimated scope:** XS (1 file)

---

### Checkpoint: Complete

- [ ] `dotnet build InfraGate.slnx` — 0 warnings, 0 errors
- [ ] `dotnet test InfraGate.slnx --no-build --filter "Category!=Keycloak&Category!=SafetyE2E"` — all unit tests pass
- [ ] `INFRA_GATE_RUN_SAFETY_E2E=1 dotnet test tests/InfraGate.Safety.E2E.Tests/ --filter "Category=SafetyE2E"` — 16 tests pass

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| `ConcurrentDictionary` memory leak (per-plan semaphores never removed) | Low | Each lock is acquired, used briefly for file-I/O, then released. The number of unique planIds in a test run is bounded. In production, planIDs are transient — after a plan is applied, no further calls use its key. |
| E2E concurrent test flaky due to timing (not all 5 tasks launch simultaneously) | Med | Use `CountdownEvent(5)` as a barrier: each task signals the event before proceeding, and the event is waited before any task proceeds to the HTTP call. This ensures all 5 reach the gateway simultaneously. |
| `Task.WhenAll` with 5 concurrent HTTP calls overloads the TestServer | Low | TestServer handles concurrent requests natively. 5 concurrent requests is within normal limits. |
| `K8sManager.Apply.cs` already calls `MarkAppliedAsync` — changing return type to `Task<bool>` is a breaking ABI change | Low | `MarkAppliedAsync` is only called from `K8sManager.Apply.cs` (line 36). The server test project references the same assembly; no other consumers exist. |
