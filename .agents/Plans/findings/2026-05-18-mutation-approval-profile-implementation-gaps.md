# Mutation Approval Profile: Documentation vs. Implementation Gap Analysis

**Date**: 2026-05-18
**Scope**: CONTEXT.md, docs/mutation-approval-profile.md, docs/mutation-approval-flow.md compared against all 111 `.cs` files across 6 source projects.

---

## Methodology

Every concept, relationship, and flow described in `CONTEXT.md`, `mutation-approval-profile.md`, and `mutation-approval-flow.md` was compared against the actual C# source code across all 6 projects (`src/InfraGate.Approvals`, `src/InfraGate.McpServer`, `src/InfraGate.McpGateway`, `src/InfraGate.KubernetesAdapter`, `src/InfraGate.RuntimeSafety`, `src/InfraGate.Observability`).

---

## Fully Implemented (aligned with docs)

| Concept | Where implemented | Evidence |
|---|---|---|
| **Plan Envelope** (generic wrapper with all fields) | `PlanEnvelope.cs` | All profile fields present: id, profile, adapterId, operation, createdAtUtc, validFromUtc, validUntilUtc, requester, approvalPolicy, executionReusePolicy, freshnessPolicy, reviewSurfaceContext, evidenceArtifacts, intentDigest, reviewDigest, payload |
| **Plan Identifier** (opaque) | `ApprovalStore.NewPlanId()` | Random 32-hex-char identifier |
| **Intent Digest** (SHA-256, adapter-defined canonicalization) | `ApprovalDigest.cs`, `KubernetesApprovalAdapter.ComputeIntentDigest()` | Algorithm + canonicalization + value |
| **Review Digest** (SHA-256, profile-defined canonicalization) | `PlanEnvelopeFactory.ComputeReviewDigest()` | Covers all required fields including intent digest, evidence artifacts, policies, etc. |
| **Approval Policy** (same-subject only) | `ApprovalPolicy.SameSubject()` | Enforced in `ValidateGrant()` and in `GatewayApprovalService` |
| **Execution Reuse Policy** (single-execution default) | `ExecutionReusePolicy.SingleExecution()` | Enforced by moving plan from pending to applied directory |
| **Plan Validity Window** | `PlanValidityWindow`, `PlanEnvelopeFactory` | 1-hour default window, validated in `ValidateGrant()` |
| **Freshness Policy & Checks** | `FreshnessPolicy.cs`, `FreshnessCheck.cs` | Adapter-defined checks: `kubernetes.live-drift`, `kubernetes.pre-execute-dry-run` |
| **Approval Challenge** (bound to plan, TTL, status) | `ApprovalChallenge.cs`, `ApprovalChallengeStore` | All challenge lifecycle states implemented |
| **Challenge TTL** | `McpGatewayOptions.ApprovalChallengeTtl` | Configurable, enforced in `ValidatePendingChallengeAsync()` |
| **Challenge Outcome** (approved, denied, rejected, expired, canceled) | `ChallengeOutcome.cs`, `GatewayApprovalService` | All 5 outcomes written to challenges and audit |
| **Approval Grant** (bound to planId, requester, approver, digests, policy, expiry, reuse) | `ApprovalGrant.cs`, `ApprovalStore.CreateGrantAsync()` | All binding fields present and validated at execution |
| **Pre-Execution Gates** | `ApprovalPreExecutionGate.cs` | Evaluates grant validity, delegates to domain adapter for freshness/domain checks |
| **Evidence Artifacts** (dry-run, diff, policy findings) | `EvidenceArtifactSummary.cs`, `KubernetesApprovalAdapter.BuildEvidenceArtifacts()` | 3 artifact types with digests, references, and redaction metadata |
| **Review Surface** (gateway browser HTML page) | `GatewayApprovalEndpoints.cs` | HTML approval page with approve/deny/cancel forms, renders plan summary, review content, diffs |
| **Review Surface Context** | `ReviewSurfaceContext.cs` | Surface + Renderer fields |
| **Canonicalization** | `CanonicalJson.cs` | Deterministic JSON canonicalization (sorted keys) |
| **Audit Spine events** | `ApprovalConventions.AuditEvents`, `PlanAuditPayloads.cs`, `ChallengeAuditPayloads.cs` | 12 of 13 audit events implemented |
| **Audit Trail** (audit.jsonl) | `ApprovalStore.WriteAuditAsync()` | All lifecycle events written to structured JSONL |
| **Domain Adapter interfaces** (IDomainPlanBuilder, IDomainPlanExecutor, IPlanReview, IPlanReviewAdapter, IPlanReviewRenderer) | 5 interfaces in `InfraGate.Approvals/` | Seam boundary between generic core and adapter |
| **Kubernetes Adapter** | `KubernetesPlanBuilder.cs`, `KubernetesPlanExecutor.cs`, `KubernetesApprovalAdapter.cs` | Owns mutations (apply, delete, scale, restart, set-image), evidence collection, freshness checks, execution |
| **Generic Approval Core** | `PlanEnvelope.cs`, `ApprovalStore.cs`, `ApprovalChallengeStore.cs`, `ApprovalPreExecutionGate.cs` | Owns envelope, lifecycle, digests, challenges, outcomes, grants, audit spine |
| **Approval Authority** | `GatewayApprovalService.cs` | Creates challenges, enforces policy, records outcomes, issues grants |
| **Requester binding** | `PlanRequester` in envelope | Recorded at plan creation, validated at approval and execution |
| **Same-Subject Approval** | `GatewayApprovalService.cs` (multiple places) | Enforced at challenge validation, approval, deny, cancel |
| **Domain Policy Checks** | `K8sPolicyValidator.cs`, policy findings in `K8sApplyEvidence` | Kubernetes-specific policy checks during plan building |
| **Approval-bound execution** | `GatewayToolDispatcher.HandleApplyApprovedPlanAsync()` | Grant + all gates must pass before ExecuteAsync |

---

## Partially Implemented / Gaps

### 1. `execution.started` audit event — MISSING (High)

**Profile says** (mutation-approval-profile.md:160): `- `execution.started`` is a required audit spine event.

**What's implemented**: The `ApprovalConventions.AuditEvents` class has 12 events but **no `execution.started` constant**. The audit flow goes: pre-execution gates -> `ExecuteAsync()` -> success writes `execution.succeeded` / failure writes `execution.failed`. There is no "execution started" event written before `ExecuteAsync()`.

**Location**: `GatewayToolDispatcher.cs:214-264` -- execution proceeds directly to `planExecutor.ExecuteAsync()` without writing a `execution.started` audit event.

---

### 2. Redacted Evidence -- DEFINED BUT NEVER POPULATED (Medium)

**Profile says** (CONTEXT.md:130): "Plan Evidence that intentionally hides sensitive parts of a Mutation Intent while disclosing that hiding to the Approver."

**What's implemented**: `EvidenceArtifactSummary` has a `RedactionMetadata` field (`Dictionary<string, string>`). The `PlanEnvelopeFactory.ComputeReviewDigest()` includes redaction metadata in the review digest. Tests verify redaction metadata changes the review digest.

**Gap**: The Kubernetes adapter's `BuildEvidenceArtifacts()` **always passes `[]` (empty dictionary)** for every evidence artifact (lines 147, 158, 167 of `KubernetesApprovalAdapter.cs`). There is no production code path that populates non-empty redaction metadata.

**Note**: The `PromptInjectionGuard` does redact prompt-injection content from MCP responses, but this is a *guardrail feature*, not the profile's *Redacted Evidence* concept (which is about domain adapter hiding sensitive mutation details while disclosing the hiding).

---

### 3. Domain Policy Checks NOT re-verified during Pre-Execution Gates -- GAP (High)

**Profile says** (mutation-approval-flow.md:193): The pre-execution gate flow explicitly includes `domain[Run required Domain Policy Checks]` as a separate gate step after freshness checks.

**What's implemented**: `KubernetesPlanExecutor.CheckPreExecutionAsync()` only runs:
1. `CheckLiveDriftAsync()` (freshness: live drift)
2. `RunPreExecuteDryRunAsync()` (freshness: pre-execute dry-run)

It does **NOT** re-run domain policy checks. Policy checks in the Kubernetes adapter happen during **plan building** (in `KubernetesPlanBuilder`, the dry-run evidence call returns `PolicyBlocked`/`PolicyFindings`), but they are not re-verified immediately before execution.

**Implication**: If Kubernetes admission policies change between plan creation and execution (e.g., a new OPA constraint), the execution could succeed even though it would now violate policy. The profile mandates re-checking domain policy at pre-execution time.

---

### 4. Multiple concurrent Approval Challenges per Plan -- PARTIALLY IMPLEMENTED (Low)

**Profile says** (CONTEXT.md:203): "A Plan Envelope may produce one or more Approval Challenges" and (mutation-approval-flow.md:158): "A new challenge may be created for the same plan envelope only while the plan validity window and approval policy allow it."

**What's implemented**: `GatewayApprovalService.EnsureApprovedOrCreateChallengeAsync()` creates a new challenge if no grant exists. The `FindApprovedAsync()` scans all challenges for an approved one. However:
- There is **no prevention of multiple concurrent pending challenges** for the same plan
- If a challenge was already created and is still pending, calling `execute_approved_plan` again will create a **second** pending challenge
- No deduplication logic exists

**Implication**: A clumsy AI agent could create dozens of pending challenges for the same plan, cluttering the challenge store and creating confusion.

---

### 5. Reusable Plans -- NOT IMPLEMENTED (as documented)

**Profile says** (mutation-approval-profile.md:144): "Reusable plans are an explicit future extension point. They must opt in through an execution reuse policy."

**What's implemented**: Only `ExecutionReusePolicy.SingleExecution()` exists. No `ReusablePlan` type, no configurable execution count, no opt-in mechanism.

**Assessment**: This is explicitly documented as a future extension point, so it's a deliberate gap, not an omission. Still, the profile document could be clearer that this is currently NOT implemented.

---

### 6. Delegated / Multi-party Approval Policies -- NOT IMPLEMENTED (as documented)

**Profile says** (mutation-approval-profile.md:123-124): "Other approval policies, such as delegated approval or multi-party approval, are future extension points."

**What's implemented**: Only `ApprovalPolicy.SameSubject()`. The `IsSupportedPolicy()` validation in `ApprovalStore` rejects any non-same-subject policy as an "old approval file format."

**Assessment**: Like Reusable Plans, this is explicitly called out as a future extension. Aligned with docs.

---

### 7. Authorization Check -- NO DISTINCT TYPE (Low)

**Profile says** (CONTEXT.md:157-158): "Authorization Check: A separate decision that an actor or system is permitted to request or execute a class of operation." (CONTEXT.md:217): "An Authorization Check is separate from an Approval Policy."

**What's implemented**: The code has OAuth-based authentication (JWT validation via `GatewayApprovalIdentityResolver`) that gates both plan creation (`HandleRequestMutationAsync`) and execution (`EnsureApprovedOrCreateChallengeAsync`). However, there is **no distinct `AuthorizationCheck` type, class, or interface** in the codebase. The auth check is implicitly done through the OAuth pipeline.

**Assessment**: The *function* of authorization checking exists, but the *concept* is not formalized as a separate typed concern distinct from `ApprovalPolicy`.

---

### 8. Retry semantics for failed execution -- NOT IMPLEMENTED (Low)

**Profile says** (CONTEXT.md:240): "A Domain Adapter owns retry semantics for non-successful Execution Attempts."

**What's implemented**: `KubernetesPlanExecutor.ExecuteAsync()` calls the downstream mutation tool once. If it fails, no retry is attempted. The `DispatchAsync` method treats any non-unsupported-operation result as "success" (line 189). No partial failure, idempotency check, or retry logic exists.

---

### 9. Profile-level execution.started audit mapping inconsistency (Low)

**Current state**: `execution.succeeded` maps to `PlanApplied` constant, but the variable name used is `PlanApplied` while the event name string is `execution.succeeded`. Similarly, `ApplyDenied`, `DryRunFailed`, `DiffFailed`, and `ApplyDriftDetected` all map to the string `execution.blocked`, but they're separate C# constants. This is a naming/convention issue rather than a functional gap -- the audit event strings are correct per the profile, but the C# constant names are legacy.

---

### 10. K8sReviewReviewRenderer does not surface RedactionMetadata (Very Low)

The `KubernetesPlanReviewRenderer` renders the plan in HTML for the human approver. It renders dry-run results, diffs, policy findings, and the manifest -- but **never mentions or displays redaction metadata**. If an evidence artifact was redacted, the approver would not see the redaction disclosures. However, since the Kubernetes adapter never actually populates redaction metadata (see gap #2), this is currently moot.

---

## Summary Table

| # | Gap | Severity | Profile Requirement | Code Reality |
|---|---|---|---|---|
| 1 | `execution.started` audit event | Missing | Required audit spine event | Never emitted |
| 2 | Redacted Evidence population | Never used | Adapter should populate when hiding sensitive data | Always passes empty dict |
| 3 | Domain Policy re-check in pre-execution gates | Missing | Must re-verify domain policy before execution | Only freshness checks run |
| 4 | Multiple concurrent challenge deduplication | Partial | Allows multiple challenges | Creates duplicates silently |
| 5 | Reusable Plans | Documented future | Explicitly deferred | Not implemented (by design) |
| 6 | Delegated/Multi-party Approval | Documented future | Explicitly deferred | Not implemented (by design) |
| 7 | AuthorizationCheck as distinct type | Implicit only | Separate concept from ApprovalPolicy | No typed representation |
| 8 | Execution retry semantics | Not implemented | Adapter-owned retry | Single attempt only |
| 9 | K8sPlanReviewRenderer doesn't show redaction | Very low | Approver should see redaction disclosures | Not shown (moot given gap #2) |

---

## Remediation Priority

1. **Add `execution.started` audit event** at the beginning of `HandleApplyApprovedPlanAsync()` in `GatewayToolDispatcher.cs`, right after pre-execution gates pass and before `ExecuteAsync()`.

2. **Re-run domain policy checks during pre-execution** in `KubernetesPlanExecutor.CheckPreExecutionAsync()`. Re-evaluate `K8sPolicyValidator` against the current Kubernetes state if possible, or at minimum re-validate the manifest against current admission policies.

3. **Populate RedactionMetadata** (if/when redacted evidence becomes a real use case) in `KubernetesApprovalAdapter.BuildEvidenceArtifacts()`.

4. **Deduplicate pending challenges** in `GatewayApprovalService.EnsureApprovedOrCreateChallengeAsync()` -- check for existing pending challenge before creating a new one.
