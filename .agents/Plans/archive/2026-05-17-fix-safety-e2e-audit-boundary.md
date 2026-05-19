# Fix Safety E2E Audit Boundary

**Date:** 2026-05-17  
**Context:** Commit `61384aa` (gateway-domain-adapter separation) deleted `K8sManager.Requests.cs` which owned dry-run audit emission. The new architecture must restore audit emission without crossing the Generic Approval Core / Domain Adapter boundary.

## Problem

The Safety E2E tests (`dry_run_failed` audit expectations) fail because:
1. `ParsePlanId` uses brittle "PlanId:" string matching — broken by new `Approval plan '...' created.` response format
2. Three tests use `DownstreamClient` (stdio McpServer) to call `request_*` tools — but `request_*` moved to the gateway
3. `dry_run_failed` audit event is never emitted after `K8sManager.Requests.cs` was deleted

Fixes 1 and 2 are already applied. Fix 3 was wrongly implemented by injecting `ApprovalStore` into the Kubernetes adapter, which crosses the architecture boundary.

## Architecture Decision

Per CONTEXT.md:225 and mutation-approval-flow.md:44-76:

- **Generic Approval Core** owns the **Audit Spine** (where/how to write audit events)
- **Domain Adapter** owns **Adapter Audit Payloads** (what data goes in the payload)
- The sequence is: adapter returns result + audit payload → core writes the audit

**Solution**: Add an optional `PlanAudit?` field to `PlanBuildResult` and `DomainPlanExecutionResult`. The adapter constructs the audit payload on failure. The gateway (core) reads it and writes it via `ApprovalStore`.

A single shared type carries the audit intent across the seam:

```csharp
// PlanAudit.cs (new, in InfraGate.Approvals)
public sealed record PlanAudit(string EventName, IPlanAuditPayload Payload);
```

## Task List

### Checkpoint A — Seam (no behavioral change)

- [x] **Task A1**: Add `PlanAudit` record in `InfraGate.Approvals`
  - File: `src/InfraGate.Approvals/PlanAudit.cs` (new)
  - Verify: `dotnet build InfraGate.slnx`

- [x] **Task A2**: Add `PlanAudit? Audit` to `DomainPlanExecutionResult`
  - File: `src/InfraGate.Approvals/DomainPlanExecutionResult.cs`
  - New: `Blocked` overload accepting `PlanAudit?`
  - Verify: existing executor tests compile and pass

- [x] **Task A3**: Add `PlanAudit? Audit` to `PlanBuildResult`
  - File: `src/InfraGate.Approvals/PlanBuildResult.cs`
  - New: `Failed` overload accepting `PlanAudit?`
  - Verify: existing builder tests compile and pass

- **Checkpoint**: `dotnet test InfraGate.slnx --filter Category!=Keycloak` passes

### Checkpoint B — Adapter returns audit payloads

- [x] **Task B1**: Revert `ApprovalStore` from adapter + tests
  - Files: `KubernetesPlanBuilder.cs`, `KubernetesPlanExecutor.cs`, both test files
  - Verify: build succeeds, unit tests pass

- [x] **Task B2**: Builder returns `PlanAudit` on evidence dry-run failure
  - File: `src/InfraGate.KubernetesAdapter/KubernetesPlanBuilder.cs`
  - In `BuildApplyManifestAsync`: construct `PlanAudit` with `DryRunFailedPayload("request", ...)` at evidence-failure return points
  - Verify: builder tests pass, `PlanBuildResult.Audit` null for success

- [x] **Task B3**: Executor returns `PlanAudit` on pre-execute dry-run failure
  - File: `src/InfraGate.KubernetesAdapter/KubernetesPlanExecutor.cs`
  - In `ExecuteAsync`: construct `PlanAudit` with `DryRunFailedPayload("pre-apply", ...)` on dry-run block
  - Verify: executor tests pass, `DomainPlanExecutionResult.Audit` non-null for dry-run block

- **Checkpoint**: all unit tests pass, build clean

### Checkpoint C — Core writes audits + E2E passes

- [x] **Task C1**: Gateway writes audit on plan build failure
  - File: `src/InfraGate.McpGateway/GatewayToolDispatcher.cs`
  - In `HandleRequestMutationAsync`: if `planResult.Audit` non-null, write via `approvalStore.WriteAuditAsync`
  - Verify: `RequestApplyManifest_FailingStrictDryRun_DoesNotCreatePendingPlanAndAudits` passes

- [x] **Task C2**: Gateway writes audit on execution failure
  - File: `src/InfraGate.McpGateway/GatewayToolDispatcher.cs`
  - In `HandleApplyApprovedPlanAsync`: if `executeResult.Audit` non-null, write it; else fall back to `ApplyDenied`
  - Verify: `ApplyApprovedPlan_PreApplyDryRunFailsAfterApproval_IsRefusedAndAudited` passes

- **Checkpoint**: `./scripts/run-tests.sh` — all 5 tiers pass

## Dependency Order

```
A1 → A2, A3 (parallel)
A3 → B1
B1 → B2, B3 (parallel)
B2 → C1
B3 → C2
```

C1 and C2 are independent.
