# Kubernetes Adapter — Behavioral Deepening Opportunities

These candidates were identified during the directory-structure restructure of `src/InfraGate.KubernetesAdapter/`. They involve **code changes** (extracting modules, eliminating duplication) — unlike the slice restructure which was purely structural.

---

## 1. Extract operation-specific builders from `KubernetesPlanBuilder`

**Files:** `src/InfraGate.KubernetesAdapter/PlanBuilding/KubernetesPlanBuilder.cs` (693 lines)

**Problem:** All 5 operations (apply, delete, scale, restart, set-image) live in one monolithic switch statement. Understanding, changing, or testing any single operation requires reading the entire file. Each operation follows the same pattern (parse args → dry-run → diff → build envelope) but with enough variation that shared infrastructure is duplicated inline.

**Solution:** Extract each operation into its own builder class behind an internal `IOperationPlanBuilder` seam. `KubernetesPlanBuilder` becomes a simple router. Each builder receives `IToolCaller` and owns the complete build method for its operation.

**Benefits:**
- **Locality** — change to `apply` logic is in `ApplyManifestBuilder`, not a 693-line file
- **Leverage** — routing interface stays the same (`BuildAsync`), implementation becomes modular
- **Tests** — each builder tested in isolation with its own `FakeToolCaller`

---

## 2. Consolidate duplicated dry-run/diff evidence logic between Builder and Executor

**Files:**
- `PlanBuilding/KubernetesPlanBuilder.cs` (lines 117–155: `GetApplyEvidenceAsync`, lines 579–601: `DeserializeDryRun`/`DeserializeDiffs`)
- `Execution/KubernetesPlanExecutor.cs` (lines 198–263: `CheckApplyDryRunAsync`/`CheckSimpleDryRunAsync`)

**Problem:** Builder and Executor call the same evidence tools (`dry_run_apply_manifest`, `check_live_drift`, etc.) with identical tool-name→argument construction, JSON deserialization, null/error handling, and policy-blocked checks. A change to the evidence format requires touching both files in lockstep.

**Solution:** Extract a `KubernetesEvidenceService` that owns tool-to-evidence calling for all 5 dry-run operations, plus the `KubernetesApplyEvidence` deserialization + policy check for apply, and the `KubernetesPlanDryRun` deserialization for other operations. Both builder and executor inject it.

**Benefits:**
- **Locality** — one place to change when evidence format changes
- **Leverage** — the evidence service interface hides tool names, argument construction, deserialization, and error handling
- **Tests** — `KubernetesEvidenceService` tested independently; builder and executor tests stub it out

---

## 3. Consolidate duplicate switch statements in `KubernetesPlanExecutor`

**Files:** `Execution/KubernetesPlanExecutor.cs` (lines 126–175: `RunPreExecuteDryRunAsync`, lines 276–328: `DispatchMutationAsync`)

**Problem:** Two switch statements over the same 5 operations, each constructing identical argument dictionaries with `StringComparer.Ordinal` and `GetValueOrDefault` lookups. Adding a new operation requires updating both. If one drifts out of sync, a plan passes pre-execution but fails at dispatch — a runtime bug caught only in production.

**Solution:** Introduce an operation→tool mapping table that both methods reference:

```csharp
private static readonly Dictionary<string, OperationDispatch> OperationMap = new(...)
{
    [Apply] = new("dry_run_apply_manifest", "apply_manifest", args => ...),
    ...
}
```

Both `RunPreExecuteDryRunAsync` and `DispatchMutationAsync` become `OperationMap.TryGetValue(...)` lookups.

**Benefits:**
- **Locality** — adding an operation means one entry in the map, not two switch cases
- **Leverage** — mapping table concentrates all operation→tool relationships
- **Tests** — one test for the mapping table covers both dispatch paths

---

## 4. Consolidate audit event helpers into a shared helper

**Files:**
- `PlanBuilding/KubernetesPlanBuilder.cs` (lines 603–626: `DryRunAudit`, `DiffAudit`)
- `Execution/KubernetesPlanExecutor.cs` (lines 333–351: `DryRunFailedAudit`, `ApplyDriftDetectedAudit`)

**Problem:** Both builder and executor construct `PlanAudit` instances with near-identical `AuditPayloads.*` payload patterns. Audit event construction logic is scattered across two files, making it hard to verify audit schemas at a glance.

**Solution:** Extract a `KubernetesAuditHelper` class with methods like `DryRunFailedAudit(planId, operation, namespaceName, message)`. Both builder and executor call through it.

**Benefits:**
- **Locality** — all Kubernetes audit event constructions in one place
- **Tests** — audit event format/field correctness tested once
