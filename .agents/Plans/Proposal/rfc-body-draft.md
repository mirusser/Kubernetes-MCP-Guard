# RFC Draft: GitHub Discussion Body

**Target repo:** `containers/kubernetes-mcp-server`  
**Type:** GitHub Discussion (Idea / RFC)  
**Status:** Draft — do not post until the demo has been run once end-to-end successfully.

---

## Title

`[RFC] Cryptographic Human-in-the-Loop for MCP Mutations: The Plan-Challenge-Hash Pattern`

---

## Body

Hi team,

I've been building a production-ready gateway for AI-assisted Kubernetes operations ([Kubernetes-MCP-Guard](https://github.com/mirusser/Kubernetes-MCP-Guard)) and I'd like to raise a security concern and propose a pattern that I think deserves standardization across MCP infrastructure servers.

### The Problem: Consent Fatigue and TOCTOU in Simple Approval Flows

Current approaches to human approval for AI mutations fall into one of three patterns, each with its own gap:

1. **Boolean prompts** ("Do you want me to restart nginx-prod? yes/no") — the human approves based on a *description* the AI wrote, not the actual payload. A compromised or prompt-injected AI can describe one thing and send another. This is a textbook **Time-of-Check to Time-of-Use (TOCTOU)** vulnerability.

2. **UI takeover** (AWS Nova Act) — designed for browser UI automation. When applied to structured API mutations, the approved payload may not be cryptographically bound between the human's approval click and the actual API call. A useful contrast, but not a mutation approval protocol.

3. **Stateful workflow engines** (Oracle Integration Cloud HITL, BPEL) — can address TOCTOU when designed with immutable plan storage and hash binding, but require adopting a monolithic proprietary platform. Not an option for teams running open-source Kubernetes tooling.

The existing `--read-only` flags and RBAC in `kubernetes-mcp-server` are excellent safeguards, but they don't solve the case where the AI legitimately has write access and a human has approved a specific operation — and then the payload drifts.

### The Proposed Solution: Plans, Challenges, and Hashes

In Kubernetes-MCP-Guard I've implemented a pattern I'm calling **Plan-Challenge-Hash**. Here's how it works:

#### 1. Plan Generation (not execution)

The AI calls `request_apply_manifest` (or `request_restart_deployment`, etc.). Instead of mutating the cluster, the server:
- Runs a **server-side dry-run** against the real cluster API
- Runs the manifest through a **policy validator** (rejects privileged containers, hostPath, hostNetwork, dangerous capabilities, etc.)
- Computes a **diff** against the live cluster state
- Writes a pending plan JSON to disk with all of the above captured
- Returns a plan ID and a summary — **no cluster mutation happens yet**

#### 2. Out-of-Band Approval

The AI calls `apply_approved_plan(planId)`. The gateway:
- Creates an **ApprovalChallenge** record with a SHA-256 hash of the pending plan file and a 15-minute TTL
- Returns an **approval URL** to the AI client

The gateway enforces that the human must open the URL in a browser session authenticated via its own out-of-band OAuth flow (completely independent of the AI client's session). The browser renders the exact plan: diff, dry-run result, policy findings. The human clicks Approve.

#### 3. Cryptographic Execution Gate

The AI calls `apply_approved_plan(planId)` again. The server:
1. Recomputes the SHA-256 of the pending plan file — must match what was in the challenge
2. Re-runs the server-side dry-run — must still succeed (cluster state may have changed)
3. Only then mutates Kubernetes

If the plan file was tampered with between challenge creation and the approval click, the hash comparison fails and the operation is refused with `approval_hash_mismatch`. The AI *cannot* swap the payload.

#### Key Security Properties

| Property | Mechanism |
|---|---|
| Payload integrity | SHA-256 of the pending plan file, compared at approval time |
| Out-of-band identity | Approver's OAuth `sub` must match requester's `sub` (same-subject mode) |
| Time-bounded | ApprovalChallenge TTL = 15 min by default; expired challenges are refused |
| Replay prevention | Challenge is single-use; applied plans are marked to prevent re-execution |
| Double-spend prevention | `applied/<planId>.json` marker file blocks a second execution |
| Pre-apply drift detection | Diff is re-checked immediately before apply; stale approval is refused |
| Audit trail | Every state transition written to `audit.jsonl` with typed payloads |

### Proof: E2E Tests Against a Real Keycloak + Kubernetes Cluster

These properties are not just documented — they are **machine-verified by E2E tests** that run against a real Keycloak instance (via Testcontainers) and a real Kubernetes cluster:

| Safety property | Test |
|---|---|
| TOCTOU block (hash mismatch) | `PlanHashMismatchTests` |
| Plan file tampered between challenge and click | `ModifiedPendingPlanTests` |
| Wrong-user approval refused (endpoint, service, browser, real JWT paths) | `WrongUserApprovalTests.ApproveChallengeEndpoint_ByDifferentSubject_IsRefused` |
| Expired challenge refused | `ExpiredApprovalTests` |
| Already-applied plan refused (replay) | `AlreadyAppliedPlanTests` |
| Pre-apply dry-run failure blocks execution | `DryRunFailureTests` |
| Dangerous manifest blocked by policy | `DangerousManifestTests` |
| Full happy path: browser approval → audit trail | `FullApprovalFlowTests.RestartDeployment_ApprovedThroughBrowser_AppliesExactPlanAndAudits` |
| RBAC: read-only SA cannot apply | `RbacMatrixTests` |

The `feature/safety-tests` branch includes SafetyE2E coverage for hash mismatch, modified pending plans, expired approvals, wrong-user approvals, already-applied plans, dangerous manifests, dry-run failure, and RBAC boundaries. The suite exercises real gateway/MCP/Kubernetes paths and uses real Keycloak JWTs for MCP bearer authentication. Browser approval OAuth is simulated at the callback/backchannel boundary in tests, with separate service-level coverage for real-JWT wrong-user rejection. A GitHub Actions workflow (`safety-e2e.yml`) runs the suite against an ephemeral KinD cluster on demand.

Branch with tests: https://github.com/mirusser/Kubernetes-MCP-Guard/tree/feature/safety-tests

### Why This Needs a Portable Standard

MCP already has useful primitives: tool annotations (`readOnlyHint`, `destructiveHint`), human-in-the-loop guidance, and in current draft work, elicitation/URL mode for out-of-band sensitive interactions. Those are good foundations. What MCP does not currently standardize is a portable **mutation-approval profile**: a `planId`, immutable digest, approval challenge, same-user identity binding, TTL, single-use semantics, drift check, and execute-after-approval flow. Servers must implement all of these themselves today.

Your repository is becoming the foundational reference for Kubernetes MCP. Without a shared profile, infrastructure MCP servers will diverge:
- MCP clients won't know whether to expect a boolean `needs_approval` flag, a URL, a hash, or nothing
- Security auditors will have no shared language for verifying HITL compliance
- Teams will rely on ad-hoc prompts or untrusted `destructiveHint` annotations for real cluster mutations

I'm proposing an optional MCP mutation-approval profile that defines a minimal contract for **structured mutation approval**:

1. A `plan` phase that returns a plan ID and hash, not a mutation
2. A `challenge` phase that creates a time-bounded, identity-bound approval ticket
3. An `execute` phase that verifies both the hash and the challenge before mutating

This doesn't require adoption of my implementation. It requires agreement on the *interface contract* so that MCP clients can reason about approval state portably.

### The Ask

1. Is the TOCTOU concern recognized by the team? Have you considered it in your roadmap?
2. Would you be open to collaborating on this thread to formalize a `propose → challenge → execute` tool contract for this repository or as an MCP standard for mutations?
3. If yes, I'm happy to draft a more formal spec document for review.

For reference, the architecture rationale is documented in [docs/why-separated-plan-from-challenge.md](https://github.com/mirusser/Kubernetes-MCP-Guard/blob/feature/safety-tests/docs/why-separated-plan-from-challenge.md) in my repo.

Thanks for the great work on this project.
— @mirusser
