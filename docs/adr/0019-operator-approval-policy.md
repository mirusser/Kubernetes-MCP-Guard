# ADR-0019: Operator Approval Policy as the First Sibling of Same-Subject Approval

**Date:** 2026-05-24
**Status:** Accepted

---

## Context

CONTEXT.md defines **Approval Policy** as "the rule that determines which **Approvers** may record an approved or denied **Challenge Outcome** for a **Plan Envelope**." Until now, only one **Approval Policy** has been implemented: **Same-Subject Approval**, requiring the **Approver** to be the same authenticated subject as the **Requester**. The gateway enforces it at grant validation time (`ApprovalReasonCodes.SameSubjectRequired`).

The **Remediation Planner** (ADR-0017) is a machine identity (`service:planner`). The **Plan Envelopes** it creates through `propose_plan` (ADR-0018) carry `Requester = service:planner`. The human operator who approves cannot be the same subject — `Same-Subject Approval` would reject every autonomous-originated plan as a matter of structure.

The profile explicitly anticipated this in its Flagged Ambiguities: *"`same-subject approval` was treated as the only approval model — resolved: **Same-Subject Approval** is the default Approval Policy, not the only possible policy."* Operator Approval Policy is the first concrete sibling.

Three shapes were considered for the new policy:

- **(i) Operator Approval Policy** — Plan declares an operator group name. Any subject in that Keycloak group may approve. One configured operator email address receives the notification.
- **(ii) Delegated Approval Policy** — Plan declares an explicit list of approver subjects. Per-plan flexibility; per-plan state. The Planner has to know who to delegate to.
- **(iii) Multi-Party Approval Policy** — Plan declares N-of-M thresholds. Genuine extension of the Approval Challenge lifecycle: a single challenge would have to accept multiple non-terminal outcomes before resolving.

Option (iii) is a real generalisation but a large lifecycle change — Approval Challenge currently records exactly one Challenge Outcome. N-of-M requires the lifecycle to model partial approvals before terminal resolution. Worth doing later; not justified by the current set of use cases.

Option (ii) is more flexible than (i) but requires the Planner to *know* who the approvers are per plan. In practice the operational model is "the on-call ops person approves" — a group, not a named individual. (ii) would degrade to "the list is `[the current on-call op]`" for every plan, with no leverage gained for the per-plan flexibility.

Option (i) matches the actual operational model and is the smallest generic-core extension. The deletion test confirms it: removing per-plan approver-list state in favour of a group reference loses no concrete capability today.

## Decision

**Operator Approval Policy** is added as a new `ApprovalPolicy` subtype.

The Plan Envelope's `ApprovalPolicy` field becomes a tagged union — `SameSubject` (existing default for human-driven plans) or `OperatorApproval { GroupName: string }` (new, used by `propose_plan`-originated plans). Grant validation gains a per-variant branch:

- `SameSubject` — existing check: `Approver.Subject == Requester.Subject`.
- `OperatorApproval` — new check: `Approver.Groups.Contains(policy.GroupName)`, sourced from the Keycloak `groups` claim on the approver's JWT.

`propose_plan` (ADR-0018) always declares `OperatorApproval { GroupName = configured-operator-group }`. The Planner does not pick the policy per call — it is unconditional for autonomous-originated plans.

The configured group name lives in gateway configuration (env `INFRA_GATE_OPERATOR_GROUP`, default `kubernetes-operators`). The configured operator email lives in `INFRA_GATE_OPERATOR_EMAIL`. Both are gateway-side configuration, not Planner-side — the Planner has no operational knowledge of who its approvers are.

Pre-execution Gate 4 (Authorization Check) for `OperatorApproval` plans is unchanged in shape — `SameSubjectAuthorizationCheck` is replaced with a per-policy `IAuthorizationCheck` resolver. The check still asks "is this caller authorised to execute this plan?" — the answer for `OperatorApproval` is "yes if the caller is the registered Executor Service Identity for plans whose approver was in the operator group." That separation keeps the **Approval Policy** ("who approves") distinct from the **Authorization Check** ("who executes") per CONTEXT.md.

## Consequences

- **Generic-core schema extension.** The Plan Envelope's `ApprovalPolicy` becomes a tagged union; `ApprovalCanonicalJson` includes the variant in the Review Digest canonicalisation. `ApprovalGrantValidation` gains a per-variant branch.
- **Persistence change.** `PostgresApprovalPersistence` schema gains a `policy_kind` discriminator column plus policy-specific columns (initially `operator_group`). In-memory `ApprovalStore` mirrors the same shape.
- **Approval Page renderer is touched.** `IApprovalPageRenderer` shows the policy summary on the review page — "Operator approval (group `kubernetes-operators`)" instead of "Same-subject approval" — so the approver can see what's required of them.
- **Audit Spine entries gain a `policy.kind` field on relevant events** (`plan.created`, `grant.issued`, `pre_execution.grant.validated`). Existing entries are forward-compatible — old entries default to `same-subject` on read.
- **Same-Subject Approval remains the default** for plans created via `request_*` (human path). No regression.
- **Approver still authenticates via Keycloak.** Operator Approval Policy is about *who may approve*, not about how the approver proves identity. The **Approval Access Code** (a UX routing token, not authentication) is orthogonal.
- **Delegated Approval and Multi-Party Approval are future extension points.** Each adds a new tag to the `ApprovalPolicy` union without disturbing the existing variants. Multi-Party also requires Approval Challenge lifecycle changes (multiple non-terminal outcomes).
- **Decision *would* be wrong** if real operational practice turned out to need per-plan named approvers (e.g., per-namespace ownership, per-blast-radius escalation chains). The judgement that justifies starting with `OperatorApproval` is that today's deployment model is a single operator group and adding **Delegated Approval Policy** later is purely additive.
- **Reversal cost** of choosing `OperatorApproval` over `DelegatedApproval` is small: adding `DelegatedApproval` later is one new variant; converting *between* variants for a single plan is not required because the policy is fixed at Plan Envelope creation.
