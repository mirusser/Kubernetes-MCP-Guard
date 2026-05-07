# Implementation Plan: Diff Before Approval

## Summary
Implement roadmap §4 fully: every mutation plan records a hash-bound, server-generated diff between normalized live Kubernetes state and normalized proposed dry-run state, and the browser approval page renders that diff before approval. Tests are a first-class part of the work, using the repo’s `writing-tests` guidance: add focused unit tests under matching `UnitTests/` folders, extend existing gateway/server integration coverage, and keep live-cluster tests opt-in.

## Key Changes
- Extend `InfraGate.Approvals` plan contracts with serialized `K8sPlanDiff[]` and `K8sPlanPolicyFinding[]`, defaulting to empty arrays for older test-created plans.
- Add an internal MCP server diff subsystem that:
  - reads live objects through typed Kubernetes client calls,
  - normalizes live/proposed JSON by removing noisy metadata/status fields,
  - emits YAML unified diffs and added/removed/changed JSON-pointer paths,
  - supports create, update, delete, and no-op diffs for all mutation plan types.
- Generate diffs after server-side dry-run success and before `ApprovalStore.CreatePlanAsync`, so the pending file hash covers dry-run output, policy warnings, and diff output.
- Enforce drift at apply time by re-reading live objects and refusing apply if normalized live state differs from stored `LiveObjectJson`.
- Update gateway approval checks/UI to require and render diff data alongside dry-run status, admission warnings, objects, requester, expiry, and policy warnings.

## Public Interfaces And Types
- `K8sPlan` gains init-only serialized properties:
  - `K8sPlanDiff[] Diffs`
  - `K8sPlanPolicyFinding[] PolicyFindings`
- Add shared approval DTOs:
  - `K8sPlanDiff(K8sObjectRef Object, string ChangeType, string Summary, string UnifiedDiff, string? LiveObjectJson, string? ProposedObjectJson, string[] AddedPaths, string[] RemovedPaths, string[] ChangedPaths)`
  - `K8sPlanPolicyFinding(string Severity, string Code, string ObjectRef, string Message)`
- Add constants for diff change types: `create`, `update`, `delete`, `no-op`.
- Add audit event constants for diff generation failure and live drift refusal.
- Add an explicit `YamlDotNet 16.3.0` package reference to `InfraGate.McpServer` if YAML rendering uses it directly.

## Implementation Tasks
1. **Plan Contract Foundation**
   - Add diff/policy DTOs in `InfraGate.Approvals`.
   - Add `Diffs` and `PolicyFindings` defaults to `K8sPlan`.
   - Map server policy warnings into stored plan findings.
   - Acceptance: existing approval store tests and current test-created plans continue to deserialize.

2. **Diff Engine**
   - Add `src/InfraGate.McpServer/Diff/` with object normalizer, typed live-reader, JSON path comparer, and unified diff formatter.
   - Normalize only roadmap noisy fields: `managedFields`, `resourceVersion`, `uid`, `creationTimestamp`, `generation`, last-applied annotation, and root `status`.
   - Acceptance: create/update/delete/no-op cases produce stable summaries, JSON paths, and unified diff text.

3. **Plan Creation Integration**
   - Generate and store diffs after dry-run success for `request_apply_manifest`, `request_delete_manifest`, `request_scale_deployment`, `request_restart_deployment`, and `request_set_deployment_image`.
   - Keep MCP response concise: say diff is recorded for browser approval, without echoing full diff to the model-visible response.
   - Acceptance: pending plan JSON contains `dryRun`, `diffs`, and any policy warnings before hashing.

4. **Apply-Time Drift Enforcement**
   - Refuse approved plans missing diff data.
   - Re-read live objects before pre-apply dry-run and compare normalized live JSON to stored `LiveObjectJson`.
   - Refuse create-after-plan, delete-after-plan, and changed-live-state drift with a clear message and audit event.
   - Acceptance: unchanged live state proceeds to pre-apply dry-run and mutation; drift never mutates Kubernetes.

5. **Gateway Approval UI**
   - Refuse approval URL creation for plans missing diff data, mirroring current dry-run fail-closed behavior.
   - Render policy warnings and per-object diff blocks in `GatewayApprovalEndpoints`.
   - HTML-encode all plan/diff content.
   - Acceptance: browser approval shows server-rendered diff and can approve/deny only valid, same-subject, unexpired, hash-matching plans.

## Unit Tests
- Add `tests/InfraGate.McpServer.Tests/UnitTests/K8sDiffServiceTests.cs`:
  - `BuildDiff_CreateObject_RecordsCreateSummaryAndAddedPaths`
  - `BuildDiff_UpdateObject_ExcludesNoisyMetadataAndStatus`
  - `BuildDiff_DeleteObject_RecordsDeleteSummaryAndRemovedPaths`
  - `BuildDiff_NoChanges_RecordsNoOp`
  - `BuildDiff_ConfigMapChange_DoesNotExposeRemovedNoisyFields`
- Extend `K8sManagerRequestTests`:
  - `RequestApplyManifestAsync_StoresDiffsInPendingPlan`
  - `RequestDeleteManifestAsync_StoresDeleteDiff`
  - `RequestScaleDeploymentAsync_StoresScaleDiff`
  - `RequestRestartDeploymentAsync_StoresRestartDiff`
  - `RequestSetDeploymentImageAsync_StoresImageDiff`
  - `RequestApplyManifestAsync_WhenLiveReadFails_DoesNotCreatePlan`
- Extend `K8sManagerApplyTests`:
  - `ApplyApprovedPlanAsync_RefusesPlanWithoutDiff`
  - `ApplyApprovedPlanAsync_WhenLiveObjectChangedAfterApproval_RefusesMutation`
  - `ApplyApprovedPlanAsync_WhenCreateTargetAppearsAfterApproval_RefusesMutation`
  - `ApplyApprovedPlanAsync_WhenDeleteTargetDisappearsAfterApproval_RefusesMutation`
  - `ApplyApprovedPlanAsync_WhenLiveStateMatchesStoredDiff_AppliesPlan`
- Extend gateway unit tests:
  - `EnsureApprovedOrCreateChallengeAsync_PlanWithoutDiff_ReturnsRefusal`
  - `GetApprovalPageAsync_ValidPlan_IncludesDiffModel`
  - `ApproveChallengeAsync_PlanHashDriftAfterDiffChange_Rejects`
- Keep test names in `Method_State_ExpectedResult` style and use existing `InternalsVisibleTo`; both server and gateway projects already expose internals to their test projects.

## Integration Tests
- Extend default gateway integration test `ApplyApprovedPlan_RequiresOutOfBandApprovalBeforeForwarding`:
  - assert the browser approval page contains a `Diff` section,
  - assert the diff includes the planned replica change,
  - assert the diff is shown before approval and no non-dry-run Kubernetes request occurs before approval.
- Add a default gateway HTTP integration test using fake Kubernetes:
  - `ApprovalPage_ForApplyManifest_RendersCreateAndUpdateDiffs`
  - create one plan where live object is missing and one where live object exists with changed fields.
- Extend opt-in server live integration `McpServer_CanApplyApprovedK8sPlans_WhenIntegrationEnabled`:
  - after requesting an apply/update/delete plan, read the pending plan from the temp approval root and assert `diffs` are present for live-cluster objects.
- Extend opt-in gateway live integration `Gateway_CanApplyApprovedK8sPlans_WhenGatewayIntegrationEnabled`:
  - open the approval page for at least one update plan and assert the rendered page includes a diff before browser approval.
- Keep live-cluster tests behind `INFRA_GATE_RUN_INTEGRATION=1` or `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1`; default integration tests must use fake/in-memory dependencies.

## Verification
- Run narrow tests first:
  - `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
  - `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Run full default suite:
  - `dotnet test InfraGate.slnx`
- Optional live checks:
  - `INFRA_GATE_RUN_INTEGRATION=1 dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
  - `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`

## Assumptions
- Scope remains roadmap §4. Safer non-force apply and broader model-visible response reduction stay separate P0 tasks.
- Existing uncommitted test changes are user work and must be preserved.
- The file-backed pending plan remains the hash boundary; storing diff data in the pending JSON makes approval hash-binding sufficient.
