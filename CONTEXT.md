# InfraGate

InfraGate explores human approval for high-risk MCP mutations. Its language separates the generic approval profile from the domain-specific adapter that explains and executes a mutation.

## Core flow

```text
Plan Envelope → Approval Challenge → Approval Grant → Execution Attempt
```

## Language

**Mutation Intent**:
The exact domain-specific operation that an executor may later perform after approval.
_Avoid_: Plan payload, request body

**Domain Adapter**:
The domain-specific participant that defines, explains, and executes **Mutation Intents** for one target system.
_Avoid_: Generic approval core

**Kubernetes Adapter**:
The first **Domain Adapter**, responsible for Kubernetes **Mutation Intents** and Kubernetes **Plan Evidence**.
_Avoid_: Generic Approval Core, Approval Authority

**Generic Approval Core**:
The domain-independent approval layer that owns envelope schema, lifecycle state, digest checks, approval challenges, approval grants, audit spine, review snapshot canonicalization, and pre-execution gate orchestration.
_Avoid_: Domain Adapter, domain-specific review content

**Mutation Approval Profile**:
The proposed MCP profile for binding human approval to exact mutation intent and approved review context.
_Avoid_: Standard

**Experimental Reference Implementation**:
The current public positioning for InfraGate as a working implementation that explores a possible **Mutation Approval Profile**.
_Avoid_: Finished standard implementation

**Plan Evidence**:
Domain-specific review material that helps a human understand a **Mutation Intent**.
_Avoid_: Generic plan, approval data

**Evidence Artifact**:
A single piece of **Plan Evidence**, such as a diff, dry-run result, policy finding, preview, cost estimate, or rollback note.
_Avoid_: Plan Envelope field

**Redacted Evidence**:
**Plan Evidence** that intentionally hides sensitive parts of a **Mutation Intent** while disclosing that hiding to the **Approver**.
_Avoid_: Hidden plan, omitted data

**Review Surface**:
The trusted human-facing surface that renders the immutable review snapshot identified by the **Review Digest**.
_Avoid_: MCP client summary, model-generated plan

**Requester**:
The authenticated subject on whose behalf a **Mutation Intent** is proposed.
_Avoid_: Approver, MCP client

**Approver**:
The authenticated subject that records an approval decision for an **Approval Challenge**.
_Avoid_: Requester, reviewer

**Approval Authority**:
The participant that creates **Approval Challenges**, enforces **Approval Policies**, records approval decisions, and exposes **Approval Grants** for execution.
_Avoid_: Gateway, approval store, workflow system

**Approval Grant**:
A durable approval result issued by the **Approval Authority** after a successful **Approval Challenge**.
_Avoid_: Approval flag, challenge status

**Audit Trail**:
The chronological record of approval-profile lifecycle events for **Plan Envelopes**, **Approval Challenges**, and **Execution Attempts**.
_Avoid_: Log file, Kubernetes event

**Audit Spine**:
The generic lifecycle event sequence required to prove approval-bound execution across **Domain Adapters**.
_Avoid_: Adapter audit schema

**Adapter Audit Payload**:
Domain-specific audit data attached to an **Audit Spine** event.
_Avoid_: Generic audit fields

**Approval Policy**:
The rule that determines which **Approvers** may decide an **Approval Challenge** for a **Plan Envelope**.
_Avoid_: OAuth scope, RBAC rule

**Authorization Check**:
A separate decision that an actor or system is permitted to request or execute a class of operation.
_Avoid_: Approval Policy, approval decision

**Same-Subject Approval**:
An **Approval Policy** requiring the **Approver** to be the same authenticated subject as the **Requester**.
_Avoid_: Self-approval

**Execution Reuse Policy**:
The rule that determines how many successful executions an approved **Plan Envelope** may authorize.
_Avoid_: Retry policy, challenge status

**Single-Execution Plan**:
A **Plan Envelope** whose approval may authorize at most one successful execution.
_Avoid_: One-shot challenge

**Reusable Plan**:
A **Plan Envelope** whose approval may authorize more than one successful execution under an explicit **Execution Reuse Policy**.
_Avoid_: Default plan, replayable plan

**Execution Attempt**:
One attempt by a **Domain Adapter** to execute an approved **Mutation Intent**.
_Avoid_: Approval challenge, replay

**Approval-Bound Execution**:
Execution that may mutate a target system only after approval and all required pre-execution checks are valid.
_Avoid_: Approved means applied

**Pre-Execution Gate**:
A required check evaluated immediately before an **Execution Attempt** may mutate the target system.
_Avoid_: Approval decision

**Freshness Check**:
A single pre-execution condition that decides whether an approved **Mutation Intent** is still based on acceptable current state.
_Avoid_: Freshness Policy, Kubernetes dry-run only

**Freshness Policy**:
A generic wrapper containing zero or more **Freshness Checks** declared by a **Plan Envelope**.
_Avoid_: Single drift check

**Domain Policy Check**:
An adapter-owned decision about whether a **Mutation Intent** is acceptable for the target system.
_Avoid_: Approval Policy, Authorization Check

**Canonicalization**:
The declared rule for turning approval-bound data into deterministic bytes before computing a digest.
_Avoid_: Serialization format, stored file bytes

**Intent Digest**:
A digest over the executable **Mutation Intent** that approval-bound execution must verify.
_Avoid_: Plan hash, payload hash

**Review Digest**:
A digest over the immutable review snapshot presented for human approval, including **Plan Envelope** metadata, **Evidence Artifact** digests or digest-bound references, redaction metadata, and review-surface context.
_Avoid_: Evidence hash, UI hash

**Plan Envelope**:
A generic wrapper that identifies and binds a **Mutation Intent** for approval without defining the domain-specific operation itself.
_Avoid_: Approval lifecycle object, Kubernetes plan

**Plan Identifier**:
An opaque stable handle for addressing a **Plan Envelope** across MCP calls, approval URLs, audit correlation, and storage.
_Avoid_: Integrity hash, deterministic plan id

**Plan Validity Window**:
The time period during which a **Plan Envelope** may still be approved or executed.
_Avoid_: Challenge TTL, approval URL expiry

**Approval Challenge**:
A short-lived approval attempt bound to one **Plan Envelope** and one approval decision path.
_Avoid_: Plan, approval URL

**Challenge TTL**:
The short lifetime of an **Approval Challenge** before that approval attempt expires.
_Avoid_: Plan expiry, plan validity

## Relationships

- A **Plan Envelope** wraps exactly one **Mutation Intent**
- A **Plan Envelope** has exactly one **Plan Identifier**
- A **Plan Envelope** records exactly one **Requester**
- A **Plan Envelope** declares one **Approval Policy** object
- A **Plan Envelope** declares one **Execution Reuse Policy** object
- A **Plan Envelope** declares one **Freshness Policy**
- A **Plan Envelope** may include **Domain Policy Check** results as **Plan Evidence**
- A **Plan Envelope** has exactly one **Plan Validity Window**
- A **Plan Envelope** carries exactly one **Intent Digest**
- A **Plan Envelope** carries exactly one **Review Digest**
- A **Review Digest** covers the **Requester**, **Approval Policy**, **Execution Reuse Policy**, **Freshness Policy**, **Plan Validity Window**, **Intent Digest**, **Evidence Artifact** digests or digest-bound references, redaction metadata, and review-surface context
- An **Intent Digest** declares its **Canonicalization**
- A **Review Digest** declares its **Canonicalization**
- A **Plan Envelope** may include or reference **Evidence Artifacts**
- **Plan Evidence** may be **Redacted Evidence**
- A **Review Surface** renders the immutable review snapshot identified by the **Review Digest**, not model-supplied approval content
- A **Plan Envelope** may produce one or more **Approval Challenges**
- An **Approval Challenge** may produce one **Approval Grant**
- An **Approval Grant** is bound to one **Plan Envelope**
- An **Approval Grant** records one **Approver**
- An **Approval Grant** is bound to the **Plan Identifier**, **Intent Digest**, **Review Digest**, **Approval Policy**, expiry, and reuse constraints
- An **Approval Challenge** does not define the **Mutation Intent**
- An **Approval Authority** creates and decides **Approval Challenges**
- An **Audit Trail** records the lifecycle of **Plan Envelopes**, **Approval Challenges**, and **Execution Attempts**
- An **Audit Spine** defines the generic lifecycle events in an **Audit Trail**
- An **Adapter Audit Payload** may be attached to an **Audit Spine** event
- An **Authorization Check** is separate from an **Approval Policy**
- An **Authorization Check** may gate creation of a **Plan Envelope** or creation of an **Execution Attempt**
- A **Domain Policy Check** is separate from an **Approval Policy** and an **Authorization Check**
- An **Approval Challenge** enforces the **Approval Policy** declared by its **Plan Envelope**
- An **Approval Challenge** has one **Challenge TTL** bounded by the **Plan Validity Window**
- A **Plan Envelope** may have one or more **Execution Attempts**
- **Approval-Bound Execution** requires **Pre-Execution Gates** after approval and before mutation
- **Pre-Execution Gates** include a valid **Approval Grant**, **Intent Digest**, **Review Digest**, **Plan Validity Window**, **Authorization Check**, **Execution Reuse Policy**, **Freshness Policy**, and required **Domain Policy Checks**
- The **Generic Approval Core** owns the approval lifecycle independent of **Domain Adapters**
- The **Generic Approval Core** owns **Canonicalization** for **Plan Envelope** metadata and the **Review Digest**
- The **Generic Approval Core** does not define **Mutation Intents** or domain-specific **Plan Evidence**
- The **Generic Approval Core** may host a **Review Surface** that renders adapter-provided **Evidence Artifacts**
- InfraGate is currently an **Experimental Reference Implementation** for a possible **Mutation Approval Profile**
- The **Kubernetes Adapter** is a **Domain Adapter**
- The **Kubernetes Adapter** owns Kubernetes mutation meaning, safety evidence, mutation-intent canonicalization, freshness checks, domain policy checks, execution behavior, and adapter audit payloads
- The **Kubernetes Adapter** does not own approval challenge creation or **Approval Policy** enforcement
- A **Domain Adapter** defines **Canonicalization** for its **Mutation Intent**
- A **Domain Adapter** defines and verifies the checks in its **Freshness Policy**
- A **Domain Adapter** defines and verifies **Domain Policy Checks**
- A **Domain Adapter** owns retry semantics for non-successful **Execution Attempts**
- An **Execution Reuse Policy** constrains successful **Execution Attempts**
- A **Single-Execution Plan** is the default **Execution Reuse Policy**
- A **Reusable Plan** is an explicit opt-in exception to **Single-Execution Plan**

## Example Dialogue

> **Dev:** "Does the **Plan Envelope** describe how to scale a Kubernetes Deployment?"
> **Domain expert:** "No — the Kubernetes adapter owns that **Mutation Intent**. The **Plan Envelope** only gives it identity, binding, and approval-facing metadata."

## Flagged Ambiguities

- "plan envelope" was used both for the generic approval wrapper and for the whole approval lifecycle — resolved: **Plan Envelope** means the generic wrapper around a domain-specific **Mutation Intent**.
- "plan hash" was used for both executable intent binding and human review binding — resolved: use **Intent Digest** for execution and **Review Digest** for the approval review snapshot.
- "requester" was treated as challenge-only state — resolved: the **Plan Envelope** records the **Requester** and the **Review Digest** covers that identity.
- "same-subject approval" was treated as the only approval model — resolved: **Same-Subject Approval** is the default **Approval Policy**, not the only possible policy.
- "TTL" was used for both plan staleness and approval URL expiry — resolved: use **Plan Validity Window** for the plan and **Challenge TTL** for each approval attempt.
- "replay prevention" was used as if all plans must be one-shot — resolved: **Single-Execution Plan** is the default, while **Reusable Plan** remains an explicit opt-in extension point.
- "retry" was treated as generic replay behavior — resolved: **Domain Adapters** own retry semantics for non-successful **Execution Attempts**.
- "freshness" was treated as Kubernetes-style dry-run and drift detection — resolved: **Freshness Policy** is declared generically, while **Freshness Checks** are defined by the **Domain Adapter**.
- "human-visible evidence" was treated as if it always shows the full mutation — resolved: **Redacted Evidence** is allowed when redaction metadata is included in the **Review Digest**.
- "digest input" was treated as raw stored file bytes — resolved: every digest declares its **Canonicalization**, with **Domain Adapters** owning intent canonicalization and the **Generic Approval Core** owning review canonicalization.
- "approval authority" was treated as the gateway implementation — resolved: **Approval Authority** is a generic role that may be implemented by the gateway, MCP server, or an external workflow system.
- "audit trail" was treated as Kubernetes-specific event records — resolved: the generic profile defines an **Audit Spine** and allows **Adapter Audit Payloads**.
- "authorization" was treated as if it were approval policy — resolved: **Authorization Check** is separate from **Approval Policy** and approval decisions.
- "policy validation" was treated as approval policy — resolved: **Domain Policy Check** is adapter-owned and separate from **Approval Policy** and **Authorization Check**.
- "approved" was treated as sufficient to execute — resolved: **Approval-Bound Execution** still requires **Pre-Execution Gates** immediately before mutation.
- "approval" was treated as a boolean flag — resolved: a successful challenge produces or references an **Approval Grant** bound to the **Plan Identifier**, **Intent Digest**, **Review Digest**, **Approver**, policy, expiry, and reuse constraints.
- "plan id" was treated as a possible integrity mechanism — resolved: **Plan Identifier** is opaque workflow identity; **Intent Digest** proves same executable intent and **Review Digest** proves same reviewed snapshot.
- "generic core" was treated as if it might be only terminology — resolved: **Generic Approval Core** owns the reusable lifecycle, while **Domain Adapters** own mutation meaning and evidence.
- "Kubernetes adapter" was treated as if it might own approval lifecycle behavior — resolved: **Kubernetes Adapter** owns Kubernetes mutation meaning and evidence, while **Generic Approval Core** owns approval lifecycle behavior.
- "approval page" was treated as a UI detail only — resolved: **Review Surface** rendering is implementation-specific, but it must present the trusted **Review Digest** snapshot instead of model-supplied approval content.
- "standard" was too strong for current positioning — resolved: InfraGate is currently an **Experimental Reference Implementation** for a possible **Mutation Approval Profile**.
