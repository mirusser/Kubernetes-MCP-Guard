# Plan Review: implement-adr-0002-opaque-plan-identifiers.md vs ADR 0002 + CONTEXT.md

Date: 2026-05-15
Plan reviewed: `.agents/Plans/implement-adr-0002-opaque-plan-identifiers.md`
Sources: `docs/adr/0002-use-opaque-plan-identifiers-and-separate-digests.md`, `CONTEXT.md`

---

## Aligned (no issues)

| Plan item | ADR/CONTEXT says | Match? |
|-----------|-----------------|--------|
| `PlanEnvelope.Id` as opaque **Plan Identifier** | "opaque stable handle, not an integrity mechanism" | Yes |
| Timestamp→random IDs | "deterministic identifiers can leak same-operation equality" | Yes — removing timestamp prefix fixes this |
| `ApprovalChallenge.PlanHash` → `PendingPlanHash` | "plan hash" is an anti-term; only for drift detection | Yes |
| Remove `GetApprovedPlanAsync`/`ApprovePendingPlanAsync`/`ApprovedPlanResult`/`approved/*.sha256` | Execution authorized only by **Approval Grant** | Yes |
| Narrow `IntentDigest` canonicalization (no planId, requester, evidence) | **Intent Digest** = executable mutation intent only | Yes |
| Include `ReviewContext` in `ReviewDigest` canonicalization | **Review Digest** covers "review-surface context" | Yes |
| Test: approved hash without grant cannot mutate | "Approval is necessary but not sufficient" | Yes |

---

## Findings

### 1. Term mismatch: "ReviewContext" vs canonical term (LOW)

**Plan says:** "Add a generic `ReviewContext`/review-surface metadata field to `PlanEnvelope`"

**CONTEXT.md says:** The canonical terms are **Review Surface** (line 126: "The trusted human-facing surface that renders the immutable review snapshot") and **Review Digest** covers "review-surface context" (line 189).

**Issue:** `ReviewContext` is not a canonical term. The field should be named after the glossary — e.g., `ReviewSurfaceContext` or `ReviewSurfaceCanonicalization` — not an invented term.

**Suggested fix:** Rename to `ReviewSurfaceContext` or use the glossary phrase "review-surface context" in comments and field descriptions.

---

### 2. Plan ID prefix `p-` leaks type (LOW)

**Plan says:** "random opaque IDs, e.g. `p-` plus 128-bit lowercase hex"

**ADR 0002 says:** "the plan identifier itself must not become an integrity mechanism because deterministic identifiers can leak same-operation equality and create awkward collision or idempotency semantics."

**Issue:** A `p-` prefix reveals the identifier type (plan vs. challenge vs. grant). Though the ADR is primarily concerned with leaking same-operation equality (which a random hex doesn't), a prefix adds structure that contradicts the "opaque" spirit. Plan IDs and challenge IDs are already distinguished by their storage paths, not their string content.

**Suggested fix:** Drop the `p-` prefix — use bare 128-bit lowercase hex. The existing `IsSafePlanId` validation already handles opaque IDs via character class check.

---

### 3. Missing: dead audit event cleanup (LOW)

**Plan says:** Remove `ApprovePendingPlanAsync` and `ApprovedPlanResult`.

**Issue:** `ApprovePendingPlanAsync` writes the `approval_hash_mismatch` audit event (`ApprovalConventions.AuditEvents.ApprovalHashMismatch`). Removing the only writer leaves a dead event constant and its payload record (`ApprovalHashMismatchPayload`). The plan doesn't specify whether to remove the constant and payload type, or repurpose them.

The `apply_denied` event already covers grant-denied execution attempts. The `approval_hash_mismatch` event was specific to the old hash-based approval path.

**Suggested fix:** Explicitly remove `ApprovalHashMismatch` event constant, `ApprovalHashMismatchPayload` record, and all audit-write call sites. No replacement needed — `ApplyDenied` covers the equivalent grant-rejected path.

---

### 4. Missing: `ApprovedDirectory` vestigial code (LOW)

**Plan says:** "approved/*.sha256 directory creation" should be removed. "Existing approved-hash files should be ignored."

**Issue:** The plan doesn't address whether the `ApprovalStore.ApprovedDirectory` property and `GetApprovedPath()` method should be deleted or just left as dead code. If `ApprovePendingPlanAsync` is removed, nothing writes to `ApprovedDirectory`, but `EnsureDirectories()` still creates it on every call (wasting an empty directory on disk). `GetApprovedPath` remains exposed on the public API surface.

**Suggested fix:** Remove `ApprovedDirectory` property and `GetApprovedPath()` method. Remove `ApprovedDirectory` from `EnsureDirectories()`. Remove `ApprovedPath` field from `ApprovalPlanResult`.

---

### 5. Test plan redundancy (INFO)

**Plan lists:**
```
dotnet test tests/InfraGate.McpServer.Tests/...
dotnet test tests/InfraGate.McpGateway.Tests/...
dotnet test InfraGate.slnx
```

The third command (`InfraGate.slnx`) is a strict superset of the first two. Not wrong, but the existing `./scripts/run-tests.sh` could replace all three with a single command that runs appropriate tiers.

---

## Verdict

The plan is **well-aligned** with ADR 0002 and CONTEXT.md. No finding violates an architectural invariant or misuses a canonical term. The four low findings are terminology/packaging/cleanup issues — naming (`ReviewContext` → `ReviewSurfaceContext`), ID purity (`p-` prefix), dead code removal (`ApprovedDirectory`, `ApprovalHashMismatch`), and test invocation redundancy.
