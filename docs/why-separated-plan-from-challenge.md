# Knowledge memo: why this repo separates `K8sPlan` from `ApprovalChallenge`

## Context

Why there are two distinct concepts — a plan and an approval challenge — when both relate to the same mutation. This memo answers that by grounding the explanation in the actual types and code paths in the repo (per the [repo-onboarding skill](../.agents/skills/repo-onboarding/SKILL.md)).

---

## The two records, side by side

[K8sPlan](../src/InfraGate.Approvals/K8sPlan.cs) — the **mutation request**:

```text
Id                string         (e.g. 20260511172300-000e8c5c)
Operation         string         (apply | delete | scale | restart | set-image)
Namespace         string
CreatedAtUtc      DateTimeOffset
Description       string
Parameters        Dictionary<string,string>
Objects           K8sObjectRef[]
Manifest          string?
DryRun            K8sPlanDryRun?
Diffs             K8sPlanDiff[]
PolicyFindings    K8sPlanPolicyFinding[]
```

[ApprovalChallenge](../src/InfraGate.Approvals/ApprovalChallenge.cs) — the **approval ticket**:

```text
Id                            string         (challenge id ≠ plan id)
PlanId                        string         (points at the K8sPlan)
PlanHash                      string         (SHA-256 of pending plan JSON when ticket was issued)
RequesterSubject              string         (OAuth sub of the AI/client side)
RequesterAuthenticationType   string?
CreatedAtUtc                  DateTimeOffset
ExpiresAtUtc                  DateTimeOffset (default TTL: 15 minutes)
Status                        string         (pending | approved | denied | expired)
ApproverSubject               string?        (OAuth sub of the human who clicked Approve)
DecidedAtUtc                  DateTimeOffset?
```

They live in different stores on disk ([ApprovalConventions.Storage](../src/InfraGate.Approvals/ApprovalConventions.cs#L10-L20)):

```text
<approval-root>/
  pending/<planId>.json         ← the K8sPlan
  challenges/<challengeId>.json ← the ApprovalChallenge
  approved/<planId>.sha256      ← hash file written after a challenge approves
  applied/<planId>.json         ← post-execution record
  audit.jsonl                   ← every state transition
```

---

## What each one represents (in one line each)

- **Plan** = *what* will change in Kubernetes. The object Kubernetes will see if approved.
- **Challenge** = *who is asking to approve it, when, and for how long*. An ephemeral permission slip bound to one specific approval attempt.

The plan is the *resource*. The challenge is the *authorization request to act on it*.

---

## Why they aren't merged

Five concrete reasons, each visible in the code:

### 1. Different lifetimes

- A plan lives from creation (request_*) through `applied/` (or denial/cleanup). Days, potentially.
- A challenge has a hard 15-minute TTL by default ([`McpGatewayOptions.DefaultApprovalChallengeTtl`](../src/InfraGate.McpGateway/McpGatewayOptions.cs#L22)) and is single-use. If it expires, the plan is still valid — you just need a new challenge.

If they were one record, the TTL on the challenge side would either over-constrain the plan or under-constrain the approval window.

### 2. One plan → many challenge attempts

If a user lets a challenge expire or denies it, [`EnsureApprovedOrCreateChallengeAsync`](../src/InfraGate.McpGateway/GatewayApprovalService.cs) creates a **fresh** challenge for the same `planId`. The plan never changes; only a new ticket is minted. This is exactly the OAuth pattern — one resource, many short-lived tokens trying to act on it.

### 3. Two different security questions

The plan answers: *"Is this change valid and policy-compliant?"* — driven by [`K8sPolicyValidator`](../src/InfraGate.McpServer/Policy/K8sPolicyValidator.cs), the dry-run, and the manifest parser. None of that involves a user.

The challenge answers: *"Did the right human, while still authorized, click Approve?"* — driven by [`GatewayApprovalService.ApproveChallengeAsync`](../src/InfraGate.McpGateway/GatewayApprovalService.cs) checking:
- The approver's `sub` claim equals `RequesterSubject` (same-subject mode).
- `ExpiresAtUtc` is still in the future.
- The challenge's `Status` is still `pending`.
- The challenge's stored `PlanHash` still matches the live pending file hash (drift detection).

Splitting the records makes it impossible for one concern to silently mutate the other.

### 4. Hash binding, decoupled from approval mechanics

When a challenge is created, it snapshots the current `PlanHash`. If the pending plan file changes between challenge creation and the approve click, the hash comparison in [GatewayApprovalService.cs](../src/InfraGate.McpGateway/GatewayApprovalService.cs) detects it and refuses approval ("The pending plan changed after this approval URL was created."). This is the safety property proved by [`ModifiedPendingPlanTests`](../tests/InfraGate.Safety.E2E.Tests/Workflows/ModifiedPendingPlanTests.cs) and [`ApproveChallengeAsync_PlanHashDrift_Rejects`](../tests/InfraGate.McpGateway.Tests/UnitTests/GatewayApprovalServiceTests.cs).

If the plan and challenge were one record, there would be no "before" snapshot to compare against.

### 5. Forward-compatibility for multi-approver flows

The roadmap ([.agents/Plans/archive/security-roadmap.md §13](../.agents/Plans/archive/security-roadmap.md)) calls for two-person rule, approver groups, and break-glass modes — all of which mean *multiple* challenges per plan, possibly with different approvers and different policies. Keeping challenge as a separate record means adding those modes is an additive change to one type, not a schema refactor of the plan.

---

## End-to-end flow (where each one is used)

```text
1. AI client calls request_apply_manifest (or scale/restart/setImage/delete)
   └── McpServer creates K8sPlan, writes pending/<planId>.json
       Audit: plan_requested  (PlanRequestedPayload)

2. AI client calls apply_approved_plan(planId)
   └── McpServer asks ApprovalStore.GetApprovedPlanAsync
       └── No approved/<planId>.sha256 exists yet
           Server returns "Refused: not approved" up to the Gateway
       Gateway's EnsureApprovedOrCreateChallengeAsync sees no challenge either
           Creates ApprovalChallenge, writes challenges/<challengeId>.json
           Audit: approval_challenge_created  (ApprovalChallengeCreatedPayload)
           Returns approval URL to the AI client

3. Human opens approval URL in a browser (separate OAuth session)
   └── GET /approvals/{challengeId}
       Gateway authenticates via ApprovalOAuth (different cookie session)
       Renders plan + diff + dry-run + policy findings

4. Human clicks Approve
   └── POST /approvals/{challengeId}/approve  (antiforgery-protected)
       GatewayApprovalService.ApproveChallengeAsync:
         - Compares HTTP user's `sub` to challenge.RequesterSubject
         - Compares challenge.PlanHash to current pending file hash
         - Marks challenge Status=approved, sets ApproverSubject + DecidedAtUtc
         - Calls ApprovalStore.ApprovePendingPlanAsync
             which writes approved/<planId>.sha256
         Audit: approval_challenge_approved  (ApprovalChallengeApprovedPayload)
         Audit: plan_approved                 (PlanApprovedPayload)

5. AI client calls apply_approved_plan(planId) again
   └── ApprovalStore.GetApprovedPlanAsync now finds approved/<planId>.sha256
       Recomputes pending hash; must still match (drift check)
       McpServer reruns dryRun=All; must still succeed (pre-apply gate)
       Then mutates Kubernetes; moves to applied/<planId>.json
       Audit: plan_applied  (PlanAppliedPayload)
```

The plan threads through every step from 1 to 5. The challenge only matters for steps 2 to 4 — it is *gone* (well, marked consumed) before any real Kubernetes mutation happens.

---

## TL;DR

| | Plan (`K8sPlan`) | Challenge (`ApprovalChallenge`) |
|---|---|---|
| **Conceptual role** | The change being requested | Permission to approve that change |
| **Lifetime** | Long (until applied/cleaned up) | 15 min, single-use |
| **Identifies** | A mutation | An approval attempt |
| **Bound to** | A namespace + objects | A requester subject + plan hash + clock |
| **Holds** | Manifest, diff, dry-run, policy findings | Identities, timestamps, status |
| **Stored at** | `pending/<planId>.json` → `applied/<planId>.json` | `challenges/<challengeId>.json` |
| **Multiplicity** | 1 per intent | Many possible per plan (retries) |

If you imagine OAuth: `K8sPlan` is the resource, `ApprovalChallenge` is the authorization-code grant. Different lifecycles for different reasons.
