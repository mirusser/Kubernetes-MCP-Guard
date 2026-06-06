# Implementation Plan: F-02 — ResourceVersion-Based TOCTOU Prevention

**Source:** Security Audit F-02 — TOCTOU via Stale Dry-Run / force-conflicts  
**Date:** 2026-06-05  
**Status:** ⚠️ PARTIALLY MITIGATED → target: ✅ FULLY MITIGATED

## Overview

The plan execution pipeline suffers from a TOCTOU window: plan creation captures a normalized snapshot of live Kubernetes state (with `resourceVersion`, `uid`, `generation`, etc. stripped), and at execution time, drift detection re-reads live state and compares normalized JSON. If the normalized fields haven't changed, the plan proceeds — even if the object was modified (e.g., a round-trip write that doesn't change normalized fields).

The fix adds Kubernetes' standard optimistic concurrency pattern by capturing `resourceVersion` at plan creation time and asserting it hasn't changed at execution time, closing the TOCTOU window with a server-enforced atomic check.

## Architecture Decisions

- **A1. Store `resourceVersion` per-diff, not globally.** Each `KubernetesPlanDiff` already references one `KubernetesObjectRef`. The `resourceVersion` is specific to that object's live state at diff time and lives naturally alongside `LiveObjectJson`. A global `Dictionary<string,string>` on `KubernetesPlanPayload` would add mapping indirection and require key format conventions.
  
- **A2. Add a new `FreshnessCheck` type, not inline logic.** The existing `FreshnessPolicy` + `FreshnessCheck` model is already used for `LiveDrift` and `PreExecuteDryRun`. Adding `ResourceVersionCheck` as a third type keeps the pre-execution pipeline composable and testable. Each check is independently togglable via the `FreshnessPolicy` on the plan envelope.

- **A3. Capture `resourceVersion` from the raw live object JSON, not from the normalized form.** The normalizer strips `resourceVersion` intentionally (to produce clean diffs). We extract the value BEFORE normalization from the raw JSON returned by `ReadComparableLiveJsonAsync` / `ReadLiveObjectAsync`. This keeps the normalizer's contract unchanged.

- **A4. Apply-side precondition for SSA, pre-execution check for subresource operations.** SSA operations (apply/delete) can natively pass `resourceVersion` as a server-side precondition, making the check atomic. Subresource operations (scale, restart, set-image) use JSON Patch or Metadata Patch, which don't support `resourceVersion` preconditions — for these, we rely on the pre-execution freshness check alone. This means a small window remains for subresource operations, but it's drastically narrowed (between freshness check and dispatch, milliseconds, in the same thread).

- **A5. No removal of `force-conflicts` — it's already absent.** The codebase does not use `force: true` on any SSA call. The `FormatServerSideApplyException` error message explicitly documents this choice. No code change needed here.

## Implementation Approach: Two-Layer Defense

```
Layer 1: Pre-execution ResourceVersion freshness check (covers ALL operations)
    └── CheckPreExecutionAsync adds a new ResourceVersionCheck before other checks
    └── Reads current live object metadata, compares resourceVersion with stored value
    └── Rejects immediately if mismatch (fast-fail, no full drift check needed)

Layer 2: Server-side resourceVersion precondition (covers SSA apply/delete only)
    └── ApplyObjectAsync sets resourceVersion on the V1ObjectMeta before SSA patch
    └── Kubernetes API rejects with 409 Conflict if version changed (atomic)
```

## Task List

### Phase 1: Data Model Changes

- [ ] **Task 1:** Add `ResourceVersion` field to `KubernetesPlanDiff` model
  - **Description:** Add `string? ResourceVersion` property to the `KubernetesPlanDiff` record in `src/InfraGate.KubernetesAdapter/Evidence/KubernetesPlanDiff.cs`. Mirror the change in `src/InfraGate.McpServer/Models/KubernetesPlanDiff.cs`. Default to `null` for backward compatibility with existing stored plans.
  - **Acceptance criteria:**
    - [ ] `KubernetesPlanDiff` (both adapter and McpServer copies) has `string? ResourceVersion` property
    - [ ] Default value is `null` (existing plans without resourceVersion continue to work)
    - [ ] Constructor parameter list updated with `string? resourceVersion = null` as last parameter
  - **Verification:**
    - [ ] Build succeeds: `dotnet build src/InfraGate.KubernetesAdapter/`
    - [ ] Build succeeds: `dotnet build src/InfraGate.McpServer/`
  - **Dependencies:** None
  - **Files likely touched:**
    - `src/InfraGate.KubernetesAdapter/Evidence/KubernetesPlanDiff.cs`
    - `src/InfraGate.McpServer/Models/KubernetesPlanDiff.cs`
  - **Estimated scope:** Small (2 files, 1 property each)

- [ ] **Task 2:** Add `ResourceVersionCheck` to `FreshnessCheckTypes`
  - **Description:** Add `public const string ResourceVersionCheck = "kubernetes.resource-version";` to `KubernetesAdapterConventions.FreshnessCheckTypes`.
  - **Acceptance criteria:**
    - [ ] New constant `ResourceVersionCheck` exists alongside `LiveDrift` and `PreExecuteDryRun`
  - **Verification:**
    - [ ] Build succeeds
  - **Dependencies:** None
  - **Files likely touched:**
    - `src/InfraGate.KubernetesAdapter/KubernetesAdapterConventions.cs`
  - **Estimated scope:** XS (1 file, 1 line)

### Phase 2: Capture ResourceVersion at Plan Creation

- [ ] **Task 3:** Extract `resourceVersion` from live object JSON before normalization
  - **Description:** In `KubernetesDiffService.BuildDiffsAsync`, after `ReadComparableLiveJsonAsync` returns the raw live JSON, parse it and extract `metadata.resourceVersion` before passing to `BuildDiff`. Pass the extracted value to `BuildDiff` and into the `KubernetesPlanDiff`. Handle null/missing gracefully (set null).
  - **Acceptance criteria:**
    - [ ] `BuildDiffsAsync` extracts `resourceVersion` from raw `liveJson` before normalization
    - [ ] `BuildDiff` accepts and forwards `resourceVersion` to `KubernetesPlanDiff` constructor
    - [ ] When live object is null (deleted/not found), `resourceVersion` is null
    - [ ] When live JSON has no `metadata.resourceVersion`, value is null (no crash)
  - **Verification:**
    - [ ] Build succeeds
    - [ ] Existing `KubernetesDiffServiceTests` pass unmodified (new parameter has default null)
  - **Dependencies:** Task 1 (KubernetesPlanDiff.ResourceVersion field), Task 2 (convention constant)
  - **Files likely touched:**
    - `src/InfraGate.McpServer/Evidence/Diff/KubernetesDiffService.cs`
  - **Estimated scope:** Small (1 file, ~15 lines changed)

- [ ] **Task 4:** Add `ResourceVersionCheck` to the plan's `FreshnessPolicy` at creation time
  - **Description:** In the plan builders (`ApplyManifestBuilder`, `DeleteManifestBuilder`, `ScaleDeploymentBuilder`, `RestartDeploymentBuilder`, `SetDeploymentImageBuilder`), when constructing the `FreshnessPolicy` for the plan envelope, include a `ResourceVersionCheck` if any diff has a non-null `ResourceVersion`. The check's parameters should include the object keys and their expected resourceVersions.
  - **Acceptance criteria:**
    - [ ] When diffs contain resourceVersions, FreshnessPolicy includes ResourceVersionCheck
    - [ ] Check parameters map object keys to resourceVersions
    - [ ] When all resourceVersions are null (no live objects existed), no ResourceVersionCheck is added
  - **Verification:**
    - [ ] Build succeeds
    - [ ] Unit test: plan builder creates policy with ResourceVersionCheck when diffs have versions
    - [ ] Unit test: plan builder omits ResourceVersionCheck when no versions captured
  - **Dependencies:** Task 3 (resourceVersion extraction)
  - **Files likely touched:**
    - `src/InfraGate.KubernetesAdapter/PlanBuilding/ApplyManifestBuilder.cs`
    - `src/InfraGate.KubernetesAdapter/PlanBuilding/DeleteManifestBuilder.cs`
    - `src/InfraGate.KubernetesAdapter/PlanBuilding/ScaleDeploymentBuilder.cs`
    - `src/InfraGate.KubernetesAdapter/PlanBuilding/RestartDeploymentBuilder.cs`
    - `src/InfraGate.KubernetesAdapter/PlanBuilding/SetDeploymentImageBuilder.cs`
  - **Estimated scope:** Medium (5 files, ~5 lines each)

### Checkpoint: Foundation
- [ ] All model changes build clean
- [ ] ResourceVersion captured at plan creation
- [ ] FreshnessPolicy includes ResourceVersionCheck when applicable

### Phase 3: Assert ResourceVersion at Pre-Execution

- [ ] **Task 5:** Implement `CheckResourceVersionAsync` in `KubernetesPlanExecutor`
  - **Description:** Add a new private method `CheckResourceVersionAsync` that reads the `ResourceVersionCheck` from the plan's `FreshnessPolicy`, parses the parameters (object-key → expected-resourceVersion), reads each live object from Kubernetes, extracts current resourceVersion, and compares. Returns a `ResultFailure?` on mismatch.
  - **Acceptance criteria:**
    - [ ] Method extracts ResourceVersionCheck from FreshnessPolicy.Checks
    - [ ] For each object key, reads live object metadata.resourceVersion
    - [ ] Compares current vs expected; returns failure with ReasonCode on mismatch
    - [ ] Returns null (success) when all match
    - [ ] Returns null when no ResourceVersionCheck present in policy
  - **Verification:**
    - [ ] Build succeeds
    - [ ] Unit test: mismatch returns failure with correct ReasonCode
    - [ ] Unit test: match returns null
    - [ ] Unit test: missing check returns null (backward compat)
    - [ ] Unit test: null stored version still permits execution (no false positives)
  - **Dependencies:** Task 2 (convention constant), Task 4 (FreshnessPolicy populated)
  - **Files likely touched:**
    - `src/InfraGate.KubernetesAdapter/Execution/KubernetesPlanExecutor.cs`
  - **Estimated scope:** Medium (1 file, ~40 lines new method + ~5 lines in CheckPreExecutionAsync)

- [ ] **Task 6:** Integrate `CheckResourceVersionAsync` into `CheckPreExecutionAsync`
  - **Description:** Call `CheckResourceVersionAsync` as the FIRST check in `CheckPreExecutionAsync`, before the drift check. This provides fast-fail: if the resourceVersion changed, we don't waste time on a full drift check. Only proceed to drift check if resourceVersion check passes (or isn't applicable).
  - **Acceptance criteria:**
    - [ ] ResourceVersion check runs before drift detection in CheckPreExecutionAsync
    - [ ] On failure, returns `DomainPlanExecutionResult.Blocked` with audit entry
    - [ ] Audit event uses a new or existing audit event name (e.g., reuse `ApplyDriftDetected` or add `ResourceVersionMismatch`)
  - **Verification:**
    - [ ] Build succeeds
    - [ ] Integration test: plan with stale resourceVersion is rejected before drift check
    - [ ] Integration test: plan with current resourceVersion proceeds to drift check
  - **Dependencies:** Task 5
  - **Files likely touched:**
    - `src/InfraGate.KubernetesAdapter/Execution/KubernetesPlanExecutor.cs`
    - (audit event payload — may need a new `AuditPayload`)
  - **Estimated scope:** Small (1 file, ~10 lines changed)

### Checkpoint: Pre-Execution Gate
- [ ] ResourceVersion check runs before drift detection
- [ ] Stale resourceVersion blocks execution with audit trail
- [ ] All existing tests pass

### Phase 4: Server-Side Precondition for SSA Apply

- [ ] **Task 7:** Set `resourceVersion` precondition on SSA apply objects
  - **Description:** In `KubernetesExecutionService.ApplyObjectAsync` and `DryRunApplyObjectAsync` (or their helper methods), after parsing the manifest into Kubernetes objects, set each object's `Metadata.ResourceVersion` to the captured value from the plan's diffs. This makes the SSA ApplyPatch call atomically fail with 409 Conflict if the object was modified.
  - **Acceptance criteria:**
    - [ ] SSA apply objects carry `resourceVersion` from the plan when available
    - [ ] Kubernetes API rejects with 409 Conflict if resourceVersion changed
    - [ ] Error message is surfaced to the caller with a clear explanation
    - [ ] When resourceVersion is not present (legacy plans), apply proceeds without precondition
  - **Verification:**
    - [ ] Build succeeds
    - [ ] Unit test: mock Kubernetes client verifies resourceVersion is set on patched object
    - [ ] Unit test: missing resourceVersion does not block apply (backward compat)
  - **Dependencies:** Task 3 (resourceVersion in diffs), requires flow-through from executor to execution service
  - **Files likely touched:**
    - `src/InfraGate.McpServer/Execution/KubernetesExecutionService.cs`
    - `src/InfraGate.McpServer/Evidence/KubernetesEvidenceService.cs` (for dry-run path)
    - Potentially a new method parameter or DTO to pass resourceVersions from executor → execution service
  - **Estimated scope:** Medium-Large (3-5 files, requires threading resourceVersion through dispatch path)

### Checkpoint: Atomic Apply
- [ ] SSA apply uses resourceVersion precondition
- [ ] 409 Conflict properly surfaced
- [ ] Backward compatible with legacy plans (no resourceVersion)

### Phase 5: Tests

- [ ] **Task 8:** Add unit tests for `KubernetesPlanExecutor.CheckResourceVersionAsync`
  - **Description:** In `KubernetesPlanExecutorTests.cs`, add test cases covering: resourceVersion match, resourceVersion mismatch, missing check (backward compat), null stored version, multiple objects with one mismatch, all objects match.
  - **Acceptance criteria:**
    - [ ] 6+ test cases covering all states
    - [ ] All tests pass
  - **Verification:**
    - [ ] `dotnet test tests/InfraGate.KubernetesAdapter.Tests/ --filter "ResourceVersion"`
  - **Dependencies:** Task 5, Task 6
  - **Files likely touched:**
    - `tests/InfraGate.KubernetesAdapter.Tests/KubernetesPlanExecutorTests.cs`
  - **Estimated scope:** Medium (1 file, ~100 lines)

- [ ] **Task 9:** Add unit tests for `KubernetesDiffService` resourceVersion capture
  - **Description:** In `KubernetesDiffServiceTests.cs`, add test cases verifying: resourceVersion extracted from live JSON, null when no live object, null when JSON has no metadata.resourceVersion, value propagated to KubernetesPlanDiff.
  - **Acceptance criteria:**
    - [ ] 4+ test cases covering extraction scenarios
    - [ ] All tests pass
  - **Verification:**
    - [ ] `dotnet test tests/InfraGate.McpServer.Tests/ --filter "ResourceVersion"`
  - **Dependencies:** Task 3
  - **Files likely touched:**
    - `tests/InfraGate.McpServer.Tests/UnitTests/KubernetesDiffServiceTests.cs`
  - **Estimated scope:** Small (1 file, ~60 lines)

- [ ] **Task 10:** Verify existing tests still pass
  - **Description:** Run the full test suite to confirm no regressions. All model changes use default parameter values, so existing tests should pass unmodified.
  - **Acceptance criteria:**
    - [ ] Full test suite passes: `dotnet test InfraGate.slnx`
  - **Verification:**
    - [ ] `dotnet test InfraGate.slnx` exit code 0
  - **Dependencies:** All previous tasks
  - **Estimated scope:** XS (verification only)

### Checkpoint: Test Coverage
- [ ] New test cases pass
- [ ] Full test suite passes
- [ ] No regressions

### Phase 6: Documentation

- [ ] **Task 11:** Update security audit document
  - **Description:** Update `F-02` in `.agents/Plans/loose/security-audit.md` to reflect the new resolution status. Mark as `✅ MITIGATED` with Implementation Notes documenting the resourceVersion capture, freshness check, and SSA precondition.
  - **Acceptance criteria:**
    - [ ] F-02 resolution updated from ⚠️ PARTIAL to ✅ MITIGATED
    - [ ] Implementation Notes section updated with new code references
  - **Verification:**
    - [ ] Document review: all new code paths documented
  - **Dependencies:** All previous tasks
  - **Files likely touched:**
    - `.agents/Plans/loose/security-audit.md`
  - **Estimated scope:** XS (1 file, ~10 lines)

### Checkpoint: Complete
- [ ] All acceptance criteria met
- [ ] Ready for review
- [ ] Security audit updated

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Scale subresource doesn't support resourceVersion precondition** | Medium | Layer 1 (pre-execution check) covers this. The window between check and dispatch is milliseconds in the same async call chain. |
| **Legacy stored plans lack resourceVersion** | Low | All new fields default to `null`. FreshnessCheck is only added when versions are actually captured. Pre-execution checks treat null as "skip check." |
| **Duplicate McpServer/Adapter model copies** | Low | `KubernetesPlanDiff` and other models exist in both `InfraGate.McpServer.Models` and `InfraGate.KubernetesAdapter.Evidence`. Both copies need updating. This is an existing pattern in the codebase. |
| **SSA resourceVersion precondition causes unexpected 409 for operators** | Medium | The error message should clearly explain that the plan is stale and needs re-creation. This is the intended behavior and the fix will include a clear error message. |
| **Dry-run path also needs resourceVersion flow** | Low | The dry-run path (`DryRunApplyObjectAsync`) already has access to the live object's JSON. Same extraction logic applies. |

## Open Questions

- **Q1:** Should the ResourceVersionCheck use a new audit event type (e.g., `ResourceVersionMismatch`) or reuse the existing `ApplyDriftDetected` event?
  - **Recommendation:** New event `ResourceVersionMismatch` for clarity in audit logs. Different root cause, different event.

- **Q2:** Should we also capture `generation` alongside `resourceVersion`? (Generation changes on spec writes, resourceVersion changes on every write including metadata-only.)
  - **Recommendation:** Capture both. `resourceVersion` is the canonical optimistic concurrency token. `generation` provides an additional signal for spec changes specifically. Store `generation` in the diff too.

- **Q3:** For subresource operations (scale), the pre-execution check covers the Deployment's resourceVersion. But the Scale subresource has its own resourceVersion. Should we check that too?
  - **Recommendation:** Check the Deployment's resourceVersion. The Scale subresource version changes whenever the Deployment spec or status changes (since it's derived), so the Deployment's resourceVersion is sufficient.

## Parallelization Opportunities

| Tasks | Safe to Parallelize | Notes |
|-------|---------------------|-------|
| Task 1 + Task 2 | ✅ Yes | Independent model changes |
| Task 3 + Task 7 | ❌ No | Task 7 depends on Task 3's capture |
| Task 5 + Task 8 | ❌ No | Task 8 tests Task 5 |
| Task 8 + Task 9 | ✅ Yes | Independent test files |
| Task 4 | ❌ Depends on Task 3 | needs captured ResourceVersion |
| Task 6 | ❌ Depends on Task 5 | integration of the check |
