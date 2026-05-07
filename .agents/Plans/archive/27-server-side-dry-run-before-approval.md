# Implementation Plan: P0 Server-Side Dry-Run Before Approval

## Summary
Implement the full security-roadmap section 3 dry-run behavior: every mutation plan must pass Kubernetes `dryRun=All` before a pending plan is created, the hash-bound plan must store the dry-run result, the gateway approval page must show dry-run status, and `apply_approved_plan` must repeat dry-run immediately before the real write. No silent fallback: dry-run failure blocks planning or apply.

## Public Interfaces And Contracts
- Extend `InfraGate.Approvals.K8sPlan` with a final optional `K8sPlanDryRun? DryRun = null` field for backward-compatible deserialization.
- Add shared approval DTOs: `K8sPlanDryRun(Status, CheckedAtUtc, Objects, Warnings, Message)` and `K8sPlanDryRunObject(Object, ResponseJson)`.
- Add audit event constant `dry_run_failed` with payload `{ phase, planId?, operation, namespace, objects, message }`.
- MCP tool names/arguments do not change. Model-visible plan responses add `Dry-run: succeeded`; browser approval reads dry-run data from the stored plan, not from model text.
- Add `K8sConventions.K8sApi` constants for `DryRunAll = "All"` and `FieldValidationStrict = "Strict"`.

## Task List
### Task 1: Add Hash-Bound Dry-Run Plan Data
**Description:** Add the shared dry-run DTOs and extend `K8sPlan` so dry-run output is serialized into pending plans and therefore included in the existing SHA-256 hash.

**Acceptance criteria:**
- Pending plan JSON includes `dryRun` for new plans.
- Existing tests that construct `K8sPlan` still compile via the default `DryRun = null`.
- Approval hash computation automatically includes dry-run content.

**Verification:** `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`

**Dependencies:** None
**Estimated scope:** S

### Task 2: Add Kubernetes Dry-Run Executor
**Description:** Add `K8sManager.DryRun.cs` with helpers for apply, delete, scale, restart, and set-image operations. Use KubernetesClient 19 overloads/`WithHttpMessagesAsync` calls with `dryRun: "All"`; capture response bodies and Kubernetes `Warning` headers. Use `fieldValidation: "Strict"` for patch/apply paths.

**Acceptance criteria:**
- Apply manifest dry-runs Deployment, Service, and ConfigMap via server-side apply patch.
- Delete dry-runs supported objects via delete APIs.
- Scale, restart, and set-image dry-run their exact patch shapes.
- Dry-run helpers return structured success data or a single formatted failure message.

**Verification:** Unit tests assert captured fake-Kubernetes requests contain `dryRun=All`, `fieldManager=infra-gate-mcp`, and strict field validation where applicable.

**Dependencies:** Task 1
**Estimated scope:** M

### Task 3: Enforce Request-Time Dry-Run Before Plan Creation
**Description:** Wire dry-run into every `request_*` mutation path before `ApprovalStore.CreatePlanAsync`. If dry-run fails, write `dry_run_failed`, return a refusal, and do not create a pending plan.

**Acceptance criteria:**
- `request_apply_manifest` runs parser, namespace validation, policy validation, then dry-run, then creates the plan.
- `request_delete_manifest`, `request_scale_deployment`, `request_restart_deployment`, and `request_set_deployment_image` dry-run before plan creation.
- Dry-run failure leaves no pending plan file and returns no `PlanId`.
- Legacy parser gap is closed for unsupported YAML fields by using strict supported-manifest parsing.

**Verification:** Add tests such as `RequestApplyManifestAsync_WhenDryRunFails_DoesNotCreatePlan`, `RequestScaleDeploymentAsync_DryRunsBeforePlan`, and `RequestDeleteManifestAsync_WhenDryRunDeleteFails_DoesNotCreatePlan`.

**Dependencies:** Task 2
**Estimated scope:** M

### Task 4: Repeat Dry-Run Immediately Before Apply
**Description:** In each apply execution path, re-run the matching dry-run after existing hash, namespace, policy, and stale-plan checks but before the real Kubernetes write. Refuse plans with missing recorded `DryRun` so old pending files cannot bypass the new guarantee.

**Acceptance criteria:**
- `apply_approved_plan` refuses approved plans created without recorded dry-run data.
- Pre-apply dry-run failure writes `dry_run_failed` and prevents the real mutation.
- Existing policy revalidation and set-image stale image checks remain intact.
- Actual apply behavior keeps the current force setting; changing force defaults is reserved for roadmap item 4.

**Verification:** Add tests proving pre-apply dry-run failure does not issue a non-dry-run PATCH/DELETE, and legacy no-dry-run plans are refused.

**Dependencies:** Task 3
**Estimated scope:** M

### Task 5: Show Dry-Run In Gateway Approval
**Description:** Update `GatewayApprovalService` and `GatewayApprovalEndpoints` so browser approval requires and renders stored dry-run data.

**Acceptance criteria:**
- Gateway refuses to create an approval challenge for a pending plan with no `DryRun`.
- Approval page shows `Server-side dry-run: succeeded`, checked time, affected objects, and admission warnings.
- Approval page does not render raw dry-run JSON or raw manifests; diff and richer display stay in roadmap item 4.

**Verification:** Gateway unit/integration tests cover dry-run rendering and challenge refusal for legacy plans.

**Dependencies:** Task 1
**Estimated scope:** S

### Task 6: Update Tests And Docs
**Description:** Update server/gateway tests and docs whose current contracts say request-time mutation tools make no Kubernetes API calls.

**Acceptance criteria:**
- Tests no longer assert empty Kubernetes requests for plan creation; they assert dry-run calls instead.
- `docs/tool-permissions.md` lists request-time dry-run verbs/resources.
- README/dev docs mention that approval plans are Kubernetes dry-run validated before browser approval.
- Architecture wording remains accurate now that the dry-run step exists in code.

**Verification:** `dotnet build InfraGate.slnx`, `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`, `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`; optional opt-in integration tests with the existing env flags.

**Dependencies:** Tasks 3-5
**Estimated scope:** M

## Assumptions
- Scope is all mutation plan types, per your selected broader path.
- No fallback is allowed when Kubernetes dry-run cannot be performed.
- Existing pending plans without dry-run must be re-requested.
- This plan does not implement browser diff, model-visible redaction changes, or safer non-force apply defaults except where strict field validation is needed for dry-run correctness.
