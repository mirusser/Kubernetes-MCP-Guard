# Implementation Plan: Senior-Level Portfolio Hardening

## Overview

This project is already a strong mid-level / senior-leaning portfolio entry. To make it read as a real senior-level engineering project, the next work should show that the system is not only clever and feature-rich, but also operable, maintainable, security-reviewed, and production-shaped.

The goal is not to add more demo features. The goal is to make the project communicate senior judgment: tradeoffs, boundaries, failure modes, operations, threat modeling, and a clear path from experimental implementation to production readiness.

## Current Positioning

Frame the project as:

> I built an experimental .NET MCP gateway exploring how AI agents can safely propose Kubernetes mutations through a Human-in-the-Loop approval flow.

Do not frame it as:

> I built production-ready Kubernetes security infrastructure.

The strongest interview summary is:

> This project explores how a .NET MCP gateway can let AI agents inspect Kubernetes and propose changes while keeping mutation authority behind OAuth-authenticated, browser-based Human-in-the-Loop approval. The interesting part is the approval model: the AI gets a plan ID and approval URL, but execution requires digest-bound human approval plus pre-execution gates.

## Biggest Missing Senior-Level Signals

### 1. Production-readiness story

The README and security docs correctly say the project is experimental. A senior-level portfolio entry should also show what would be required for production:

- deployment topology
- secrets handling
- external OIDC setup
- persistent approval store strategy
- backup/restore expectations
- TLS / ingress assumptions
- failure modes and recovery

Add a short `docs/production-readiness.md`.

### 2. Operational observability

The project has JSONL audit logs and Serilog, but a senior reviewer will look for:

- structured event taxonomy
- metrics
- health checks / readiness checks
- correlation IDs across request -> plan -> challenge -> grant -> execution
- how to debug a failed approval or apply

This does not require full OpenTelemetry immediately. The first step can be a documented observability model that identifies current coverage and gaps.

### 3. Security threat model depth

The security docs are already good, but senior-level polish should include:

- explicit attacker personas
- trust boundary diagram
- abuse cases
- what each control prevents
- what each control does not prevent
- residual risk table

This makes the project read as security engineering, not only security-themed application work.

### 4. Downstream identity story

The planned client-credentials OAuth flow is a real senior-level next step. Present it as a clear service boundary:

- gateway authenticates the MCP user/client
- downstream server authenticates gateway/service identity
- process locality is not treated as trust
- future delegated identity remains possible if needed

### 5. Persistence and concurrency model

The file-backed approval store is appropriate for an experimental implementation, but senior reviewers may ask:

- what happens with concurrent approvals?
- what happens with multiple gateway replicas?
- how are partial writes handled?
- what about clock skew?
- what is the cleanup and retention policy?
- how would this migrate from file storage to a database?

This does not need a database implementation now, but the current tradeoff and next design should be explicit.

### 6. Crisp demo narrative

The project is complex. The senior-level presentation needs a clean three-minute flow:

> AI proposes a Kubernetes mutation. Gateway builds a digest-bound plan. Human approves out-of-band. Execution validates grant, digests, freshness, policy, and RBAC before mutation.

Then show one failure case:

- wrong user cannot approve
- tampered plan cannot execute
- stale live state blocks execution

## What Is Already Senior-ish

- The approval vocabulary is thoughtful.
- The generic core vs Kubernetes adapter boundary is a real architecture decision.
- The tests are much deeper than typical portfolio tests.
- The security model is honest about non-goals.
- The project has CI, Docker, OAuth, Kubernetes, and real integration surfaces.

## Task List

### Task 1: Add `docs/production-readiness.md`

**Description:** Document the gap between the experimental reference implementation and a production deployment. This should not claim production readiness; it should show a credible senior-level understanding of what production readiness would require.

**Acceptance criteria:**
- [ ] Describes deployment topology for local/demo, single-node production-like, and multi-instance future shapes.
- [ ] Covers TLS/ingress, external OIDC, secret handling, persistent approval store, backup/restore, retention, failure recovery, and rollout concerns.
- [ ] Explicitly states current limitations and what is intentionally not production-certified.

**Verification:**
- [ ] `git diff --check -- docs/production-readiness.md README.md`
- [ ] Links from README project map or boundaries section point to the new doc.

**Dependencies:** None.

**Estimated scope:** Small.

### Task 2: Expand the security model with threat-model depth

**Description:** Strengthen `docs/security-model.md` or add a focused threat-model doc that names attackers, trust boundaries, abuse cases, control coverage, and residual risks.

**Acceptance criteria:**
- [ ] Includes explicit attacker personas such as malicious MCP client, compromised workload output, wrong authenticated user, stale approval replay attempt, and compromised gateway host.
- [ ] Includes a trust-boundary diagram or table.
- [ ] Maps controls to threats and clearly states what each control does not prevent.
- [ ] Includes a residual risk table.

**Verification:**
- [ ] `git diff --check -- docs/security-model.md docs/threat-model.md README.md`
- [ ] Stale terminology scan does not introduce bare "plan hash" as the primary execution binding.

**Dependencies:** None.

**Estimated scope:** Medium.

### Task 3: Add an observability and debugging model

**Description:** Document the current operational signals and the desired observability direction without pretending all production observability exists today.

**Acceptance criteria:**
- [ ] Identifies current audit streams: guardrail audit and approval audit.
- [ ] Defines event taxonomy and correlation path across request, plan, challenge, grant, pre-execution gate, and execution attempt.
- [ ] Documents common debugging flows for approval failure, digest mismatch, dry-run failure, policy denial, and Kubernetes RBAC denial.
- [ ] Lists future metrics and health/readiness checks, clearly marked as future work unless already implemented.

**Verification:**
- [ ] `git diff --check -- docs/observability-model.md README.md`
- [ ] Existing `src/InfraGate.Observability/README.md` remains consistent with the new doc.

**Dependencies:** None.

**Estimated scope:** Medium.

### Task 4: Document downstream identity direction

**Description:** Capture the planned client-credentials OAuth boundary so the project does not present current process topology as the final trust model.

**Acceptance criteria:**
- [ ] Describes current gateway-authenticated MCP boundary without presenting lack of downstream auth as a feature.
- [ ] Describes planned client-credentials OAuth from gateway to downstream server.
- [ ] Explains why service identity, delegated user identity, and Kubernetes RBAC identity are separate concerns.
- [ ] Updates README wording only if needed.

**Verification:**
- [ ] `git diff --check -- docs README.md`
- [ ] README contains no "no token passthrough" or equivalent selling point.

**Dependencies:** None.

**Estimated scope:** Small.

### Task 5: Document persistence, concurrency, and migration tradeoffs

**Description:** Explain the current file-backed approval store and the design path toward a multi-instance-capable durable store.

**Acceptance criteria:**
- [ ] Documents current file-store assumptions, single-instance expectations, partial-write behavior, challenge TTL, retention, and cleanup considerations.
- [ ] Explains concurrency risks such as simultaneous approval attempts, execution reuse races, and multi-gateway deployment.
- [ ] Describes a future database-backed store shape without requiring immediate implementation.
- [ ] Links back to `InfraGate.Approvals` ownership and generic approval core concepts.

**Verification:**
- [ ] `git diff --check -- docs src/InfraGate.Approvals/README.md README.md`
- [ ] No current-state doc claims multi-instance production support unless implemented.

**Dependencies:** None.

**Estimated scope:** Medium.

### Task 6: Prepare a three-minute portfolio demo script

**Description:** Create a concise narrative for engineering interviews that explains the project without drowning the reviewer in terminology.

**Acceptance criteria:**
- [ ] Includes the 30-second project summary.
- [ ] Includes the happy-path flow: propose -> plan -> browser approval -> gate validation -> Kubernetes mutation.
- [ ] Includes one failure case: wrong user, tampered plan, or stale live state.
- [ ] Includes a short "what I would productionize next" section.

**Verification:**
- [ ] `git diff --check -- docs/portfolio-demo-script.md README.md`
- [ ] Script can be read aloud in under three minutes.

**Dependencies:** Tasks 1-5 inform the final "productionize next" section, but the first draft can be written independently.

**Estimated scope:** Small.

## Checkpoint: Senior Portfolio Readiness

- [ ] README points to production readiness, security/threat model, observability, and demo script docs.
- [ ] Docs clearly distinguish shipped behavior from target profile direction.
- [ ] The first screen of the README still reads well to a hiring manager.
- [ ] The project can be explained in three minutes without relying on internal-only terminology.
- [ ] The project remains honest: experimental reference implementation, not production-certified infrastructure.

## Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Docs overstate production readiness | High | Keep "experimental reference implementation" language visible and repeat current limitations. |
| Too much terminology overwhelms reviewers | Medium | Use README and demo script for plain language; keep glossary detail in `CONTEXT.md`. |
| New docs drift from implementation | Medium | Link each claim to existing code/docs where possible and run README/doc terminology scans. |
| Planned downstream OAuth is described as already implemented | High | Mark client-credentials OAuth as planned direction until the code exists. |
| File-store limitations look like hidden flaws | Medium | State the tradeoff directly and explain the migration path. |

## Open Questions

- Should production-readiness and threat-model content be separate docs, or should `docs/security-model.md` own both?
- Should the portfolio demo script live under `docs/` or a public-facing `.github/`/README section?
- Should observability direction stop at documentation, or should the next implementation step add correlation IDs/metrics first?
