# Mutation Evidence-Parity Contract

**Status:** Reference contract. Current assessment against the sole known candidate (`containers/kubernetes-mcp-server`) is **`no-go`** — see [§4](#4-no-go-determination-and-the-no-shim-rule).

This document is the reviewable matrix and conformance-test specification required before any change routes Kubernetes mutations somewhere other than `InfraGate.McpServer` → `InfraGate.KubernetesAdapter`. It exists because [ADR-0021](adr/0021-mcpserver-local-dto-copies-over-shared-contracts.md) and [ADR-0033](adr/0033-kubernetes-mcp-server-readonly-secondary-downstream.md) both condition any future replacement on "evidence parity" without pinning down, operation by operation, what that means. This document pins it down.

## Scope

For every mutation operation InfraGate exposes today (create/update via apply, delete, scale, restart, set-image), this contract enumerates:

- the required input,
- the preview/diff evidence produced before approval,
- the freshness evidence re-checked immediately before execution,
- how the operation binds to an Approval Grant,
- the execution output,
- the audit events it must emit, and
- the negative tests that must pass.

It also gives dedicated treatment to six cross-cutting properties that are not specific to any one operation: resourceVersion freshness, canonical plan digest binding, approval grant verification, audit identity/events, failure semantics, and rollback behavior.

## Non-Goals

This document does **not**:

- authorize routing any mutation to `kubernetes-mcp-server` or any other downstream — that remains a separate, explicitly human-approved plan per ADR-0033 point 9;
- resolve ADR-0033's open blocker (whether upstream's generic `apply` supports a dry-run mode `KubernetesPlanBuilder` can consume);
- specify capability negotiation, rollout, or rollback-of-the-routing-decision-itself — that is "Future Checkpoint I: Replacement Gate" in `.agents/Plans/2026-08-09-kubernetes-mcp-server-integration-hardening.md`, which is explicitly out of scope for this plan's completion ledger;
- change any production code. Every capability below already exists in `InfraGate.KubernetesAdapter` / `InfraGate.Approvals` and is cited to its current implementation.

## How This Contract Is Organized

The mutation-approval architecture splits into a domain-agnostic **Generic Approval Core** (`InfraGate.Approvals`) and a Kubernetes-specific **Domain Adapter** (`InfraGate.KubernetesAdapter`), per the Ownership diagram in [`docs/mutation-approval-flow.md`](mutation-approval-flow.md). A replacement downstream only has to satisfy the adapter side — `IDomainPlanExecutor` (`src/InfraGate.Approvals/Execution/IDomainPlanExecutor.cs`) and the plan-building contract `KubernetesPlanBuilder` currently implements. The generic core (Gates 1–6 of the Pre-Execution Gate Flow) does not change regardless of which downstream produces evidence.

The 8-gate Pre-Execution Gate Flow, and its ownership split, is documented in `mutation-approval-flow.md`'s "Pre-Execution Gate Flow" section:

- **Gates 1–6** (grant validity, plan validity window, authorization/requester-subject match, intent digest match, review digest match, execution reuse policy) — owned by the generic core, implemented identically for every operation in `ApprovalGrantValidation.Validate` (`src/InfraGate.Approvals/Grant/ApprovalGrantValidation.cs`). Nothing here is replaceable per-operation; a candidate downstream inherits this unchanged.
- **Gates 7–8** (freshness policy checks, domain policy checks) — owned by `KubernetesPlanExecutor.CheckPreExecutionAsync` (`src/InfraGate.KubernetesAdapter/Execution/KubernetesPlanExecutor.cs:34-115`). This is where operation-specific evidence parity actually has to be proven, and where the matrix below is anchored.

`CheckPreExecutionAsync` runs five sub-checks in a fixed order, each capable of blocking execution before any mutation call is dispatched:

1. `CheckLiveDriftAsync` (line 174) — only if `FreshnessPolicy` declares `kubernetes.live-drift`.
2. `CheckResourceVersionAsync` (line 147) — only if `FreshnessPolicy` declares `kubernetes.resource-version`.
3. `CheckStoredPolicyFindings` (line 263) — blocks if any policy finding stored on the payload has `Severity == "Deny"`.
4. `CheckSetDeploymentImagePolicy` (line 242) — Set-Image only; re-validates the target image tag against `KubernetesPolicyValidator.ValidateSetDeploymentImage` at execution time, not just at plan-build time.
5. `RunPreExecuteDryRunAsync` (line 202) — re-runs the operation's dry-run tool immediately before dispatch.

Any candidate downstream's `CheckPreExecutionAsync` equivalent must reproduce this exact ordering and blocking behavior, not just "some freshness check."

## 1. Operation-by-Operation Evidence-Parity Matrix

Freshness-check composition is built in `KubernetesBuilderInfrastructure.BuildFreshnessPolicy` (`src/InfraGate.KubernetesAdapter/PlanBuilding/KubernetesBuilderInfrastructure.cs:33-56`) from two base sets — `manifestFreshnessChecks` = `[LiveDrift, PreExecuteDryRun]` (Apply/Delete) and `deploymentFreshnessChecks` = `[PreExecuteDryRun]` (Scale/Restart/SetImage) — plus a conditional `ResourceVersionCheck` appended whenever the plan's diffs carry a resource/stability version (i.e. the target object already exists).

| Operation | Mutation Tool | Dry-Run Evidence Tool | Diff Evidence Tool | Freshness Checks Declared | Domain Policy Gate | Audit Events (chronological) | Block Reason Codes | Primary Test Coverage |
|---|---|---|---|---|---|---|---|---|
| **Apply** (create/update) | `apply_manifest` | `dry_run_apply_manifest` (via `evidenceService.GetApplyEvidenceAsync` / `CheckApplyDryRunAsync`) | `diff_manifest` | `LiveDrift` + `PreExecuteDryRun` always; `+ResourceVersionCheck` when the target object already exists (update, not create) | Manifest policy validated at request time (`ApplyManifestBuilder.CheckManifestPolicy`); stored findings re-checked at Gate 7 | `plan.created` → `pre_execution.grant.validated` → `pre_execution.checked` → `execution.started` → `execution.succeeded`/`execution.blocked` | `MissingArguments`, `PolicyBlocked`, `LiveDrift`, `ResourceVersionMismatch`, `PreExecuteDryRunFailed`, `DiffEvidenceEmpty`/`Failed` | `ApplyManifestBuilderTests` (`BuildAsync_ApplyManifest_*`), `KubernetesPlanExecutorTests`, `FullApprovalFlowTests`, `ReviewDigestMismatchTests`, `AlreadyAppliedPlanTests`, `DangerousManifestTests` |
| **Delete** | `delete_manifest` | `dry_run_delete_manifest` | `diff_manifest` | `LiveDrift` + `PreExecuteDryRun` always; `+ResourceVersionCheck` (target always pre-exists for delete) | Same manifest-policy path as Apply | Same spine as Apply | Same as Apply, minus create-specific cases | `DeleteManifestBuilderTests` (`BuildAsync_DeleteManifest_*`), `KubernetesPlanExecutorTests` |
| **Scale** | `scale_deployment` | `dry_run_scale_deployment` | `diff_deployment` | `PreExecuteDryRun` always; `+ResourceVersionCheck` (deployment always pre-exists) — **no `LiveDrift`** | No manifest to validate; stored findings re-check still applies (Gate 7 step 3) | Same spine as Apply | `ResourceVersionMismatch`, `PreExecuteDryRunFailed`, `PolicyBlocked` (stored findings), `UnsupportedOperation` | `ScaleDeploymentBuilderTests`, `KubernetesPlanExecutorTests` (`CheckPreExecutionAsync_LiveDriftNotInFreshnessPolicy_SkipsDriftCheck` pins the missing-LiveDrift behavior) |
| **Restart** | `restart_deployment` | `dry_run_restart_deployment` | `diff_deployment` | Same as Scale | Same as Scale | Same spine as Apply | Same as Scale | `RestartDeploymentBuilderTests`, `KubernetesPlanExecutorTests` |
| **Set-Image** | `set_deployment_image` | `dry_run_set_deployment_image` | `diff_deployment` | Same as Scale | Same as Scale, **plus** an execution-time re-check of the target image tag (`CheckSetDeploymentImagePolicy`, Gate 7 step 4) — the only operation with a second, execution-time policy gate | Same spine as Apply | `PolicyBlocked` (both build-time and execution-time), `ResourceVersionMismatch`, `PreExecuteDryRunFailed` | `SetDeploymentImageBuilderTests`, `KubernetesPlanExecutorTests` (`CheckPreExecutionAsync_SetDeploymentImageLatestImageTag_BlocksWithoutDryRun` — proves the live-tag re-check blocks *before* the dry-run call even runs) |

**Every operation, without exception, requires:**
- an Approval Challenge → Approval Grant obtained through the generic core before `ExecuteAsync` is reachable (Gates 1–6);
- a `Requester` bound into the `PlanEnvelope` at build time and re-verified against the grant's `RequesterSubject` at Gate 3;
- `execution.started` emitted before the mutation tool is dispatched, and exactly one of `execution.succeeded` / `execution.blocked` / `execution.failed` emitted after (`KubernetesPlanExecutor.ExecuteAsync`, lines 117-145);
- `ExecutionReusePolicy.SingleExecution` enforced generically — a second `execute_approved_plan` call against an already-`Applied` plan is refused with `PlanAlreadyApplied` before it reaches the adapter at all.

A candidate downstream that cannot reproduce every cell in this table for every operation — not "most operations" or "the common case" — fails evidence parity for that operation.

## 2. Cross-Cutting Requirements

### 2.1 resourceVersion Freshness

`kubernetes.resource-version` is one of three `FreshnessCheckTypes` (`src/InfraGate.KubernetesAdapter/KubernetesAdapterConventions.cs:20-25`, alongside `LiveDrift` and `PreExecuteDryRun`). It is only declared when the plan's diffs carry a `StabilityVersion` or `ResourceVersion` (`KubernetesBuilderInfrastructure.BuildFreshnessPolicy`, lines 37-42) — i.e. whenever the operation targets a pre-existing object. At Gate 7, `CheckResourceVersionAsync` calls `check_resource_version` with the per-object versions captured at plan-build time and blocks with `ResourceVersionMismatch` unless the tool returns exactly `"ok"` (`DriftCheckResults.NoDrift`). This is the mechanism that prevents "approve now, apply later, after someone else already changed the object" — a candidate must reproduce both halves: capturing the version at build time *and* re-verifying it at execution time, not just one or the other.

### 2.2 Canonical Plan Digest Binding

Two digests are computed and bound into every plan, using versioned canonicalization identifiers (`ApprovalConventions.Canonicalizations` and `KubernetesAdapterConventions.Canonicalizations`):

| Digest | Canonicalization ID | Computed From | Verified At |
|---|---|---|---|
| Intent digest (generic) | `infra-gate.approval.plan-envelope.v1` | The `PlanEnvelope` itself | Gate 4 |
| Review digest (generic) | `infra-gate.approval.review.v1` | The rendered review surface shown to the approver | Gate 5, via `PlanEnvelopeFactory.ComputeReviewDigest(envelope)` recomputed and compared at grant-validation time |
| Kubernetes intent | `infra-gate.kubernetes.intent.v1` | The decoded Kubernetes-specific plan payload | Adapter decode (`KubernetesApprovalAdapter.Decode`) |
| Dry-run evidence | `infra-gate.kubernetes.evidence.dry-run.v1` | Dry-run tool output | Evidence capture at plan-build time |
| Diff evidence | `infra-gate.kubernetes.evidence.diff.v1` | Diff tool output | Evidence capture at plan-build time |
| Policy-findings evidence | `infra-gate.kubernetes.evidence.policy-findings.v1` | Stored policy findings | Evidence capture at plan-build time; re-verified at Gate 7 step 3 |

Digest comparison uses `FixedTimeStringComparer.Equals` (timing-safe) and returns `DigestChanged` on mismatch (`ApprovalConventions.ResultReasonCodes.DigestChanged`). A candidate downstream must produce byte-identical canonical serialization for whatever it substitutes as intent/evidence, or digest binding silently stops catching tampering — this is not negotiable partial credit.

### 2.3 Approval Grant Verification

`ApprovalGrant.RequesterSubject` / `ApproverSubject` are checked against the envelope's requester and, for `ApprovalPolicyTypes.SameSubject`, against each other (`ApprovalGrantValidation.Validate`). `GatewayAuditIdentityResolver.Resolve(ClaimsPrincipal)` derives audit identity from OAuth claims — known service clients (Observer/Planner/Executor) resolve via `azp` to `service:*` subjects; human identities fall back through `sub` → `client_id` → `preferred_username` → `email`. This identity derivation is entirely generic-core and adapter-independent; a replacement changes nothing here, but every operation's audit payload must still carry a resolvable identity end to end (see 2.4).

### 2.4 Audit Identity and Events

The full Audit Spine (`ApprovalConventions.AuditEvents`, `src/InfraGate.Approvals/ApprovalConventions.cs:92-115`) is generic and dot-separated:

`plan.created`, `pre_execution.grant.validated`, `pre_execution.checked`, `execution.started`, `execution.succeeded`, `execution.blocked` (the single event name shared by `ApplyDenied`/`DryRunFailed`/`DiffFailed`/`ApplyDriftDetected` — deliberately, per the `NOSONAR:S1192` comment centralizing that literal), `execution.failed`, plus the six challenge-lifecycle events (`challenge.created`/`.approved`/`.denied`/`.expired`/`.rejected`/`.canceled`) and `grant.issued`.

Every payload is a typed record implementing `IPlanAuditPayload` or `IChallengeAuditPayload` (marker interfaces, not abstract base classes, specifically to preserve JSON field-serialization order) in `src/InfraGate.Approvals/Audit/Payloads/{Plan,Challenge}AuditPayloads.cs`. `AuditPayloadsTests` pins the field set per payload with an explicit comment warning that renames are breaking schema changes. A candidate downstream's adapter payloads (the Kubernetes-specific portion nested inside the generic payload, e.g. `KubernetesPreExecutionCheckedAdapterPayload`, `KubernetesExecutionStartedAdapterPayload`) must satisfy the same pinning discipline — evidence parity includes audit *schema* parity, not just "an audit log exists."

### 2.5 Failure Semantics

Failure is modeled per-gate, not as one generic "operation failed":

- Gates 1–6 failures never reach the adapter — they short-circuit in `ApprovalGrantValidation.Validate` with a generic reason code (`GrantExpired`, `InvalidGrant`, `DigestChanged`, `PlanAlreadyApplied`, etc. — the 19 codes in `ApprovalConventions.ResultReasonCodes`).
- Gate 7–8 failures are adapter-specific (`KubernetesAdapterConventions.ResultReasonCodes` — 14 codes covering dry-run, diff, drift, policy, and operation-support failures) and always block *before* dispatch — no partial mutation is possible from a Gate 7/8 failure.
- A failure *during* dispatch (`DispatchMutationAsync`, after `execution.started` has already been emitted) is the one case that is not a clean block — `IsUnsupportedOperationMessage` is the only currently-modeled post-dispatch failure path, and it maps to `UnsupportedOperation`, not a generic exception-to-reason-code contract. Retry semantics on a failed *mutation call itself* (as opposed to a failed *pre-execution check*) are undefined generically — this is explicitly domain-adapter-owned behavior today, and a candidate must document what it does here, not silently inherit InfraGate's behavior by assumption.

### 2.6 Rollback Behavior

**No rollback capability exists today.** This was confirmed by exhaustive search: `KubernetesPlanExecutor.ExecuteAsync` → `DispatchAsync` → `DispatchMutationAsync` is one-shot — it calls the mutation tool once and returns whatever message comes back. There is no compensating/undo action anywhere in the mutation-execution path. Every "rollback" reference in the ADRs, the plan, and `docs/mutation-approval-flow.md`'s Rollback Guidance section describes rollback as **a requirement a future replacement must prove**, not existing InfraGate behavior — e.g. ADR-0033's evidence-gate clause lists "rollback proof" alongside dry-run/diff/freshness/approval/audit/failure-semantics as one of the dimensions whose absence keeps the result `no-go`. `ExecutionReusePolicy.SingleExecution` only prevents *replaying* an already-successful execution; it is not a rollback mechanism.

This means the correct evidence-parity bar for rollback is **parity with "none," proven equally absent, not silently assumed** — a candidate does not need to invent rollback InfraGate itself lacks, but any claim of rollback capability in a candidate must be scrutinized as a *new* capability requiring its own review, not something this contract already validates.

## 3. Conformance Test Specification

A candidate downstream release must pass a conformance suite exercising **real Kubernetes and real OAuth/approval infrastructure** — no mocks, no compatibility shim standing in for missing behavior. The suite structure to replicate is the one `InfraGate.Safety.E2E.Tests` already uses for the current implementation (`tests/InfraGate.Safety.E2E.Tests/README.md`, "What it covers"):

| Safety property (existing, McpServer-proven) | What a candidate must independently prove |
|---|---|
| `FullApprovalFlowTests` — request → browser approval → apply mutates Kubernetes only after approval, writes approval/applied audit evidence | Same flow, same audit event sequence, through the candidate's tool surface |
| `ReviewDigestMismatchTests` — tampering the pending plan after approval prevents grant use | Digest recomputation and mismatch detection reproduced exactly (§2.2) |
| `ExpiredApprovalTests` — expired challenge refused with a stable reason code | Unaffected — generic core; candidate must not be able to bypass it |
| `AlreadyAppliedPlanTests` — second apply of the same plan is refused | `ExecutionReusePolicy.SingleExecution` unaffected — generic core |
| `DangerousManifestTests` — privileged-container manifest rejected before a pending plan is even created | Candidate's build-time policy gate must reject the same `PolicyCodes` set (`DEPLOYMENT_PRIVILEGED_CONTAINER`, `DEPLOYMENT_HOST_PATH`, `DEPLOYMENT_HOST_NAMESPACE`, `DEPLOYMENT_ADDED_CAPABILITIES`, `IMAGE_LATEST_TAG`, `SERVICE_LOAD_BALANCER`, `SERVICE_NODE_PORT`, `CONFIG_MAP_SECRET_LIKE_KEY`) |
| `ModifiedPendingPlanTests` — pending-plan tamper detected at approve time | Generic core — unaffected |
| `WrongUserApprovalTests` — challenge created by A cannot be approved by B | Generic core (§2.3) — unaffected |
| `DryRunFailureTests` — strict-validation dry-run failure blocks both plan creation and pre-apply execution | Candidate must fail closed at **both** points, matching Gate 7 step 5 (`RunPreExecuteDryRunAsync`) — a candidate that only validates at build time and not immediately pre-execution fails parity |
| `RbacMatrixTests` — a read-only ServiceAccount cannot complete a mutation; API returns 403 | Candidate must inherit RBAC identically — this proves defense-in-depth independent of the approval layer |

Plus, net-new for a candidate (properties the current suite doesn't need to prove because there's only one implementation today):

1. **Per-operation dry-run/diff artifact equivalence** — for each of the five operations in §1, the candidate's dry-run and diff tool output must canonicalize to a digest-comparable evidence artifact (§2.2), not merely "return something truthy."
2. **Freshness-check declaration parity** — for each operation, the candidate's plan-build step must declare exactly the freshness checks listed in §1's matrix (no fewer — declaring fewer silently weakens Gate 7).
3. **Audit payload schema parity** — golden-file comparison against `PlanAuditPayloads.cs` / `ChallengeAuditPayloads.cs` field sets, the same discipline `AuditPayloadsTests` already enforces for the current implementation.
4. **Negative-path completeness per operation** — each row of §1's matrix needs its own `PolicyBlocked`, `ResourceVersionMismatch`/`LiveDrift`, and `PreExecuteDryRunFailed` (or operation-specific equivalent) test, mirroring `KubernetesPlanExecutorTests` and the per-operation `*BuilderTests` files in `tests/InfraGate.KubernetesAdapter.Tests/`.
5. **Rollback declaration** — an explicit statement of what the candidate does and does not support, reviewed against §2.6's "parity with none" bar.

A run that produces this evidence outside real Kubernetes/OAuth infrastructure, or that skips any of the above with CI remaining green, does not satisfy this contract — this is the same failure mode the hardening plan's own risk table calls out ("CI remains green because real integration is skipped").

## 4. No-Go Determination and the No-Shim Rule

**The current assessment is `no-go`.** ADR-0033's own open question is a precondition this contract cannot even begin to evaluate against a concrete candidate: whether `kubernetes-mcp-server`'s generic `apply` supports a dry-run mode `KubernetesPlanBuilder` could consume. Until that's answered for a specific pinned, checksum-verified release, no row of §1's matrix can be marked satisfied for that candidate.

More generally, per ADR-0021 and ADR-0033 point 9 (near-verbatim in both): **any missing operation, preview/diff, freshness evidence, approval binding, audit behavior, failure semantics, or rollback proof keeps the result `no-go`.** This is a hard floor, not a weighted score — a candidate that satisfies 4 of 5 operations, or satisfies an operation's mutation call but not its diff evidence, is `no-go` for that operation, full stop.

**No compatibility shim is permitted to bypass this.** Concretely: no code path may synthesize a passing dry-run/diff/freshness result when the candidate doesn't actually produce one, no fallback may silently skip Gate 7/8 sub-checks when the candidate's evidence is absent or malformed, and no wrapper may fabricate an audit payload field to satisfy schema pinning without the candidate having genuinely produced that data. The existing pre-execution gate order (§ "How This Contract Is Organized") must run unmodified against genuine candidate evidence — the gate does not get relaxed to accommodate a candidate's gaps.

A `go` result requires: (a) every cell of §1's matrix independently verified against a specific pinned, checksum-verified release; (b) the full conformance suite in §3 passing against real Kubernetes and real OAuth/approval infrastructure with non-skipped output; and (c) explicit human approval, starting a new, separately reviewed plan with its own rollout and rollback steps for the *routing change itself* — this contract authorizes none of that on its own.

## 5. Cross-References

- [ADR-0021](adr/0021-mcpserver-local-dto-copies-over-shared-contracts.md) — records the local-DTO-copy architecture and the original evidence-parity requirement text this contract expands into a matrix.
- [ADR-0033](adr/0033-kubernetes-mcp-server-readonly-secondary-downstream.md) — the current read-only-secondary-downstream decision; point 9 ("Evidence gate") is the second source of the requirement text, and its open dry-run-mode question is this contract's current blocker.
- [`docs/mutation-approval-flow.md`](mutation-approval-flow.md) — the Ownership diagram, Object Flow diagram, and authoritative 8-gate Pre-Execution Gate Flow this contract's §1/§2 are anchored to.
- [`docs/mutation-approval-profile.md`](mutation-approval-profile.md) — canonical term definitions for the approval-lifecycle vocabulary used throughout this contract.
- `tests/InfraGate.Safety.E2E.Tests/README.md` — the seven safety properties and test-tier architecture §3 requires a candidate to reproduce.
- `.agents/Plans/2026-08-09-kubernetes-mcp-server-integration-hardening.md`, Task 21 — the task this document satisfies.
