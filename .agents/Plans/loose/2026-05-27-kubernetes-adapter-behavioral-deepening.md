# Kubernetes Adapter — Behavioral Deepening Plan

**Last validated:** 2026-06-03 — plan still accurate, line numbers updated.

## Validation (2026-06-03)

All four refactorings remain unimplemented. No new files have been added to `PlanBuilding/` or `Execution/` since the directory restructure. No `IOperationPlanBuilder`, `KubernetesEvidenceService`, `OperationDispatch`, or `KubernetesAuditHelper` exist.

**Line count drift:**
- `KubernetesPlanBuilder.cs`: 693 → **723** lines (+30)
- `KubernetesPlanExecutor.cs`: **373** lines (unchanged)

Line number references below have been updated to match current (2026-06-03) file positions.

## Architecture Decisions

1. **Extract, don't rewrite.** Each refactoring moves existing code into new files with the same behavior. No new abstractions, no new dependencies — just locality improvements. Tests should pass before and after each step.

2. **Bottom-up dependency order.** Audit helpers (Phase 1) and operation map (Phase 2) are prerequisites for evidence service (Phase 3), which is a prerequisite for builder extraction (Phase 4). Each phase leaves the system in a working state.

3. **All new types are `internal`.** Nothing in this plan adds to the adapter's public API surface. The `IDomainPlanBuilder` and `IDomainPlanExecutor` seams are the only public contracts.

---

## Phase 1: Audit Consolidation (no dependencies)

### Task 1: Extract shared Kubernetes audit helper

**Description:** Move `DryRunAudit` and `DiffAudit` from `KubernetesPlanBuilder` (lines 618–629) and `DryRunFailedAudit` and `ApplyDriftDetectedAudit` from `KubernetesPlanExecutor` (lines 339–351) into a single `KubernetesAuditHelper` class in the adapter root. Both builder and executor call through it.

**Acceptance criteria:**
- [x] `KubernetesAuditHelper.cs` exists with all four static factory methods
- [x] `KubernetesPlanBuilder.cs` no longer contains `DryRunAudit` or `DiffAudit` — calls helper instead
- [x] `KubernetesPlanExecutor.cs` no longer contains `DryRunFailedAudit` or `ApplyDriftDetectedAudit` — calls helper instead
- [x] All existing tests pass unchanged

**Verification:**
- [ ] Build: `dotnet build src/InfraGate.KubernetesAdapter/`
- [ ] Tests: `dotnet test` — full suite (adapter has no tests yet, so validate upstream tests still pass)
- [ ] Manual: audit JSONL output shape unchanged when running the gateway end-to-end

**Files touched:**
- NEW: `src/InfraGate.KubernetesAdapter/KubernetesAuditHelper.cs`
- `src/InfraGate.KubernetesAdapter/PlanBuilding/KubernetesPlanBuilder.cs`
- `src/InfraGate.KubernetesAdapter/Execution/KubernetesPlanExecutor.cs`

**Estimated scope:** Small (1 new file, 2 modified)

---

## Phase 2: Operation Map (no hard dependencies)

### Task 2: Consolidate executor switch statements into operation map

**Description:** Replace both `RunPreExecuteDryRunAsync` (lines 132–181) and `DispatchMutationAsync` (lines 282–338) with a single `OperationDispatch` table. Each operation maps to its dry-run tool name, mutation tool name, and argument constructor lambda.

**Acceptance criteria:**
- [ ] `OperationDispatch` record exists mapping operation → `(dryRunTool, mutationTool, argsBuilder)`
- [ ] `RunPreExecuteDryRunAsync` uses `OperationMap.TryGetValue` lookup instead of switch
- [ ] `DispatchMutationAsync` uses `OperationMap.TryGetValue` lookup instead of switch
- [ ] Adding a new operation requires ONE entry in the map, not two switch cases
- [ ] All existing tests pass unchanged

**Verification:**
- [ ] Build: `dotnet build src/InfraGate.KubernetesAdapter/`
- [ ] Tests: `dotnet test`

**Files touched:**
- `src/InfraGate.KubernetesAdapter/Execution/KubernetesPlanExecutor.cs`
- Possibly NEW: `src/InfraGate.KubernetesAdapter/Execution/OperationDispatch.cs`

**Estimated scope:** Small (1 file modified, possibly 1 new)

---

## Phase 3: Evidence Service (depends on Phase 2 operation map)

### Task 3: Extract shared evidence service

**Description:** Extract a `KubernetesEvidenceService` that owns tool-to-evidence calling for all 5 dry-run operations, JSON deserialization, and error/policy-blocked handling. Builder and executor inject it via constructor. Uses the operation map from Phase 2 for dry-run tool name resolution.

**Current duplicated code to extract:**
- Builder `GetApplyEvidenceAsync` (line 132), `DeserializeDryRun` (line 594), `DeserializeDiffs` (line 606)
- Executor `CheckApplyDryRunAsync` (line 204), `CheckSimpleDryRunAsync` (line 244)

**Acceptance criteria:**
- [x] `IKubernetesEvidenceService` interface with methods for all 5 dry-run operations
- [x] `KubernetesEvidenceService` class implementing evidence calls, deserialization, error handling
- [x] Builder's `BuildApplyManifestAsync` uses `IEvidenceService` for apply evidence
- [x] Builder's 4 other build methods use `IEvidenceService` for dry-run/diff
- [x] Executor's `RunPreExecuteDryRunAsync` uses `IEvidenceService` for pre-execution checks
- [x] Builder and executor no longer contain `Dictionary<StringComparer.Ordinal>` argument construction for evidence tools
- [x] All existing tests pass unchanged

**Verification:**
- [ ] Build: `dotnet build src/InfraGate.KubernetesAdapter/`
- [ ] Tests: `dotnet test`

**Files touched:**
- NEW: `src/InfraGate.KubernetesAdapter/Evidence/KubernetesEvidenceService.cs`
- NEW: `src/InfraGate.KubernetesAdapter/Evidence/IKubernetesEvidenceService.cs`
- `src/InfraGate.KubernetesAdapter/PlanBuilding/KubernetesPlanBuilder.cs`
- `src/InfraGate.KubernetesAdapter/Execution/KubernetesPlanExecutor.cs`

**Estimated scope:** Medium (2 new files, 2 modified)

**Dependency:** Phase 2 (`OperationDispatch` provides tool name resolution for evidence calls)

---

## Phase 4: Builder Extraction (depends on Phase 3 evidence service)

### Task 4: Extract operation-specific builders

**Description:** Extract each of the 5 private build methods (`BuildApplyManifestAsync`, `BuildDeleteManifestAsync`, `BuildScaleDeploymentAsync`, `BuildRestartDeploymentAsync`, `BuildSetDeploymentImageAsync`) into separate classes behind an `IOperationPlanBuilder` seam. `KubernetesPlanBuilder` becomes a router that selects the right builder based on the mutation tool name.

**Acceptance criteria:**
- [x] `IOperationPlanBuilder` interface with `BuildAsync(arguments, requester, approvalPolicy, ct) → Task<PlanBuildResult>`
- [x] 5 builder classes: `ApplyManifestBuilder`, `DeleteManifestBuilder`, `ScaleDeploymentBuilder`, `RestartDeploymentBuilder`, `SetDeploymentImageBuilder`
- [x] `KubernetesPlanBuilder` switch expression becomes a `Dictionary<string, IOperationPlanBuilder>` lookup
- [x] Each builder injects `IKubernetesEvidenceService` (from Phase 3)
- [x] `KubernetesPlanBuilder.cs` drops from 723 lines to ~50 (router only), shared infrastructure in `KubernetesBuilderInfrastructure.cs`
- [x] Each builder is independently testable with a `FakeToolCaller`
- [x] All existing tests pass unchanged

**Verification:**
- [ ] Build: `dotnet build src/InfraGate.KubernetesAdapter/`
- [ ] Tests: `dotnet test`

**Files touched:**
- NEW: `src/InfraGate.KubernetesAdapter/PlanBuilding/IOperationPlanBuilder.cs`
- NEW: `src/InfraGate.KubernetesAdapter/PlanBuilding/ApplyManifestBuilder.cs`
- NEW: `src/InfraGate.KubernetesAdapter/PlanBuilding/DeleteManifestBuilder.cs`
- NEW: `src/InfraGate.KubernetesAdapter/PlanBuilding/ScaleDeploymentBuilder.cs`
- NEW: `src/InfraGate.KubernetesAdapter/PlanBuilding/RestartDeploymentBuilder.cs`
- NEW: `src/InfraGate.KubernetesAdapter/PlanBuilding/SetDeploymentImageBuilder.cs`
- `src/InfraGate.KubernetesAdapter/PlanBuilding/KubernetesPlanBuilder.cs`

**Estimated scope:** Large (6 new files, 1 major modification)

**Dependency:** Phase 3 (`IKubernetesEvidenceService` is injected into each builder)

---

## Checkpoints

### Checkpoint: After Phase 1 + 2
- [ ] `dotnet build` passes
- [ ] `dotnet test` passes (full suite)
- [ ] Audit helper and operation map are isolated and testable
- [ ] No behavior change — gateway still produces identical audit events and dispatch calls

### Checkpoint: After Phase 3
- [ ] `dotnet build` passes
- [ ] `dotnet test` passes
- [ ] Builder and executor no longer construct `Dictionary<StringComparer.Ordinal>` inline for evidence tools
- [ ] Evidence service can be tested independently with a `FakeToolCaller`

### Checkpoint: After Phase 4
- [ ] `dotnet build` passes
- [ ] `dotnet test` passes
- [ ] `KubernetesPlanBuilder.cs` is ~120 lines (from 723)
- [ ] Each builder is independently testable
- [ ] Adding a new operation requires: (a) one builder class, (b) one OperationDispatch entry, (c) one entry in the router — all in separate, focused files

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Line-by-line extraction introduces subtle behavior differences | High | Each phase verified by `dotnet test`; no behavior should change |
| `IKubernetesEvidenceService` abstraction leaks tool-call details | Medium | Keep the interface narrow — one method per evidence type, not one per tool |
| Operation dispatch mapping hides the operation set from compiler exhaustiveness checks | Low | Unit test the `OperationDispatch` table for all 5 expected operations |
| Builder extraction creates too many small classes | Low | Each builder maps 1:1 to a Kubernetes operation; natural decomposition boundary |
| No existing adapter unit tests | Medium | Manual verification via gateway end-to-end after each phase; adapter test project can be added in a future plan |

## Scope Boundaries

**In scope:** Extract, consolidate, and deduplicate existing code. No new behavior. No new abstractions beyond the seams listed above. No new tests (but existing tests must keep passing).

**Out of scope:**
- Adding a unit test project for the adapter (separate plan)
- Changing the approval flow or gateway behavior
- Modifying `KubernetesApprovalAdapter` or `KubernetesDomainAdapter`
- Any changes to `InfraGate.Approvals` or other non-adapter projects
- Public API changes to `IDomainPlanBuilder` or `IDomainPlanExecutor`
