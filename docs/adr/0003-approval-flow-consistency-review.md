# Review Approval-Flow Docs For Consistency; Fix Stale Terminology

## Context

`docs/` contains two layers of documentation:

- **Target profile docs** (`CONTEXT.md`, `mutation-approval-profile.md`, `mutation-approval-flow.md`, ADR 0001, ADR 0002) that define canonical terminology and the target architectural shape (InfraGate, Generic Approval Core, Domain Adapter, Plan Envelope, Intent Digest, Review Digest, Challenge Outcome, Approval Grant, etc.).
- **Operational docs** (`architecture.md`, `devs-readme.md`, `security-model.md`, `demo-failing-deployment.md`, `why-separated-plan-from-challenge.md`, `MCP-compliance.md`, `tool-permissions.md`, `releasing.md`, `roadmap.md`) that describe the current implementation and developer workflows.

A full consistency review was performed against the skill checklist in `.agents/skills/review-mutation-approval-flow/SKILL.md`. The review found that the target profile docs are internally consistent but the operational docs lag significantly behind the canonical vocabulary.

## Decision

### Previously accepted drift now resolved

The codebase no longer uses an approved hash file as execution authorization. Pending-plan hashes remain for approval-challenge drift detection, but execution now consumes an **Approval Grant** bound to separate **Intent Digest** and **Review Digest** values.

### Fix now (mechanical renames)

The following will be corrected in the operational docs:

1. **"Kubernetes MCP Guard" → "InfraGate"** wherever the project name appears as a product identifier.
2. **"single-use" → "Single-Execution"** or `**Single-Execution Plan**` in approval-lifecycle descriptions.
3. **"plan hash" → "pending-plan hash"** where the text specifically describes approval-challenge drift detection; execution documentation should use **Intent Digest**, **Review Digest**, and **Approval Grant**.

### Already correct (no changes)

The target profile docs (`CONTEXT.md`, `mutation-approval-profile.md`, `mutation-approval-flow.md`, ADR 0001, ADR 0002) are internally consistent across the following invariants:

- Challenge Outcome / Approval Grant split: outcome is audit record; grant is execution authorization
- Challenge Outcome does not authorize execution
- Generic Approval Core vs Domain Adapter ownership boundaries
- Same-Subject Approval as default (not only) policy
- Plan Validity Window ≠ Challenge TTL ≠ Grant Expiry
- Approval is necessary but not sufficient (pre-execution gates still required)
- Opaque Plan Identifier is workflow identity, not an integrity mechanism
- Review Digest canonicalization owned by Generic Core; Intent Digest canonicalization owned by Domain Adapter
- All scenario flows (happy path, denied, expired, stale, failed) correctly exercise invariants

## Consequences

- Operational docs will carry the old name "Kubernetes MCP Guard" until renamed to "InfraGate" in a follow-up pass.
- Operational docs should now describe the Intent/Review Digest model and Approval Grants for execution authorization.
- "single-use" → "Single-Execution Plan" renames can be applied immediately without waiting for code changes.
- `why-separated-plan-from-challenge.md` will need a substantial rewrite after the two-digest migration lands, since its table and code excerpts will become stale.
- Architecture diagram (`docs/architecture.md`) will need an update to reflect the Generic Core / Domain Adapter boundary after the code split.

## Cross-References

- ADR 0001: Separate Generic Approval Core From Domain Adapters
- ADR 0002: Use Opaque Plan Identifiers And Separate Digests
- `docs/mutation-approval-flow.md`: Diagrams, relationship table, scenarios
- `docs/mutation-approval-profile.md`: Profile narrative and minimum envelope shape
- `docs/roadmap.md`: Near-term work and implementation direction
