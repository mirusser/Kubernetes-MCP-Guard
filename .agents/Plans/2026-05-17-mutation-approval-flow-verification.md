# Mutation-Approval Flow: Docs-to-Code Verification

Date: 2026-05-17

## Summary

The implementation **substantially matches** the documented flow. The critical structural contracts (Challenge/Grant split, separate digests, generic/adapter ownership, pre-execution gates) are correctly implemented. There are **naming mismatches** in audit events, one documented challenge status (`canceled`) that is not implemented, and a **Review Digest coverage gap**.

---

### Findings (ordered by severity)

#### 1. `canceled` challenge status: Documented, not implemented
**Violates**: Scenario coverage invariant (SKILL.md §7)

| Source | Location |
|--------|----------|
| Docs listing "canceled" as a terminal outcome | `CONTEXT.md:151`, `docs/mutation-approval-profile.md:117,158`, `docs/mutation-approval-flow.md:145,151,158` |
| Code constants | `ApprovalConventions.cs:80-95` — no `Canceled` in `ChallengeStatuses` or `ChallengeOutcomeStatuses` |

No cancellation logic exists anywhere in `src/`.

**Fix**: Either add `Canceled` to `ApprovalConventions.ChallengeStatuses` and `ChallengeOutcomeStatuses` (with implementation), or remove it from the three doc files as not-yet-implemented.

---

#### 2. Audit event naming: Dot vs. underscore convention
**Violates**: This is a documentation accuracy issue

| Documented Event | Code Constant | File & Line |
|---|---|---|
| `plan.created` | `plan_requested` | `ApprovalConventions.cs:65` |
| `challenge.created` | `approval_challenge_created` | `ApprovalConventions.cs:72` |
| `challenge.approved` | `approval_challenge_approved` | `ApprovalConventions.cs:73` |
| `challenge.denied` | `approval_challenge_denied` | `ApprovalConventions.cs:74` |
| `challenge.expired` | `approval_challenge_expired` | `ApprovalConventions.cs:75` |
| `challenge.rejected` | `approval_challenge_rejected` | `ApprovalConventions.cs:76` |
| `grant.issued` | `grant_issued` | `ApprovalConventions.cs:77` |
| `execution.started` | *(none)* | — |
| `execution.succeeded` | `plan_applied` (different semantics) | `ApprovalConventions.cs:66` |

The docs (`mutation-approval-profile.md:152-163`) use dot-separated names. Code uses underscores and sometimes different nouns (`plan_requested` vs `plan.created`, `plan_applied` vs `execution.succeeded`).

**Fix**: Either rename code constants to match docs, or update the docs to reflect actual code event names.

---

#### 3. Review Digest does not cover Evidence Artifact digests
**Violates**: Review digest semantics invariant (SKILL.md §4)

`CONTEXT.md:197` states:
> Review Digest covers [...] **Evidence Artifact** digests or digest-bound references, redaction metadata, and review-surface context.

The implementation at `PlanEnvelopeFactory.cs:88-106` includes the **raw `payload`** (domain adapter payload) directly — not evidence artifact digests or digest-bound references. There is no "evidence artifact digest" concept in code (zero grep hits for evidence+digest in `src/`). Redaction metadata is also not included.

**Fix**: Either (a) declare in docs that the raw payload is the interim evidence representation, or (b) implement evidence artifact digest summarization into the Review Digest.

---

#### 4. `DomainPolicyCheck` record defined but unused
**Violates**: Generic/domain ownership boundary (SKILL.md §6)

`DomainPolicyCheck.cs:3` is defined in `InfraGate.Approvals` (generic layer) but **never referenced** by any code. The Kubernetes adapter uses its own `K8sPlanPolicyFinding` record instead (`src/InfraGate.KubernetesAdapter/K8sPlanPolicyFinding.cs:4`).

**Fix**: Either use `DomainPolicyCheck` in the adapter (converting from `K8sPlanPolicyFinding`), or delete it as speculative.

---

#### 5. No unified Pre-Execution Gate orchestrator
**Violates**: The docs describe a single gate flow (`mutation-approval-flow.md:161-195`) but code distributes gate checks across:
- `ApprovalStore.ValidateGrant()` at `ApprovalStore.cs:366-407` — grant binding, digest matching, plan validity, reuse policy
- `KubernetesPlanExecutor.CheckLiveDriftAsync()` — freshness: live-drift
- `KubernetesPlanExecutor.RunPreExecuteDryRunAsync()` — freshness: pre-execute dry-run

The separation is architecturally consistent with generic/adapter ownership, but the docs imply a single orchestrating class that calls adapter checks. In practice, the gateway calls the store's `ValidateGrant()`, then separately calls the adapter's executor which internally runs freshness/domain checks. This is a **minor** discrepancy — the flow works correctly but doesn't have the `PreExecutionGate` abstraction.

---

#### 6. Audit events: defined but never written

| Event | Constant | Status |
|---|---|---|
| `apply_failed` | `ApprovalConventions.cs:68` | Defined, **never written** |
| `dry_run_failed` | `ApprovalConventions.cs:69` | Defined, **never written** |
| `diff_failed` | `ApprovalConventions.cs:70` | Defined, **never written** |
| `apply_drift_detected` | `ApprovalConventions.cs:71` | Defined, **never written** |

Dead audit event constants with typed payloads (`PlanAuditPayloads.cs`).

---

### What IS correctly implemented

- **Challenge/Grant split** — `ChallengeOutcome` is terminal audit record; `ApprovalGrant` is separate durable authorization (`CONTEXT.md:149-155`). Grant creation in `ApprovalStore.CreateGrantAsync()` is independent of challenge outcome recording in `GatewayApprovalService.ApproveChallengeAsync()`.
- **Separate Intent/Review digests** — computed in different places with different canonicalizations (`KubernetesApprovalAdapter.cs:102` for intent, `PlanEnvelopeFactory.cs:73` for review).
- **Generic owns review digest, adapter owns intent digest** — matches `CONTEXT.md:226-227`.
- **Grant binding** — `ApprovalGrant.cs:3-14` binds PlanId, Requester, Approver, IntentDigest, ReviewDigest, ApprovalPolicy, ExecutionReusePolicy, Expiry.
- **Same-Subject Approval** is default, not the only possible policy.
- **Single-Execution** is default; ReusablePlan is explicitly rejected.
- **Freshness checks are adapter-owned** — `KubernetesAdapterConventions.cs:19-20` defines `LiveDrift` and `PreExecuteDryRun`.
- **Pre-execution gates** (distributed) verify: grant valid, plan validity window, authorization (same-subject), intent digest match, review digest match, reuse policy, freshness, domain policy.
- **Stale terms check**: no "approval outcome", "plan hash", or "approval flag" anywhere in code or docs — clean.

---

### Residual Risk

- Mermaid diagrams in `docs/mutation-approval-flow.md` have not been visually rendered/verified.
- The `GatewayApprovalService` writes `challenge.approved` audit but the `grant.issued` audit is written separately inside `ApprovalStore.CreateGrantAsync()` — this is correct but the docs' sequence diagram doesn't distinguish these as separate audit-write calls.
