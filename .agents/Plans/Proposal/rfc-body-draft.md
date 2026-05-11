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

Current approaches to human approval for AI mutations fall into one of two failure modes:

1. **Boolean prompts** ("Do you want me to restart nginx-prod? yes/no") — the human approves based on a *description* the AI wrote, not the actual payload. A compromised or prompt-injected AI can describe one thing and send another. This is a textbook **Time-of-Check to Time-of-Use (TOCTOU)** vulnerability.

2. **UI takeover** (AWS Nova Act) — works well for browser automation, but when the AI is talking to a structured API, the human still gets a text summary and the underlying JSON payload can be swapped in memory between approval and execution.

3. **Stateful workflow engines** (Oracle Integration Cloud HITL, BPEL) — solve TOCTOU but require adopting a monolithic proprietary platform. Not an option for teams running open-source Kubernetes tooling.

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

The human opens the URL in a **separate browser session** authenticated via its own OAuth flow (completely independent of the AI client's session). The browser renders the exact plan: diff, dry-run result, policy findings. The human clicks Approve.

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

All tests run opt-in via `INFRA_GATE_RUN_SAFETY_E2E=1`. The test infrastructure uses real Keycloak tokens — no mocked auth.

Branch with tests: https://github.com/mirusser/Kubernetes-MCP-Guard/tree/feature/safety-tests

### Why This Should Be a Standard

Your repository is becoming the foundational reference for Kubernetes MCP. If MCP infrastructure servers adopt different, incompatible approval patterns:
- MCP clients won't know whether to expect a boolean `needs_approval` flag, a URL, or a hash
- Security auditors will have no shared language for verifying HITL compliance
- The TOCTOU attack surface will remain open across the ecosystem

I'm proposing that we define a minimal standard for **structured mutation approval** in MCP infrastructure servers:

1. A `plan` phase that returns a plan ID and hash, not a mutation
2. A `challenge` phase that creates a time-bounded, identity-bound approval ticket
3. An `execute` phase that verifies both the hash and the challenge before mutating

This doesn't require adoption of my implementation. It requires agreement on the *interface contract* so that MCP clients can reason about approval state portably.

### The Ask

1. Is the TOCTOU concern recognized by the team? Have you considered it in your roadmap?
2. Would you be open to a Discussion thread on formalizing a `propose → approve → execute` tool naming convention?
3. If yes, I'm happy to draft a more formal spec document for review.

For reference, the architecture rationale is documented in [docs/why-separated-plan-from-challenge.md](https://github.com/mirusser/Kubernetes-MCP-Guard/blob/main/docs/why-separated-plan-from-challenge.md) in my repo.

Thanks for the great work on this project.
— @mirusser
