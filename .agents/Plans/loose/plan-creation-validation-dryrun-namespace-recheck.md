# Plan creation validation hardening — dry-run, deployment existence check, namespace re-validation on apply

## Source

Verification of `docs/full-architecture-diagram.md` against actual source code revealed
three gaps where the diagram claims security behavior that does not exist in code,
and three understatements where the diagram omits behavior the code already has.

## Gaps — code needs new behavior

### 1. Dry-run K8s API validation during plan creation

**Diagram claim (B3, step 6):** `Svr->>Svr: dry-run against K8s API (server-side apply, force-conflicts)`

**Code reality:** No dry-run or any K8s API call happens during plan creation for any
`request_*` tool (except `request_set_deployment_image` which does a read-only
`ReadNamespacedDeploymentAsync` to show current→target image). Plan creation is purely
local: validate parameters → create `K8sPlan` record → write JSON to
`pending/{planId}.json` → compute SHA-256. A grep for `dryRun`/`DryRun` across the entire
`src/` tree returns zero results.

**Plan:** Add a dry-run server-side apply to K8s API during `request_apply_manifest`
plan creation to validate the manifest before presenting it for approval. This catches
schema errors, field name typos, namespace mismatches, and RBAC issues at plan time
rather than at apply time (when the user has already approved). Use the Kubernetes
client's `dryRun` parameter (`dryRun: ["All"]`) with server-side apply and
`force: true` / `fieldManager`.

**Files:** `src/InfraGate.McpServer/K8sManager.Requests.cs`
(`RequestApplyManifestAsync` and potentially `RequestDeleteManifestAsync`)

For `request_delete_manifest`: validate the resources exist before planning a
deletion (read them first via the K8s API).

### 2. Deployment existence check for non-manifest plan tools

**Diagram claim (B3, step 5):** `Svr->>Svr: K8sManifestParser — validate kind (Deployment / Service / ConfigMap only)`

**Code reality:** For the diagram's example tool `request_scale_deployment`, `K8sManifestParser`
is never called (`K8sManager.Requests.cs:80-92`). Only parameter-level validation happens
(namespace allowlist, non-empty name, replicas 0–5). The same applies to
`request_restart_deployment` and `request_set_deployment_image`.

**Plan:** Add a K8s read to confirm the target Deployment exists before creating a scale,
restart, or set-image plan. This prevents creating plans against non-existent Deployments
and catches namespace/name typos at plan time.

**Files:** `src/InfraGate.McpServer/K8sManager.Requests.cs`
(`RequestScaleDeploymentAsync`, `RequestRestartDeploymentAsync`)

### 3. Namespace re-validation on apply

**Diagram claim:** Implicit — the gateway+server re-validate all security constraints at every step.

**Code reality:** `ApplyApprovedPlanAsync` (`K8sManager.Apply.cs`) never calls
`ValidateNamespace`. The namespace was validated at plan creation time, but if
`K8S_MCP_ALLOWED_NAMESPACES` changes between plan creation and apply, a previously-allowed
namespace mutation could execute against a now-disallowed namespace.

**Plan:** Add a `ValidateNamespace(plan.Namespace)` call at the top of
`ApplyApprovedPlanAsync`, before any approval check or K8s API call.

**Files:** `src/InfraGate.McpServer/K8sManager.Apply.cs` (`ApplyApprovedPlanAsync`)

---

## Understatements — diagram needs updating (code already has this)

### 4. Two hash check points, not one

**Diagram claim (B3, step 15):** Single hash re-computation at apply time.

**Code reality:** Two independent hash checks exist:
- **Approval time** (`ApprovalStore.cs:144`): `ApprovePendingPlanAsync` — compares user-echoed
  hash vs fresh hash of pending file.
- **Apply time** (`ApprovalStore.cs:85`): `GetApprovedPlanAsync` — compares stored approved
  hash vs fresh hash of pending file.

Both write `"approval_hash_mismatch"` audit entries and reject. This is defense-in-depth
against plan tampering at either stage.

**Plan:** Update diagram B3 to show both checks, or add a note that the check runs twice.

**Files:** `docs/full-architecture-diagram.md`

### 5. Two conditional audit points in Gateway, not one

**Diagram claim (B2 & B3):** Single "audit if needed" step after sanitization.

**Code reality** (`GuardedToolRunner.cs:45-93`): Two independent conditional audit points:
- **Pre-call audit** (line 55): if request argument scan has findings (`RequestDirection` + `WarnAction`).
- **Post-call audit** (line 71): if response sanitization found issues (`ResponseDirection` + `WarnRedactAction` or `RedactManifestAction`).

The request is still forwarded even if pre-call findings exist — guard warns, does not block.

**Plan:** Update diagrams B2 and B3 to show the pre-call audit point.

**Files:** `docs/full-architecture-diagram.md`, `README.md`

### 6. Identity resolution and response warning

**Diagram claim:** Neither step is shown.

**Code reality:**
- `GuardedToolRunner.cs:51`: `GetAuditIdentity()` resolves `(subject, authenticationType)`
  via `GatewayAuditIdentityResolver` before scanning — maps OAuth JWT / static bearer /
  anonymous for audit entries.
- `GuardedToolRunner.cs:87-92`: When either scan finds issues, a warning string
  (`"Guardrail warning: Potential prompt-injection content was detected..."`) is
  prepended to the response.

**Plan:** Optionally add identity resolution to diagrams B2/B3. Lower priority.

**Files:** `docs/full-architecture-diagram.md`, `README.md`

### 7. `ClientAllowsRedirectUri` annotation placement (B1)

**Diagram claim:** `ClientAllowsRedirectUri` in DCR registration block.

**Code reality:** Called during authorization (`GET /authorize`,
`DevIssuerApplication.Authorization.cs:68`), not registration. Only `IsLoopbackHttpUri`
is DCR-only.

**Plan:** Move `ClientAllowsRedirectUri` annotation to the authorization step in diagram B1.

**Files:** `docs/full-architecture-diagram.md`, `README.md`

---

## Implementation order

1. **Code: dry-run validation during plan creation** (gap 1) — highest value, replaces
   a false diagram claim with actual security behavior.
2. **Code: namespace re-validation on apply** (gap 3) — simple fix, closes a security gap.
3. **Code: deployment existence check** (gap 2) — simple K8s read, catches typos early.
4. **Diagram: update B3** for two hash checks (gap 4) + dry-run (gap 1) + deploy check (gap 2).
5. **Diagram: update B2/B3** for two audit points (gap 5) + identity resolution (gap 6).
6. **Diagram: fix B1 annotation** (gap 7).

## Verification

After implementation:
- `request_apply_manifest` dry-runs against K8s API and returns dry-run result in plan summary.
- `request_delete_manifest` reads resources to confirm they exist before planning deletion.
- `request_scale_deployment` and `request_restart_deployment` read the target Deployment
  to confirm it exists before creating a plan.
- `apply_approved_plan` rejects if the plan's namespace is no longer in the allowed list.
- `docs/full-architecture-diagram.md` diagrams match the actual code flow for all steps.
- Existing tests pass; new tests verify the new validation paths.
