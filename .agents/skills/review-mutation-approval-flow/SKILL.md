---
name: review-mutation-approval-flow
description: Review InfraGate's MCP mutation-approval flow, diagrams, glossary, relationship table, profile sketch, and related ADRs for consistency. Use when Codex is asked to review, validate, stress-test, refine, or update docs around Plan Envelope, Approval Challenge, Challenge Outcome, Approval Grant, digests, pre-execution gates, Generic Approval Core, Domain Adapter, or the Kubernetes adapter boundary.
---

# Review Mutation Approval Flow

## Core Sources

Read only what the task needs, but prefer this order:

1. Use `$repo-onboarding` first when the repo context is not already loaded, especially for broad reviews, unfamiliar work, or checking current source-of-truth docs.
2. `CONTEXT.md` for canonical terminology. Treat it as glossary only.
3. `docs/mutation-approval-flow.md` for diagrams, relationship table, and scenario flows.
4. `docs/mutation-approval-profile.md` for the profile narrative and minimum envelope shape.
5. `docs/adr/0001-separate-generic-approval-core-from-domain-adapters.md` and `docs/adr/0002-use-opaque-plan-identifiers-and-separate-digests.md` when reviewing architectural decisions.
6. `docs/roadmap.md` only when checking public positioning or implementation direction.

Do not read `.agents/Plans/` unless the user explicitly asks for historical planning context.

## Review Workflow

1. Verify glossary alignment.
   - Use exact canonical terms from `CONTEXT.md`.
   - Do not introduce near-synonyms like "approval result", "plan hash", or "approval flag" unless calling them out as anti-terms.
   - Keep `CONTEXT.md` free of implementation detail; put concrete flows in `docs/mutation-approval-flow.md`.

2. Check the challenge/grant split.
   - `Approval Challenge` is the approval attempt.
   - `Challenge Outcome` is the terminal audit record for the challenge.
   - `Approval Grant` is durable execution authorization.
   - Resolving an approved challenge records a challenge outcome and issues or references a grant.
   - The challenge outcome does not authorize execution and does not causally create the grant.
   - Execution consumes the approval grant, not the challenge outcome.

3. Check identity and binding invariants.
   - `Plan Envelope` records the requester.
   - `Approval Grant` is bound to plan identifier, requester, approver, intent digest, review digest, approval policy, expiry, and reuse constraints.
   - Same-subject approval is the default approval policy, not the only possible policy.

4. Check plan identity and digest semantics.
   - `Plan Identifier` is an opaque workflow handle, not an integrity mechanism.
   - `Intent Digest` proves the executable mutation intent is unchanged.
   - `Review Digest` proves the approved review snapshot is unchanged.
   - Review digest covers envelope metadata, requester, policies, validity, intent digest, evidence artifact digests or digest-bound references, redaction metadata, and review-surface context.
   - Domain adapter owns mutation-intent canonicalization; Generic Approval Core owns plan-envelope and review-digest canonicalization.

5. Check validity and execution gates.
   - `Plan Validity Window`, `Challenge TTL`, approval-grant expiry, and freshness policy are separate.
   - Approval is necessary but not sufficient.
   - Pre-execution gates verify grant, digests, validity, authorization, reuse policy, freshness policy, and required domain policy checks immediately before mutation.
   - Single-execution is the default execution reuse policy; reusable plans are explicit opt-in future work.

6. Check generic/domain ownership.
   - Generic Approval Core owns lifecycle state, envelope schema, digest checks, challenge/outcome/grant concepts, audit spine, review canonicalization, and pre-execution gate orchestration.
   - Domain Adapter owns mutation meaning, evidence artifacts, mutation-intent canonicalization, freshness checks, domain policy checks, execution behavior, retry/idempotency semantics, and adapter audit payloads.
   - Kubernetes is the first adapter, not the definition of the generic profile.

7. Check scenario coverage.
   - Happy path: plan -> challenge -> approved challenge records outcome and issues/references grant -> gates -> execution.
   - Denied/rejected/canceled challenge: terminal outcome, no grant, no execution.
   - Expired challenge: terminal outcome, no grant; another challenge may be possible while plan validity and policy allow it.
   - Approved but stale before execution: grant exists, freshness/domain gate blocks execution.
   - Failed or unknown execution: adapter owns retry semantics; reuse policy constrains successful executions.

## When Editing Docs

- Make the smallest documentation change that resolves the inconsistency.
- Update `CONTEXT.md` only when a canonical term or relationship changes.
- Update `docs/mutation-approval-flow.md` when diagrams, relationship tables, or scenarios need to make the model testable.
- Update `docs/mutation-approval-profile.md` when the profile narrative changes.
- Update ADRs only for durable, surprising decisions with real trade-offs.
- Keep roadmap wording high-level and public-facing.

## Verification

Before saying the review or edit is done:

- Search for stale terms with `rg`, especially `Approval Outcome`, `approval outcome`, `plan hash`, `approval flag`, and wording where `Challenge Outcome` appears to produce or authorize a grant.
- Run `git diff --check`.
- If Mermaid diagrams were changed, inspect the syntax manually at minimum. Do not claim rendered-diagram verification unless you actually rendered them.

## Review Output

For a review, lead with findings ordered by severity. Include file and line references. For each finding, name the violated invariant and suggest concrete wording or diagram changes.

If there are no findings, say that clearly and list any residual risk, such as diagrams not being visually rendered.
