# Mutation-Approval Drift Remediation Plan

Date: 2026-05-18

## Purpose

Remediate the implementation drift from the directional mutation-approval docs captured in `.agents/Plans/2026-05-17-mutation-approval-flow-verification.md` without weakening the canonical mutation-approval language in `CONTEXT.md`, ADR 0001, ADR 0002, or ADR 0006.

The plan follows the `grill-with-docs` workflow: resolve one design question at a time, check code when the answer is discoverable, and update `CONTEXT.md` or ADRs only when a durable term or decision changes.

## Assumptions

- "Drift" means the six findings in the May 17 verification plan: `canceled` challenge status, audit event naming, review-digest evidence coverage, unused `DomainPolicyCheck`, distributed pre-execution gates, and unused audit constants.
- `CONTEXT.md` remains a glossary, not an implementation status ledger.
- `docs/mutation-approval-profile.md` and `docs/mutation-approval-flow.md` describe the direction this repository is going. Treat them as target-state design docs.
- The default remediation direction is to make implementation catch up to the target docs. Do not downgrade target docs to match current implementation unless the target itself is wrong.
- Implementation changes should be surgical: no broad refactor unless a drift item cannot be resolved honestly in docs.

## Resolved Grilling Decisions

1. `docs/mutation-approval-profile.md` and `docs/mutation-approval-flow.md` are directional target-state docs for where InfraGate is going.
   - Consequence: drift findings are implementation gaps unless the target terminology itself is wrong.
   - Consequence: `CONTEXT.md` and the profile docs should not be weakened just because code has not caught up yet.
2. Challenge cancellation is part of this implementation effort.
   - Consequence: do not remove or downscope `canceled` from the directional docs.
   - Consequence: add `canceled` status/outcome support, audit emission, and focused tests in the approval lifecycle.
3. Cancellation should support requester-initiated withdrawal through the browser approval surface now.
   - Consequence: add a browser cancel action for pending approval challenges, allowed only for the same authenticated subject as the requester under the current **Same-Subject Approval** policy.
   - Consequence: model the `canceled` **Challenge Outcome** with a nullable actor subject so system-initiated cancellation can be added later without changing the outcome shape.
   - Consequence: keep cancellation distinct from denial: cancellation withdraws the approval attempt before approval or denial, while denial records that the approver reviewed and rejected the mutation.
4. Persisted InfraGate audit event values should move to the dot-separated **Audit Spine** names through constants and tests.
   - Consequence: change `ApprovalConventions.AuditEvents` values toward target names such as `plan.created`, `challenge.created`, `challenge.approved`, `challenge.canceled`, `grant.issued`, `execution.blocked`, and `execution.succeeded`.
   - Consequence: add tests that pin the constant values so future audit drift is caught directly.
   - Consequence: update tests and operational docs that assert or display persisted event names; do not add a compatibility mapping layer for this implementation plan.
5. Explicit **Evidence Artifact** digest records should be implemented before broader adapter-seam work.
   - Consequence: stop treating raw adapter payload hashing as sufficient **Review Digest** coverage.
   - Consequence: add a generic review artifact summary that the **Generic Approval Core** can include in review-digest canonicalization without owning domain-specific evidence meaning.
   - Consequence: have the Kubernetes adapter derive digest-bound artifacts for dry-run, diff, policy findings, and redaction metadata before ADR 0006's larger dynamic adapter seam.
6. **Domain Policy Checks** are owned by **Domain Adapters**, but the **Generic Approval Core** may define a base policy-check contract.
   - Consequence: do not keep `DomainPolicyCheck` as an unused concrete generic record.
   - Consequence: replace it with a generic base type or interface that carries only cross-adapter fields needed for review/gating/audit while leaving concrete policy meaning to adapter-specific types.
   - Consequence: Kubernetes policy findings should inherit or implement that base contract rather than being redefined as generic policy semantics.
7. **Pre-Execution Gate** orchestration should become a concrete generic module now.
   - Consequence: move generic grant, digest, validity, authorization, reuse, and orchestration concerns into a **Generic Approval Core** gate module.
   - Consequence: call adapter-owned freshness and domain policy checks through an explicit seam instead of leaving the gateway/executor flow as informal sequencing.
   - Consequence: keep Kubernetes execution behavior adapter-owned; the generic gate module decides whether execution may proceed, not how the target system mutates.
8. Failure audit constants are intended guarantees only for user-visible failure paths that need an audit trail.
   - Consequence: add production writers and tests for required failure events.
   - Consequence: remove constants if they are speculative and not part of the target **Audit Spine** or a needed adapter audit payload.
   - Consequence: after Task 3 renames, failure events should use target-style audit names such as `execution.blocked`, `execution.failed`, or adapter-specific payload events instead of stale underscore names.

## Grilling Queue

All currently identified drift decisions are resolved. Continue by implementing the task list in dependency order.

## Task List

### Task 1: Lock Directional Docs Policy

**Description:** Record that `CONTEXT.md`, `docs/mutation-approval-profile.md`, and `docs/mutation-approval-flow.md` define the target direction for InfraGate. Implementation should catch up to them unless a target term or relationship is wrong.

**Acceptance criteria:**
- [ ] The directional-docs decision is recorded in this plan.
- [ ] `CONTEXT.md` remains glossary-only.
- [ ] The rest of this plan treats the verification findings as implementation gaps by default.

**Verification:**
- [ ] `rg -n "direction|target|Current Repository Fit|currently|implemented|canceled|challenge\\.canceled" .agents/Plans/2026-05-18-mutation-approval-drift-remediation-plan.md docs/mutation-approval-profile.md docs/mutation-approval-flow.md`

**Dependencies:** None

**Files likely touched:**
- `docs/mutation-approval-profile.md`
- `docs/mutation-approval-flow.md`
- `CONTEXT.md` only if canonical terminology changes

**Estimated scope:** Small

### Task 2: Implement Challenge Cancellation

**Description:** Add `canceled` as a real terminal **Challenge Outcome** for pending **Approval Challenges**.

**Acceptance criteria:**
- [ ] `ApprovalConventions.ChallengeStatuses.Canceled` and `ApprovalConventions.ChallengeOutcomeStatuses.Canceled` exist.
- [ ] A pending challenge can transition to canceled with a terminal `ChallengeOutcome` and no `ApprovalGrant`.
- [ ] Same-subject requester cancellation is available from the browser approval page.
- [ ] The `canceled` outcome supports a nullable actor subject so later system-initiated cancellation fits the same shape.
- [ ] A canceled challenge cannot later be approved or denied.
- [ ] Cancellation writes an audit event using the accepted audit naming strategy.
- [ ] Browser approval UI and/or service API exposes the agreed cancellation workflow.
- [ ] Tests cover successful cancellation, repeated cancellation, approval-after-cancel refusal, and no-grant behavior.

**Verification:**
- [ ] `rg -n "Canceled|canceled|challenge\\.canceled|approval_challenge_canceled" src tests docs CONTEXT.md`
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "FullyQualifiedName~GatewayApprovalServiceTests|FullyQualifiedName~GatewayApprovalEndpointsTests"`

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.Approvals/ApprovalConventions.cs`
- `src/InfraGate.Approvals/AuditPayloads/ChallengeAuditPayloads.cs`
- `src/InfraGate.McpGateway/McpGatewayConventions.cs`
- `src/InfraGate.McpGateway/GatewayApprovalService.cs`
- `src/InfraGate.McpGateway/GatewayApprovalEndpoints.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayApprovalServiceTests.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayApprovalEndpointsTests.cs`

**Estimated scope:** Medium

### Task 3: Move Audit Events Toward Audit Spine Names

**Description:** Change persisted InfraGate audit event constants toward the dot-separated **Audit Spine** names from the target docs and pin those values with tests.

**Acceptance criteria:**
- [ ] The plan does not change target docs to underscore names.
- [ ] `ApprovalConventions.AuditEvents` constants use dot-separated target names where the target event exists.
- [ ] Existing event concepts are mapped deliberately, including `apply_denied` to an execution-blocked-style target name and `plan_applied` to `execution.succeeded`.
- [ ] `challenge.canceled` is added with Task 2.
- [ ] Tests pin every retained audit event constant value.
- [ ] Operational docs that display persisted audit event names are updated to the new constants.

**Verification:**
- [ ] `rg -n "plan\\.created|challenge\\.created|challenge\\.approved|grant\\.issued|execution\\.started|execution\\.succeeded|plan_requested|approval_challenge_created|grant_issued|plan_applied" docs src tests`
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "FullyQualifiedName~Audit|FullyQualifiedName~GatewayApprovalServiceTests"`

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.Approvals/ApprovalConventions.cs`
- audit-related tests under `tests/`
- operational docs where persisted event names are shown

**Estimated scope:** Small

### Task 4: Implement Evidence Artifact Digest Coverage

**Description:** Implement target **Review Digest** coverage over **Evidence Artifact** digests or digest-bound references and redaction metadata before the larger ADR 0006 adapter-seam work.

**Acceptance criteria:**
- [ ] Target profile language continues to require digest-bound **Evidence Artifacts** and redaction metadata.
- [ ] A generic evidence-artifact summary type exists in the approval core and contains artifact type, digest, canonicalization, and redaction metadata/reference fields without owning Kubernetes semantics.
- [ ] `PlanEnvelopeFactory` review digest canonicalization includes evidence artifact summaries instead of hashing the raw adapter payload as the evidence binding.
- [ ] The Kubernetes adapter derives evidence artifact summaries for dry-run result, diff evidence, policy findings, and redaction metadata where applicable.
- [ ] Tests prove review digest changes when an artifact digest or redaction metadata changes.
- [ ] Tests prove intent digest does not change when review evidence changes.

**Verification:**
- [ ] `rg -n "Review Digest|Evidence Artifact|Redacted Evidence|redaction|payload|ComputeReviewDigest|evidence.*digest" CONTEXT.md docs src tests`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter "FullyQualifiedName~PlanEnvelopeFactoryTests|FullyQualifiedName~KubernetesApprovalAdapterTests"`

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.Approvals/PlanEnvelopeFactory.cs`
- new approval-core evidence artifact summary type(s)
- `src/InfraGate.KubernetesAdapter/KubernetesApprovalAdapter.cs`
- `src/InfraGate.KubernetesAdapter/KubernetesPlanPayload.cs` only if persisted envelope shape needs artifact summaries
- matching `PlanEnvelopeFactory` and Kubernetes adapter tests

**Estimated scope:** Medium

### Task 5: Replace `DomainPolicyCheck` With A Base Contract

**Description:** Preserve the generic hook for policy checks without moving concrete **Domain Policy Check** meaning into the **Generic Approval Core**.

**Acceptance criteria:**
- [ ] `DomainPolicyCheck` is no longer an unused concrete generic record.
- [ ] The approval core defines a base class or interface for cross-adapter policy-check shape only.
- [ ] Kubernetes policy findings inherit or implement the generic base contract while retaining Kubernetes-owned semantics.
- [ ] The base contract carries only fields that are meaningful across adapters, such as code, message, severity, and optional object/reference text.
- [ ] The chosen approach aligns with ADR 0001 and ADR 0006.

**Verification:**
- [ ] `rg -n "DomainPolicyCheck|K8sPlanPolicyFinding|Domain Policy Check" src tests docs CONTEXT.md`
- [ ] `dotnet build InfraGate.slnx`

**Dependencies:** Task 4 only if evidence/domain policy representation is implemented together

**Files likely touched:**
- `src/InfraGate.Approvals/DomainPolicyCheck.cs`
- `src/InfraGate.KubernetesAdapter/K8sPlanPolicyFinding.cs`
- adapter review/evidence tests if policy findings become part of artifact summaries

**Estimated scope:** Small

### Task 6: Deepen Pre-Execution Gate Orchestration

**Description:** Implement the ADR 0006 direction by deepening **Pre-Execution Gate** orchestration into a concrete generic module.

**Acceptance criteria:**
- [ ] The invariant remains: every required gate is evaluated immediately before mutation.
- [ ] Generic gate checks for grant validity, digest binding, plan validity, authorization, and reuse live behind a concrete approval-core module.
- [ ] Adapter-owned freshness and domain policy checks cross an explicit seam called by the generic gate module.
- [ ] The gateway execution path calls the generic gate module before any adapter execution.
- [ ] Kubernetes execution behavior remains adapter-owned after gates pass.
- [ ] Tests cover generic gate pass/fail behavior and adapter gate delegation.

**Verification:**
- [ ] `rg -n "Pre-Execution Gate|PreExecution|ValidateGrant|CheckLiveDrift|RunPreExecuteDryRun|orchestration" docs src tests`
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "FullyQualifiedName~GatewayToolDispatcherTests|FullyQualifiedName~PreExecution"`

**Dependencies:** Task 1

**Files likely touched:**
- Generic approval core files under `src/InfraGate.Approvals/`
- Gateway execution path files
- Kubernetes adapter executor files
- Matching tests

**Estimated scope:** Medium

### Task 7: Resolve Failure Audit Event Drift

**Description:** Make retained failure audit constants real by adding production writers and tests, and remove speculative failure constants that are not part of the target **Audit Spine** or adapter audit payloads.

**Acceptance criteria:**
- [ ] Every documented current audit event is written by code.
- [ ] Every retained failure audit constant has at least one production writer.
- [ ] Speculative failure constants with no target audit-spine or adapter-audit role are removed.
- [ ] Dry-run, diff, apply failure, and drift-detected paths are each classified as either retained-and-written or removed-with-doc cleanup.
- [ ] Tests cover every retained failure event write.
- [ ] Operational docs do not promise events that code does not emit.

**Verification:**
- [ ] `rg -n "apply_failed|dry_run_failed|diff_failed|apply_drift_detected" src tests docs`
- [ ] Focused gateway/server tests pass for changed paths.

**Dependencies:** Task 3

**Files likely touched:**
- `src/InfraGate.Approvals/ApprovalConventions.cs`
- Plan request/execution paths where failure events should be written
- Matching tests
- Docs that mention failure audit events

**Estimated scope:** Small to Medium

## Checkpoints

### Checkpoint A: After Tasks 1-3

- [ ] Directional-docs policy is settled.
- [ ] Challenge cancellation is implemented and covered by focused tests.
- [ ] Audit event naming has a convergence strategy toward the target **Audit Spine** names.

### Checkpoint B: After Tasks 4-6

- [ ] Review digest coverage is honest and testable.
- [ ] Generic/domain ownership is not contradicted by unused types.
- [ ] Pre-execution gate implementation either moves toward ADR 0006 or remains explicitly tracked as a gap.

### Checkpoint C: Complete

- [ ] `rg` stale-term checks are clean for the agreed target/current distinction.
- [ ] `git diff --check` passes.
- [ ] Relevant focused tests pass for any implementation changes.
- [ ] Mermaid diagrams are manually inspected if edited; rendered verification is stated only if actually performed.

## Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Treating target profile docs as current implementation docs | Medium | Treat verification findings as implementation gaps unless target terminology is wrong |
| Implementing cancellation before a real actor/workflow exists | Medium | Decide actor and workflow before writing code, or track as an explicit implementation gap |
| Renaming audit events breaks log consumers | Medium | Add compatibility or migration strategy before changing persisted event values |
| Papering over review digest coverage weakens safety claims | High | Keep target requirement intact; implement or sequence explicit evidence artifact digest coverage |
| Adding generic policy abstractions too early | Medium | Apply deletion test; remove unused generic records unless a real adapter seam needs them |

## Next Step

Begin implementation at Task 2 and Task 3 together only if the audit-event rename and cancellation audit event can be kept in one focused slice. Otherwise implement Task 2 first, then Task 3.
