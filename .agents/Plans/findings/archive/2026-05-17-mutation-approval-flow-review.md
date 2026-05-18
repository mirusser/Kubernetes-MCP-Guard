# Review: mutation-approval-flow.md vs CONTEXT.md vs Implementation

**Date:** 2026-05-17

---

## 1. Glossary Alignment — PASS

All canonical terms from `CONTEXT.md` appear consistently across `mutation-approval-flow.md` and `mutation-approval-profile.md`. No anti-terms ("plan hash", "Approval Outcome", "approval flag", "self-approval", "plan expiry") found in any current doc or C# production file. (ADR 0003 previously confirmed this.)

## 2. Challenge/Grant Split — PASS

Documented correctly and **implemented in C#** (`ChallengeOutcome.cs`, `ApprovalGrant.cs` at `src/InfraGate.Approvals/`). The split is correct: ChallengeOutcome is the terminal audit record; ApprovalGrant is durable execution authorization. `ApprovalStore.ValidateGrant()` (`src/InfraGate.Approvals/ApprovalStore.cs:340`) enforces this separation.

## 3. Identity & Binding Invariants — PASS

- Requester recorded in PlanEnvelope (`PlanEnvelope.cs:55`)
- Same-subject approval is the default (only implemented) policy (`ApprovalConventions.cs:50`)
- ApprovalGrant binds to PlanId, Requester, Approver, IntentDigest, ReviewDigest, ApprovalPolicy, execution reuse policy, and expiry (`ApprovalGrant.cs:3-14`)

## 4. Plan Identity & Digest Semantics — PASS

- PlanId is opaque random hex (`ApprovalStore.cs:33-39`)
- IntentDigest separate from ReviewDigest (`PlanEnvelope.cs:63-65`)
- Kubernetes adapter owns intent canonicalization (`KubernetesApprovalAdapter.cs:98-110`)
- Generic core owns review digest canonicalization (`PlanEnvelopeFactory.ComputeReviewDigest`)

## 5. Validity & Execution Gates — PARTIAL

**In docs:** All gates described correctly in `mutation-approval-flow.md:160-197`.

**In code:** `ValidateGrant()` at `ApprovalStore.cs:340` checks grant expiry, plan validity window, digest matching, same-subject policy, reuse policy, and recomputes review digest. However:

| Gate | C# Type? | Verified at execution? |
|---|---|---|
| Plan Validity Window | `PlanValidityWindow` | Yes |
| Authorization Check | No separate type | Implicit via gateway OAuth |
| Approval Grant valid | `ApprovalGrant` | Yes |
| Intent Digest match | `ApprovalDigest` | Yes |
| Review Digest match | `ApprovalDigest` | Yes |
| Execution Reuse Policy | `ExecutionReusePolicy` | Single-execution enforced via applied marker |
| **Freshness Policy** | **None** | **Only implicit** (dry-run re-run in adapter, not formalized) |
| **Domain Policy Checks** | **No formal type** | Adapter-level findings exist (`K8sPlanPolicyFinding`) but no generic `DomainPolicy` model |

## 6. Generic/Domain Ownership — PARTIAL

The `InfraGate.Approvals` project (29 types) correctly owns generic concepts. The `InfraGate.KubernetesAdapter` project owns adapter-specific types. Clean separation exists. But:

- **`PlanEnvelope` is missing `FreshnessPolicy`** — the profile sketch shows it as a required field (`mutation-approval-profile.md:65-71`), but no such property exists on the C# record (`PlanEnvelope.cs:5-68`).
- **No `DomainPolicyCheck` type** — documented in CONTEXT.md (line 105-108) but not modeled generically or passed through gates.

## 7. Scenario Coverage — MOSTLY

All five scenarios in `mutation-approval-flow.md:199-241` are conceptually correct vs. the current implementation, but "Approved But Stale Before Execution" (scenario 4) currently only exercises dry-run re-execution (adapter-level), not a formalized FreshnessPolicy gate. The profile sketch acknowledges this at line 199: *"Remaining drift from the target profile includes fuller generic policy/freshness modeling."*

---

## Summary: What's Missing

| Gap | Where defined | Severity |
|---|---|---|
| `FreshnessPolicy` C# type | `mutation-approval-profile.md:65-71` | **High** — blocking pre-execution gate completeness |
| `FreshnessPolicy` field on `PlanEnvelope` | Profile sketch JSON schema | **High** — plan envelope incomplete vs. target |
| `DomainPolicyCheck` model | `CONTEXT.md:105-108` | **Medium** — documented but not formalized |
| Formal `AuthorizationCheck` gate | `CONTEXT.md:157-159` | **Low** — implicit via OAuth, may not need explicit type |
| `Redacted Evidence` types | `CONTEXT.md:129-131` | **Low** — not currently needed for same-subject flow |
| `ReusablePlan` execution reuse policy | `CONTEXT.md:93-95` | **Low** — explicitly deferred in roadmap |

The docs (`CONTEXT.md`, `mutation-approval-flow.md`, `mutation-approval-profile.md`) are internally consistent and form a clear target. The code has covered ~70% of the target profile. The main missing piece is **formalizing FreshnessPolicy as a first-class generic type with checks evaluated during the pre-execution gate sequence**, and adding it to the `PlanEnvelope` model.
