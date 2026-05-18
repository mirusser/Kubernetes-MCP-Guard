**Remediation Plan**
Assumptions:
- Keep this pass focused on true gaps: audit flow, set-image policy coverage, challenge dedup.
- Do not implement reusable plans, delegated approval, retry semantics, or redacted evidence now.
- `IApprovalAuditPublisher` is implementation plumbing, not a new glossary term.

Architecture decisions:
- Generic core owns the Audit Spine and persistence.
- Domain adapters publish adapter audit payloads through a narrow `IApprovalAuditPublisher`.
- `execution.started` is emitted at the Kubernetes execution boundary and excludes grant/digest proof.
- Grant and gate proof is audited from `ApprovalPreExecutionGate` and adapter pre-execution checks.
- Generic audit payloads expose flexible `adapterPayload`; Kubernetes uses strong records before serializing into that slot.

## Task 1: Add Approval Audit Publisher

**Description:** Add a small audit publisher abstraction in `InfraGate.Approvals` and route it to `ApprovalStore.WriteAuditAsync`.

**Acceptance criteria:**
- `IApprovalAuditPublisher.PublishAsync(PlanAudit, ct)` exists.
- Production DI routes publisher events to `audit.jsonl`.
- Tests can use a recording/no-op publisher.

**Verification:**
- `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter "AuditPayloadsTests|ApprovalConventionsTests"`

**Dependencies:** None  
**Files likely touched:** `InfraGate.Approvals`, `InfraGate.McpGateway/Program.cs`  
**Scope:** S

## Task 2: Add Explicit Gate Audit Events

**Description:** Emit `pre_execution.grant.validated` after generic grant validation and `pre_execution.checked` after Kubernetes pre-execution checks pass.

**Acceptance criteria:**
- New audit event constants are pinned.
- Grant validation event includes grant/generic proof fields.
- Adapter check event includes nested `adapterPayload`.
- Existing blocked paths still emit `execution.blocked`.

**Verification:**
- Unit tests for `ApprovalPreExecutionGate`.
- Unit tests for `KubernetesPlanExecutor.CheckPreExecutionAsync`.

**Dependencies:** Task 1  
**Files likely touched:** `ApprovalPreExecutionGate.cs`, `PlanAuditPayloads.cs`, `KubernetesPlanExecutor.cs`, tests  
**Scope:** M

## Task 3: Emit `execution.started`

**Description:** Have `KubernetesPlanExecutor.ExecuteAsync` publish `execution.started` immediately before the raw mutation tool call.

**Acceptance criteria:**
- Event is emitted only after pre-execution gates have passed.
- Payload contains `planId`, `operation`, `adapterId`, and nested Kubernetes execution context.
- Payload does not include grant id, approver, or digest proof.

**Verification:**
- `KubernetesPlanExecutorTests` proves started event is emitted before mutation dispatch.
- Dispatcher tests prove no started event when pre-execution blocks.

**Dependencies:** Task 1  
**Files likely touched:** `KubernetesPlanExecutor.cs`, audit payload records, tests  
**Scope:** M

## Task 4: Close Set-Image Domain Policy Gap

**Description:** Apply Kubernetes domain policy to `set_deployment_image` at plan creation and pre-execution.

**Acceptance criteria:**
- `set_deployment_image` with implicit/latest image is rejected.
- Pinned images still work.
- Delete/scale/restart are documented or tested as current no-op policy cases.

**Verification:**
- `K8sPolicyValidatorTests`
- `KubernetesPlanBuilderTests`
- `KubernetesPlanExecutorTests`

**Dependencies:** None  
**Files likely touched:** `K8sPolicyValidator.cs`, `KubernetesPlanBuilder.cs`, `KubernetesPlanExecutor.cs`, tests  
**Scope:** M

## Task 5: Deduplicate Pending Challenges

**Description:** Reuse an existing still-pending challenge URL for the same plan/requester/hash/digests instead of creating duplicates.

**Acceptance criteria:**
- Repeated `execute_approved_plan(planId)` returns the same active challenge URL.
- Expired or terminal challenges do not block new challenge creation.
- Approved grant path remains unchanged.

**Verification:**
- `GatewayApprovalServiceTests`
- `ApprovalChallengeStoreTests`

**Dependencies:** None  
**Files likely touched:** `ApprovalChallengeStore.cs`, `GatewayApprovalService.cs`, tests  
**Scope:** M

## Task 6: Update Docs

**Description:** Update profile/flow docs so the target model matches the chosen audit split.

**Acceptance criteria:**
- Audit Spine lists `pre_execution.grant.validated`, `pre_execution.checked`, and `execution.started`.
- Flow docs show generic gate audit versus adapter execution audit.
- Findings doc or remediation note clarifies that apply policy already re-checks; set-image was the real gap.

**Verification:**
- `rg "execution.started|pre_execution.grant.validated|pre_execution.checked" CONTEXT.md docs .agents/Plans/findings`
- `git diff --check`

**Dependencies:** Tasks 1-4 decisions finalized  
**Scope:** S

Checkpoint after Tasks 1-3:
- Audit publisher and audit event flow works without changing mutation behavior.

Checkpoint after Tasks 4-5:
- Safety gaps closed and challenge behavior is less noisy.

Final verification:
- `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Ideally: `dotnet test InfraGate.slnx --filter "Category!=Keycloak"`
