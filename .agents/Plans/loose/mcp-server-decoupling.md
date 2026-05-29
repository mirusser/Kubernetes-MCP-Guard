# Implementation Plan: Decoupling McpServer from Approvals and KubernetesAdapter

## Overview
We will sever the project references from `InfraGate.McpServer` to `InfraGate.Approvals` and `InfraGate.KubernetesAdapter`. The MCP server becomes a pure Kubernetes execution substrate that communicates with the Gateway strictly over the Model Context Protocol (JSON-RPC) without carrying domain concepts (approval plans, approval storage, domain policy checks) that belong to the Generic Approval Core and Kubernetes Domain Adapter.

This plan continues the architectural direction of ADR-0001 (Separate Generic Approval Core From Domain Adapters), ADR-0006 (McpGateway Pure Generic Approval Core), and lesson `[architecture]` L36 ("Don't pass approval persistence configuration to McpServer").

## Architecture Decisions
- **McpServer becomes a pure executor.** It no longer runs `KubernetesPolicyValidator` — domain policy checks move entirely to the Gateway's `KubernetesAdapter` (KubernetesPlanBuilder and KubernetesPlanExecutor). The evidence JSON emits `PolicyFindings = []`, `PolicyBlocked = false`, `PolicyRefusal = null`.
- **Local DTO records.** McpServer defines its own evidence/diff DTOs in `InfraGate.McpServer.Models` for JSON serialization. The contract between McpServer and the Gateway is the JSON schema.
- **`KubernetesObjectRef` via MSBuild file-link.** Per lesson `[architecture]` L54, the single shared data record `KubernetesObjectRef.cs` from `KubernetesAdapter/PlanBuilding/` is compiled into McpServer via `<Compile Include>` — no shared DLL, no duplication, no transitive dependency.
- **`KubernetesPlanPolicyFinding` drops `: IDomainPolicyCheck`.** McpServer only serializes policy findings to JSON; it never polymorphically dispatches on the interface. The copied DTO is a plain record.
- **Local string constants.** `DiffChangeTypes` and `DateTimeFormats.RoundTrip` are localized into `KubernetesConventions`.
- **`ApprovalRoot` removed.** McpServer never persists approval plans.

## Task List

### Phase 1: Configuration Cleanup
## Task 1: Remove `ApprovalRoot` from `KubernetesMcpOptions`

**Description:** Remove the unused `ApprovalRoot` configuration and production safety validation from the server's options since the server never persists approval plans.

**Acceptance criteria:**
- [ ] `ApprovalRoot`, `IsApprovalRootExplicit`, and `DeniedApprovalRootNames` are removed from `KubernetesMcpOptions`.
- [ ] Environment variable constant `EnvironmentVariables.ApprovalRoot` alias is removed from `KubernetesConventions`.
- [ ] Environment variable parsing for `K8S_MCP_APPROVAL_ROOT` is removed from `FromEnvironment()` and `FromConfiguration()`.
- [ ] `ProductionSafetyValidator.RequirePersistentDirectory` call for `ApprovalRoot` is removed from `ValidateProductionSafety()`.
- [ ] `InfraGate.McpServer.Tests/UnitTests/K8SMcpOptionsTests.cs` is updated to remove approval root test setups and assertions.

**Verification:**
- [ ] Tests pass: `dotnet test tests/InfraGate.McpServer.Tests`

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.McpServer/Configuration/KubernetesMcpOptions.cs`
- `src/InfraGate.McpServer/KubernetesConventions.cs`
- `tests/InfraGate.McpServer.Tests/UnitTests/K8SMcpOptionsTests.cs`

**Estimated scope:** Small: 3 files

### Checkpoint: Foundation
- [ ] Server options build and tests pass.

### Phase 2: Remove Policy Validator
## Task 2: Remove `KubernetesPolicyValidator` from McpServer

**Description:** Domain policy checks are the KubernetesAdapter's responsibility (per ADR-0001 and CONTEXT.md). Remove `KubernetesPolicyValidator` and `KubernetesPolicyOptions` usage from McpServer. Evidence tools will emit raw dry-run results without policy judgment. The Gateway-side adapter will run its own policy checks.

**Acceptance criteria:**
- [ ] `KubernetesPolicyValidator` and `KubernetesPolicyOptions` constructor parameters and DI registrations are removed from `KubernetesEvidenceService` and `KubernetesExecutionService`.
- [ ] Evidence generation produces `PolicyFindings = Array.Empty<KubernetesPlanPolicyFinding>()`, `PolicyBlocked = false`, `PolicyRefusal = null` (or the fields are dropped if the Gateway adapter handles their absence).
- [ ] Execution no longer calls `KubernetesPolicyValidator.ValidateAsync()` as a pre-execution gate.
- [ ] No `using InfraGate.KubernetesAdapter.Policy;` remains in McpServer.

**Verification:**
- [ ] `dotnet build src/InfraGate.McpServer`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests`

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.McpServer/Evidence/KubernetesEvidenceService.cs`
- `src/InfraGate.McpServer/Execution/KubernetesExecutionService.cs`
- McpServer DI registration / `Program.cs` (if policy validator is registered there)
- Tests referencing `KubernetesPolicyValidator` in `McpServer.Tests`

**Estimated scope:** Medium: 4-6 files

### Phase 3: DTO and Constant Localization
## Task 3: Create local DTOs in `McpServer/Models`

**Description:** Copy the evidence and diff DTOs into the `McpServer` project for JSON serialization. These are strictly data records with no logic.

**Acceptance criteria:**
- [ ] Copy `KubernetesApplyEvidence`, `KubernetesPlanPolicyFinding` (without `: IDomainPolicyCheck`), `KubernetesPlanDryRun`, `KubernetesPlanDryRunObject`, and `KubernetesPlanDiff` into `src/InfraGate.McpServer/Models/`.
- [ ] Update their namespace to `InfraGate.McpServer.Models`.
- [ ] `KubernetesPlanPolicyFinding` is a plain `sealed record class` — no interface inheritance.
- [ ] `KubernetesPlanDiff` references `KubernetesObjectRef` which is file-linked (Task 4), not copied.
- [ ] All DTO properties precisely match the KubernetesAdapter originals for JSON schema compatibility.

**Verification:**
- [ ] Project builds: `dotnet build src/InfraGate.McpServer`

**Dependencies:** Task 2

**Files likely touched:**
- `src/InfraGate.McpServer/Models/KubernetesApplyEvidence.cs`
- `src/InfraGate.McpServer/Models/KubernetesPlanPolicyFinding.cs`
- `src/InfraGate.McpServer/Models/KubernetesPlanDryRun.cs`
- `src/InfraGate.McpServer/Models/KubernetesPlanDryRunObject.cs`
- `src/InfraGate.McpServer/Models/KubernetesPlanDiff.cs`

**Estimated scope:** Small: 5 files (all new)

## Task 4: MSBuild file-link `KubernetesObjectRef`

**Description:** `KubernetesObjectRef` is a pure data record (4-property positional record, no external dependencies) used in 7+ McpServer files. Instead of copying it, compile it from the KubernetesAdapter source via MSBuild `<Compile Include>` per lesson `[architecture]` L54.

**Acceptance criteria:**
- [ ] Add `<Compile Include="..\..\src\InfraGate.KubernetesAdapter\PlanBuilding\KubernetesObjectRef.cs" Link="Models\KubernetesObjectRef.cs" />` to `InfraGate.McpServer.csproj`.
- [ ] Verify that `KubernetesObjectRef` resolves in all McpServer files without the KubernetesAdapter project reference.
- [ ] Update Dockerfile for McpServer to COPY the KubernetesAdapter directory (per lesson `[docker-build]` L55).

**Verification:**
- [ ] `dotnet build src/InfraGate.McpServer`

**Dependencies:** Task 3

**Files likely touched:**
- `src/InfraGate.McpServer/InfraGate.McpServer.csproj`
- McpServer Dockerfile (if exists)

**Estimated scope:** Small: 1-2 files

## Task 5: Localize `DiffChangeTypes` and `DateTimeFormats` constants

**Description:** Remove usages of `ApprovalConventions` inside McpServer by defining local string constants.

**Acceptance criteria:**
- [ ] Add `DiffChangeTypes` nested class to `KubernetesConventions` with `Create = "create"`, `Update = "update"`, `Delete = "delete"`, `NoOp = "no-op"`.
- [ ] Add `DateTimeFormats` nested class with `RoundTrip = "O"`.
- [ ] Replace all `ApprovalConventions.DiffChangeTypes.*` in `KubernetesDiffService.cs`.
- [ ] Replace all `ApprovalConventions.DateTimeFormats.RoundTrip` in `KubernetesEvidenceService.cs` and `KubernetesExecutionService.cs`.
- [ ] Remove `using InfraGate.Approvals;` and `using InfraGate.Approvals.Plan;` from all McpServer files.

**Verification:**
- [ ] `grep -r "InfraGate.Approvals" src/InfraGate.McpServer/` returns zero results.

**Dependencies:** Task 3

**Files likely touched:**
- `src/InfraGate.McpServer/KubernetesConventions.cs`
- `src/InfraGate.McpServer/Evidence/KubernetesEvidenceService.cs`
- `src/InfraGate.McpServer/Evidence/Diff/KubernetesDiffService.cs`
- `src/InfraGate.McpServer/Execution/KubernetesExecutionService.cs`
- `src/InfraGate.McpServer/Configuration/KubernetesMcpOptions.cs`

**Estimated scope:** Small: 5 files

### Phase 4: Sever Project References
## Task 6: Remove Project References and Update Usings

**Description:** Delete the project references to `InfraGate.Approvals` and `InfraGate.KubernetesAdapter` and update all `using` directives to use local namespaces.

**Acceptance criteria:**
- [ ] `<ProjectReference Include="..\InfraGate.Approvals\InfraGate.Approvals.csproj" />` is removed.
- [ ] `<ProjectReference Include="..\InfraGate.KubernetesAdapter\InfraGate.KubernetesAdapter.csproj" />` is removed.
- [ ] Replace `using InfraGate.KubernetesAdapter.Evidence;` with `using InfraGate.McpServer.Models;` in all files.
- [ ] Replace `using InfraGate.KubernetesAdapter.PlanBuilding;` — `KubernetesObjectRef` resolves via file-link.
- [ ] Remove `using InfraGate.KubernetesAdapter;` and update references to use local types/constants.
- [ ] Handle `KubernetesManagerHelpers.cs` — uses only `KubernetesObjectRef` (file-linked), so update using only.
- [ ] Handle `Manifest/KubernetesManifestParser.cs` and `Manifest/KubernetesParsedManifest.cs` — same.

**Verification:**
- [ ] `dotnet build src/InfraGate.McpServer`
- [ ] `grep -r "InfraGate.KubernetesAdapter" src/InfraGate.McpServer/` returns zero results.
- [ ] `grep -r "InfraGate.Approvals" src/InfraGate.McpServer/` returns zero results.

**Dependencies:** Tasks 4, 5

**Files likely touched:**
- `src/InfraGate.McpServer/InfraGate.McpServer.csproj`
- `src/InfraGate.McpServer/Evidence/KubernetesEvidenceService.cs`
- `src/InfraGate.McpServer/Evidence/Diff/KubernetesDiffService.cs`
- `src/InfraGate.McpServer/Execution/KubernetesExecutionService.cs`
- `src/InfraGate.McpServer/KubernetesConventions.cs`
- `src/InfraGate.McpServer/KubernetesManagerHelpers.cs`
- `src/InfraGate.McpServer/Manifest/KubernetesManifestParser.cs`
- `src/InfraGate.McpServer/Manifest/KubernetesParsedManifest.cs`

**Estimated scope:** Medium: 8 files

### Phase 5: Test Cleanup
## Task 7: Migrate Misplaced Tests

**Description:** `InfraGate.McpServer.Tests` contains ~15 test files that directly import `InfraGate.Approvals` (11 files) and `InfraGate.KubernetesAdapter` (9 files). Many of these test KubernetesAdapter and Approvals behavior, not McpServer behavior. These tests must be migrated or updated.

**Acceptance criteria:**
- [ ] Tests that test KubernetesAdapter types directly (e.g., `KubernetesApprovalAdapterTests`, `KubernetesPlanBuilderTests`, `KubernetesPlanExecutorTests`, `KubernetesPlanReviewTests`, `KubernetesPolicyValidatorTests`, `KubernetesDomainAdapterTests`) are moved to a KubernetesAdapter test project or identified for separate migration.
- [ ] Tests that test Approvals types directly (e.g., `ApprovalConventionsTests`, `ApprovalDigestTests`, `ApprovalStoreTests`, `AuditPayloadsTests`, `FixedTimeStringComparerTests`, `PlanEnvelopeFactoryTests`) are moved to an Approvals test project or identified for separate migration.
- [ ] Tests that test McpServer behavior and only transitively referenced adapter types are updated to use local `InfraGate.McpServer.Models` types.
- [ ] `InfraGate.McpServer.Tests` has no direct import of `InfraGate.Approvals` or `InfraGate.KubernetesAdapter` namespaces.

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests`
- [ ] All migrated tests still pass in their new location.

**Dependencies:** Task 6

**Files likely touched:**
- `tests/InfraGate.McpServer.Tests/**/*.cs` (~15 files)
- New or existing test project(s) receiving migrated tests

**Estimated scope:** Large: ~15 files + possible new test project

## Task 8: Add JSON Contract Roundtrip Test

**Description:** Add a contract test that serializes each McpServer DTO and deserializes it with the corresponding KubernetesAdapter type, asserting property-level equality. This is stronger than relying on E2E tests alone.

**Acceptance criteria:**
- [ ] A test roundtrips `KubernetesApplyEvidence`, `KubernetesPlanDiff`, `KubernetesPlanDryRun`, `KubernetesPlanDryRunObject`, `KubernetesPlanPolicyFinding` through serialize (McpServer type) → deserialize (KubernetesAdapter type).
- [ ] Test asserts all properties match.
- [ ] Test lives in a cross-cutting integration test project that references both McpServer and KubernetesAdapter.

**Verification:**
- [ ] Contract test passes.

**Dependencies:** Task 6

**Files likely touched:**
- New test file in an appropriate test project

**Estimated scope:** Small: 1-2 files

### Checkpoint: Complete
- [ ] `InfraGate.McpServer.csproj` has zero `ProjectReference` to `Approvals` or `KubernetesAdapter`.
- [ ] `grep -r "InfraGate.Approvals\|InfraGate.KubernetesAdapter" src/InfraGate.McpServer/` returns zero results.
- [ ] All tests (unit, integration, and E2E) pass: `dotnet test tests/InfraGate.McpServer.Tests && dotnet test tests/InfraGate.Safety.E2E.Tests`
- [ ] JSON contract roundtrip test passes.
- [ ] Ready for review.

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| JSON Serialization Mismatch | High | Copied DTOs must have identical property names, types, and ordering. JSON contract roundtrip test (Task 8) catches drift at compile time. E2E tests validate real-world flow. |
| Policy Check Gap During Migration | Medium | While McpServer stops running policy checks, the Gateway-side KubernetesAdapter must already run its own. Verify Gateway-side `KubernetesPlanBuilder` calls `KubernetesPolicyValidator.ValidateAsync()` before marking this complete. |
| `KubernetesObjectRef` Namespace Mismatch | Low | File-linked `KubernetesObjectRef` will compile under `InfraGate.KubernetesAdapter.PlanBuilding` namespace inside McpServer. This is harmless for runtime behavior (JSON serialization uses property names, not namespace). If it causes confusion, add a namespace alias or a `using` alias. |
| Docker Build Missing COPY Layer | Medium | MSBuild file-link requires the linked file's source directory to exist in the Docker build context. Per lesson `[docker-build]` L55, add COPY for `src/InfraGate.KubernetesAdapter/PlanBuilding/` to the McpServer Dockerfile. |
| Test Migration Scope | Medium | ~15 test files need relocation. Some may require new test project scaffolding. Scope this as a separate PR if it threatens the main decoupling work. |

## Open Questions
- None. Architecture decisions resolved: policy checks → Gateway; shared types → MSBuild file-link; test migration → part of this plan.
