# Implementation Plan: Decoupling McpServer from Approvals and KubernetesAdapter

## Overview
We will sever the project references from `InfraGate.McpServer` to `InfraGate.Approvals` and `InfraGate.KubernetesAdapter`. This ensures the MCP server acts purely as a generic tool execution adapter that communicates with the Gateway strictly over the Model Context Protocol (JSON-RPC) without leaking domain concepts (like approval plans or approval storage paths) into the executor.

## Architecture Decisions
- The `McpServer` will define its own local DTO records for JSON serialization (e.g. `KubernetesApplyEvidence`, `KubernetesPlanDiff`) inside an `InfraGate.McpServer.Models` namespace instead of importing them from the adapter.
- The `McpServer` will define its own local string constants for JSON diff outputs (e.g., `"create"`, `"update"`) rather than referencing `ApprovalConventions`.
- The `McpServer` configuration (`KubernetesMcpOptions`) will drop all awareness of `ApprovalRoot` since it never writes to it.
- The Gateway (`InfraGate.KubernetesAdapter`) will seamlessly deserialize the server's raw JSON outputs back into its own domain objects. The contract becomes the JSON schema itself.

## Task List

### Phase 1: Foundation (Configuration Cleanup)
## Task 1: Remove `ApprovalRoot` from `KubernetesMcpOptions`

**Description:** Remove the unused `ApprovalRoot` configuration and validation from the server's options since the server never persists approval plans.

**Acceptance criteria:**
- [ ] `ApprovalRoot`, `IsApprovalRootExplicit`, and `DeniedApprovalRootNames` are removed from `KubernetesMcpOptions`.
- [ ] Environment variable parsing for `K8S_MCP_APPROVAL_ROOT` is removed.
- [ ] `ProductionSafetyValidator.RequirePersistentDirectory` for `ApprovalRoot` is removed.
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

### Phase 2: DTO and Constant Localization
## Task 2: Create local DTOs in `McpServer/Models`

**Description:** To remove the dependency on `InfraGate.KubernetesAdapter`, we must copy the evidence and diff DTOs into the `McpServer` project. These will be strictly used for JSON serialization to satisfy the Gateway's expected schema.

**Acceptance criteria:**
- [ ] Copy `KubernetesApplyEvidence`, `KubernetesPlanPolicyFinding`, `KubernetesPlanDryRun`, `KubernetesPlanDryRunObject`, `KubernetesPlanDiff`, `KubernetesPlanDiffChange`, and `KubernetesObjectRef` records into `src/InfraGate.McpServer/Models/`.
- [ ] Update their namespace to `InfraGate.McpServer.Models`.
- [ ] Ensure they are functionally identical for JSON serialization purposes.

**Verification:**
- [ ] Project builds: `dotnet build src/InfraGate.McpServer`

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.McpServer/Models/*.cs`

**Estimated scope:** Medium: ~7 files

## Task 3: Localize `DiffChangeTypes` constants

**Description:** Remove usages of `ApprovalConventions` inside `McpServer` by defining local string constants for diff changes and date formatting.

**Acceptance criteria:**
- [ ] Add a local `DiffChangeTypes` subclass to `KubernetesConventions` with `Create = "create"`, `Update = "update"`, `Delete = "delete"`, `NoOp = "no-op"`.
- [ ] Replace `ApprovalConventions.DateTimeFormats.RoundTrip` with `"O"` directly.
- [ ] Update `KubernetesDiffService` and `KubernetesEvidenceService` to use these local constants.

**Verification:**
- [ ] `McpServer` has no `using InfraGate.Approvals;` directives left.

**Dependencies:** Task 2

**Files likely touched:**
- `src/InfraGate.McpServer/KubernetesConventions.cs`
- `src/InfraGate.McpServer/Evidence/KubernetesEvidenceService.cs`
- `src/InfraGate.McpServer/Evidence/Diff/KubernetesDiffService.cs`
- `src/InfraGate.McpServer/Execution/KubernetesExecutionService.cs`

**Estimated scope:** Small: 4 files

### Phase 3: Sever Dependencies
## Task 4: Remove Project References and Update Usages

**Description:** Delete the project references to `InfraGate.Approvals` and `InfraGate.KubernetesAdapter` and update all `using` directives in the server code to use the new local `Models` namespace.

**Acceptance criteria:**
- [ ] `<ProjectReference Include="..\InfraGate.Approvals\InfraGate.Approvals.csproj" />` is removed from `InfraGate.McpServer.csproj`.
- [ ] `<ProjectReference Include="..\InfraGate.KubernetesAdapter\InfraGate.KubernetesAdapter.csproj" />` is removed from `InfraGate.McpServer.csproj`.
- [ ] Fix compiler errors in `McpServer` by replacing `using InfraGate.KubernetesAdapter.Evidence;` with `using InfraGate.McpServer.Models;`.

**Verification:**
- [ ] `McpServer` compiles successfully without the dependencies.

**Dependencies:** Task 3

**Files likely touched:**
- `src/InfraGate.McpServer/InfraGate.McpServer.csproj`
- `src/InfraGate.McpServer/Evidence/KubernetesEvidenceService.cs`
- `src/InfraGate.McpServer/Evidence/Diff/KubernetesDiffService.cs`
- `src/InfraGate.McpServer/KubernetesConventions.cs`

**Estimated scope:** Small: 4 files

## Task 5: Fix Test Project Compilation

**Description:** The unit tests in `InfraGate.McpServer.Tests` might have been referencing the old `InfraGate.KubernetesAdapter` types. Update them to use the local `InfraGate.McpServer.Models` namespace.

**Acceptance criteria:**
- [ ] `InfraGate.McpServer.Tests` compiles.
- [ ] All tests pass.

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests`
- [ ] End-to-end tests verify that the Gateway still successfully parses the server's output: `dotnet test tests/InfraGate.Safety.E2E.Tests`

**Dependencies:** Task 4

**Files likely touched:**
- `tests/InfraGate.McpServer.Tests/**/*.cs`

**Estimated scope:** Medium: 3-5 files

### Checkpoint: Complete
- [ ] `InfraGate.McpServer` has zero references to `Approvals` or `KubernetesAdapter`.
- [ ] All tests (unit and E2E) pass.
- [ ] Ready for review.

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| JSON Serialization Mismatch | High | The DTO properties in `McpServer.Models` must precisely match the properties in `KubernetesAdapter`. We will run the `Safety.E2E.Tests` to guarantee the JSON string returned by the server is perfectly deserialized by the Gateway. |

## Open Questions
- None.
