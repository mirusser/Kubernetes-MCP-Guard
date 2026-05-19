# Mutation Approval Flow Review — Post Digest-Grant Fixes

Date: 2026-05-15
Review scope: Commits `51d05f7` through `8c08bd5` — test fixes after digest-bound approval grants.

Sources consulted: `CONTEXT.md`, `docs/mutation-approval-flow.md`, `docs/mutation-approval-profile.md`, `docs/adr/0001-*.md`, `docs/adr/0002-*.md`

---

## Findings

### 1. Stale "plan hash" terminology (LOW)

**File:** `.agents/skills/run-tests/SKILL.md:163`

Uses "plan hash mismatch detection" — the canonical term is **digest mismatch** (Intent Digest / Review Digest). The test file is legacy-named `PlanHashMismatchTests` but new documentation should use canonical terminology.

**Suggested fix:** Replace with "review digest mismatch detection."

---

### 2. Digest-mismatch audit event gap (LOW)

**Files:** `PlanHashMismatchTests.cs`, `GatewayApprovalService.cs`

The old `approval_hash_mismatch` audit event was written by `ApprovePendingPlanAsync` (direct-store approval path). The new grant-based path (`ApproveChallengeAsync` → `CreateGrantAsync` → `GetGrantedPlanAsync` → `ValidateGrant`) does **not** write an audit event when the review digest no longer matches. The refusal message is returned to the caller but not recorded in the audit trail.

This means a security-relevant event (stale grant refused due to digest change) leaves no audit evidence. The `apply_denied` event *is* written by the server-side `ApplyApprovedPlanAsync`, but only if the request reaches the server — the gateway returns the refusal first, so the server-side event is never triggered.

**Suggested fix:** Write an audit event in `GetGrantedPlanAsync` or `ValidateGrant` when the grant is denied due to digest mismatch, or gateway-level write in `EnsureApprovedOrCreateChallengeAsync` on the `!granted.IsGranted && granted.GrantExists` path.

---

### 3. `CreateAsync` overload without digests is a trap (MEDIUM)

**File:** `src/InfraGate.Approvals/ApprovalChallengeStore.cs:24-39`

The simplified overload passes `intentDigest: null, reviewDigest: null`, which causes `ValidatePendingChallengeAsync` to immediately reject the challenge at the digest-binding check (line 320-331 of `GatewayApprovalService.cs`). No caller in production uses this overload — the Gateway always passes digests from the pending plan envelope. But it remains in the public API surface and the Keycloak test was a victim of it.

**Suggested fix:** Remove the simplified overload, or make `intentDigest` and `reviewDigest` required parameters in the remaining overload.

---

## Items Reviewed With No Findings

- **Challenge/grant split**: Correct. `ApproveChallengeAsync` creates grant + outcome. `ApplyApprovedPlanAsync` consumes grant. Test fixes feed correct data into both paths.
- **Identity and binding invariants**: Correct. Challenge binds to planId, hash, requester, digests. Grant binds to plan, requester, approver, digests, policy, expiry. Same-subject is default.
- **Pre-execution gates**: No changes. Gate orchestration in `ValidateGrant` unchanged.
- **Generic/domain ownership boundary**: Correct. DI fixes separate generic (`ApprovalStore`, `GatewayApprovalService`, `PlanEnvelopeFactory`) from domain-adapter (`KubernetesPlanReviewAdapter`, `KubernetesPlanReviewRenderer`).
- **Scenario coverage**: All 7 safety-property E2E scenarios pass.
- **Other anti-terms**: No "approval flag," "approval result" (singular), or "Approval Outcome" found in changed files.
