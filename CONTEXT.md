# InfraGate

InfraGate explores human approval for high-risk MCP mutations. Its language separates the generic approval profile from the domain-specific adapter that explains and executes a mutation.

## General Idea

```text
- AI proposes.
- Domain adapter turns it into exact mutation intent + evidence.
- Human approves out-of-band.
- Approval is bound to intent/review digests.
- Execution verifies the grant immediately before mutation.
```

## Core flow

```text
Plan Envelope → Approval Challenge
Approval Challenge → Challenge Outcome (audit record)
Approval Challenge → Approval Grant (approved only) → Execution Attempt
```

## Language

**Mutation Approval Profile**:
The proposed MCP profile for binding human approval to exact mutation intent and approved review context.
_Avoid_: Standard

**Experimental Reference Implementation**:
The current public positioning for InfraGate as a working implementation that explores a possible **Mutation Approval Profile**.
_Avoid_: Finished standard implementation

**Run Profile**:
The authoring record for one target runnable environment. It declares the environment's runtime shape without owning CI/CD triggers, job ordering, or release policy.
_Avoid_: CI workflow, environment variable dump, secret store

**Generic Approval Core**:
The domain-independent approval layer that owns envelope schema, lifecycle state, digest checks, approval challenges, challenge outcomes, approval grants, audit spine, review snapshot canonicalization, and pre-execution gate orchestration.
_Avoid_: Domain Adapter, domain-specific review content

**Domain Adapter**:
The domain-specific participant that defines, explains, and executes **Mutation Intents** for one target system.
_Avoid_: Generic approval core

**Kubernetes Adapter**:
The first **Domain Adapter**, responsible for Kubernetes **Mutation Intents** and Kubernetes **Plan Evidence**.
_Avoid_: Generic Approval Core, Approval Authority

**Approval Authority**:
The participant that creates **Approval Challenges**, enforces **Approval Policies**, records **Challenge Outcomes**, and issues or exposes **Approval Grants** for execution.
_Avoid_: Gateway, approval store, workflow system

**Identity Provider**:
The external OAuth/OIDC authority that authenticates **Requesters** and **Approvers** and issues JWTs consumed by the gateway.
_Avoid_: Approval Authority, Gateway

**Gateway Service Identity**:
The machine identity used by the gateway when it calls a private downstream domain server.
_Avoid_: Requester, Approver, user token passthrough

**Mutation Intent**:
The exact domain-specific operation that an executor may later perform after approval.
_Avoid_: Plan payload, request body

**Plan Envelope**:
A generic wrapper that identifies and binds a **Mutation Intent** for approval without defining the domain-specific operation itself.
_Avoid_: Approval lifecycle object, Kubernetes plan

**Plan Identifier**:
An opaque stable handle for addressing a **Plan Envelope** across MCP calls, approval URLs, audit correlation, and storage.
_Avoid_: Integrity hash, deterministic plan id

**Requester**:
The authenticated subject on whose behalf a **Mutation Intent** is proposed.
_Avoid_: Approver, MCP client

**Approval Policy**:
The rule that determines which **Approvers** may record an approved or denied **Challenge Outcome** for a **Plan Envelope**.
_Avoid_: OAuth scope, RBAC rule

**Same-Subject Approval**:
An **Approval Policy** requiring the **Approver** to be the same authenticated subject as the **Requester**. Used for **Plan Envelopes** originated by human-driven MCP clients. For autonomous-originated plans, see **Operator Approval Policy**.
_Avoid_: Self-approval, Operator Approval Policy

**Plan Validity Window**:
The outer time period during which a **Plan Envelope** may participate in approval or execution, subject to **Challenge TTL**, **Approval Grant** expiry, reuse constraints, and **Freshness Policy**.
_Avoid_: Challenge TTL, approval URL expiry

**Execution Reuse Policy**:
The rule that determines how many successful executions an approved **Plan Envelope** may authorize.
_Avoid_: Retry policy, challenge status

**Single-Execution Plan**:
A **Plan Envelope** whose approval may authorize at most one successful execution.
_Avoid_: One-shot challenge

**Reusable Plan**:
A **Plan Envelope** whose approval may authorize more than one successful execution under an explicit **Execution Reuse Policy**.
_Avoid_: Default plan, replayable plan

**Freshness Policy**:
A generic wrapper containing zero or more **Freshness Checks** declared by a **Plan Envelope**.
_Avoid_: Single drift check

**Freshness Check**:
A single pre-execution condition that decides whether an approved **Mutation Intent** is still based on acceptable current state.
_Avoid_: Freshness Policy, Kubernetes dry-run only

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
The trusted human-facing surface that renders the immutable review snapshot identified by the **Review Digest** to the **Approver**.
_Avoid_: MCP client summary, model-generated plan

**Approval Challenge**:
A short-lived approval attempt bound to one **Plan Envelope** and one challenge outcome path.
_Avoid_: Plan, approval URL

**Challenge TTL**:
The short lifetime of an **Approval Challenge** before that approval attempt expires.
_Avoid_: Plan expiry, plan validity

**Approver**:
The authenticated subject permitted by an **Approval Policy** to record an approved or denied **Challenge Outcome** for an **Approval Challenge**.
_Avoid_: Requester, reviewer

**Challenge Outcome**:
The terminal record of an **Approval Challenge**, such as approved, denied, rejected, expired, or canceled. It records outcome time and the authenticated subject or system actor that produced it when applicable, but it does not authorize execution.
_Avoid_: Approval Grant, challenge status, execution authorization

**Approval Grant**:
A durable execution authorization issued or exposed by the **Approval Authority** when an **Approval Challenge** is approved.
_Avoid_: Approval flag, challenge status, Challenge Outcome

**Authorization Check**:
A separate decision that an actor or system is permitted to request or execute a class of operation.
_Avoid_: Approval Policy, Challenge Outcome

**Pre-Execution Gate**:
A required check evaluated immediately before an **Execution Attempt** may mutate the target system.
_Avoid_: Challenge Outcome

**Approval-Bound Execution**:
Execution that may mutate a target system only after approval and all required pre-execution checks are valid.
_Avoid_: Approved means applied

**Execution Attempt**:
One attempt by a **Domain Adapter** to execute an approved **Mutation Intent**.
_Avoid_: Approval challenge, replay

**Audit Trail**:
The chronological record of approval-profile lifecycle events for **Plan Envelopes**, **Approval Challenges**, **Challenge Outcomes**, **Approval Grants**, and **Execution Attempts**.
_Avoid_: Log file, Kubernetes event

**Audit Spine**:
The generic lifecycle event sequence required to prove approval-bound execution across **Domain Adapters**, including grant validation, adapter pre-execution checks, execution start, blocked execution, failed execution, and successful execution.
_Avoid_: Adapter audit schema

**Adapter Audit Payload**:
Domain-specific audit data attached to an **Audit Spine** event under an adapter-owned payload slot.
_Avoid_: Generic audit fields

**Audit Stream**:
The per-component, append-only audit record for one runtime component (currently **Approval Authority**, **Anomaly Observer**, **Remediation Planner**), written transactionally with the state change it describes. Each **Audit Stream** carries its own tamper-evident hash chain over its own rows and outbox-shape state fields so rows can later be published to an external sink without schema rewrites. Streams are correlated across components by IDs (`plan_id`, `anomaly_id`, `challenge_id`, `grant_id`, `cycle_id`) — not by a shared hash chain. The **Generic Approval Core** owns the **Approval Authority** stream; **Anomaly Observer** and **Remediation Planner** each own their own stream.
_Avoid_: **Audit Trail** (currently scoped to approval-lifecycle only), **Adapter Audit Payload** (the domain-specific JSON inside one event), unified ledger across components

**Notification Registry**:
The in-memory mapping from active MCP session identifiers to their notification targets and from plan URIs to subscribed session sets, used to route **Approval Notifications** back to the right AI agent sessions.
_Avoid_: Session store, connection pool

**Approval Notification**:
A server-to-client MCP `notifications/resources/updated` message sent when an **Approval Challenge** is approved, carrying the plan status resource URI so the AI agent's host can read the updated status and retry execution without manual prompting.
_Avoid_: Push event, callback

**Prompt Library**:
A shared module exposing a single interface (`IPromptLibrary`) that loads named prompt-template assets and renders them with typed, validated arguments using Semantic Kernel as a renderer.
_Avoid_: Ad-hoc prompt loader, manual string replacement

**Prompt Template**:
A structured asset (e.g., Handlebars) containing LLM instructions and placeholders, rendered by the **Prompt Library** before being passed to an agent.
_Avoid_: Static markdown file, unvalidated string template

**Agent MCP Toolset**:
The shared `IAgentMcpToolset` abstraction in `InfraGate.AgentMcp` used by AI agents (Observer, Planner) to connect to the MCP Gateway. It enforces visibility filtering based on `ReadOnlyHint` annotations and abstracts the connection lifecycle.
_Avoid_: McpClient, direct gateway transport


### Guardrails

**Tool-Call Guardrail**:
A framework function-invocation middleware (`AIAgentBuilder.Use(...)`) in `InfraGate.AgentGuardrails` that enforces an explicit, caller-declared tool-name allow-list at invocation time. Every agent tool call must be in the allow-list; disallowed calls are blocked, metered, and not executed. Owned by the guardrail module and composed into the shared `ToolCallingAgentFactory` seam so both Observer and Planner agents get it.
_Avoid_: ReadOnlyHint filtering, Gateway tool-permission check

**Guardrail Metric**:
A consolidated `Counter<long>` on the `InfraGate.AgentGuardrails` meter that records every guardrail outcome — tool blocks at the agent layer and decision rejections at the workflow layer — with a reason tag (`tool_not_allowed`, `invalid_operation`, `invalid_arguments`, `dedupe_in_batch`). The two formerly bespoke Planner counters (`infragate.planner.decision.invalid_operation`, `infragate.planner.decision.invalid_arguments`) were removed in favour of this single reason-tagged counter.
_Avoid_: PlannerMetrics, ObserverMetrics, ad-hoc counter

**Hallucination Rate**:
The decision-layer ratio `rejected{reason=invalid_operation,invalid_arguments} / (accepted+rejected)` computed from the **Guardrail Metric**. The `dedupe_in_batch` reason is excluded from the numerator because it represents an operational drop, not a hallucination. The tool-block rate uses `tool_call.blocked` divided by the allowed-call span count from §4's per-function spans.
_Avoid_: Agent error rate, task failure rate

**Model-Visible Content Guard**:
The `IModelVisibleContentGuard` seam in `InfraGate.AgentGuardrails` that evaluates text before LLM ingestion. Composed into `SnapshotExecutor` (Observer) and `DecideExecutor` (Planner). Returns one of four actions: `Allow` (pass through), `Redact` (replace with safe placeholder), `Quarantine` (withhold content, send placeholder, record forensic digest), or `BlockModelIngestion` (skip LLM entirely, record forensic digest).
_Avoid_: Tool-call guardrail, gateway permission check

**Model-Visible Content**:
Any text consumed by an LLM that passes through the **Model-Visible Content Guard** — snapshot JSON, anomaly JSON, tool result text.
_Avoid_: Prompt template, system prompt, tool name

**Quarantine**:
A **Model-Visible Content Guard** action that replaces suspicious content with a bounded safe placeholder and records a SHA-256 digest for forensic investigation. The original text is never sent to the LLM.
_Avoid_: Block, Redact, Reject


### Anomaly Observation

**Anomaly Observer**:
A non-human MCP client that periodically inspects target-system state through the gateway's read-only tools and emits structured **Anomaly Reports**. The **Anomaly Observer** is a peer MCP client, not a participant in the approval lifecycle.
_Avoid_: Approver, Requester, Domain Adapter, generic monitoring system

**Anomaly**:
A condition in the target system that deviates from a defined healthy state in a way the **Anomaly Observer** can detect through gateway read-only tools. Initial examples include Pod `CrashLoopBackOff`, Deployment with `availableReplicas < spec.replicas`, Service with zero endpoints, and recent Warning events from `events.k8s.io/v1`.
_Avoid_: Application error, performance trend, security incident

**Observation Cycle**:
One iteration of the **Anomaly Observer's** loop: **Snapshot** fetch, analysis (optionally with deep-dive read-only tool calls), **Anomaly Report** emission, and deduplication against the previous cycle.
_Avoid_: Kubernetes reconciliation loop, Kubernetes watch

**Snapshot**:
The aggregated read-only state captured at the start of an **Observation Cycle** before analysis. The **Snapshot** is the deterministic input to a single **Observation Cycle's** analysis step.
_Avoid_: Plan Evidence, audit record

**Detection Rule**:
A documented condition under which the **Anomaly Observer** classifies a **Snapshot** signal as an **Anomaly**. **Detection Rules** live in the **Anomaly Observer's** system prompt and supporting code; they are not part of the **Generic Approval Core**.
_Avoid_: Approval Policy, Domain Policy Check, Pre-Execution Gate

**Anomaly Report**:
A single **Anomaly** reported by an **Observation Cycle**, carrying anomaly kind, target resource reference, **Severity**, **Status** (`Active` or `Resolved`), evidence summary, and detection timestamp. An **Anomaly Report** is informational; it does not authorize any mutation.
_Avoid_: Plan Envelope, Approval Challenge, Audit Spine event, policy finding

**Severity**:
The **Anomaly Observer's** classification of an **Anomaly Report's** urgency. Initial scale: `High` (resource unavailable to users), `Medium` (degraded but serving), `Low` (warning signal worth noting).
_Avoid_: Approval Policy, Kubernetes event type

**Observer Service Identity**:
The machine identity the **Anomaly Observer** uses to authenticate to the gateway via OAuth client_credentials. Separate from **Requester**, **Approver**, and **Gateway Service Identity**, and not authorized for any mutation tool.
_Avoid_: Requester, Approver, Gateway Service Identity, user token passthrough

**Anomaly Handoff**:
The act of the **Anomaly Observer** publishing an **Anomaly Report** to the **Remediation Planner**. The wire contract remains `AnomalyHandoffBatch`, but the Observer sends one report per A2A message with `contextId = AnomalyId` so the Planner can create one durable **Planner Task** per anomaly. The transport uses the Agent-to-Agent (A2A) protocol via `A2AAnomalyHandoffSink` to the **Remediation Planner**'s `/a2a/planner` endpoint, authenticated by **Observer Service Identity** bearer.
_Avoid_: Plan Envelope, Approval Notification, Approval Grant, Audit Spine event

**Reverse Context Request**:
A request the **Remediation Planner**'s LLM agent sends to the **Observer Inbound Channel** (via the `ask_observer_to_inspect` AI function) when it needs current cluster state before proposing a plan. The Observer executes the named read-only MCP tool against the gateway, enforces the server-side allowed-tools whitelist (`AgentGuardrailPolicy`), and returns the result or a denial. Allowed calls are audited as `handoff.tool_served`; denied calls as `handoff.tool_denied`.
_Avoid_: reverse handoff, Observer tool proxy, push notification, agent tool call

**Observer Inbound Channel**:
The A2A server the **Anomaly Observer** hosts at `/a2a/observer` to receive **Reverse Context Requests** from the **Remediation Planner**. `ObserverInboundAgentHandler` accepts the `"tool-request"` envelope intent and rejects unknown intents. Protected by JWT Bearer + `PlannerSender` authorization policy (`azp == infra-gate-planner`).
_Avoid_: Observer A2A proxy, planner webhook, reverse subscription

**Dedupe Key**:
The tuple `(AnomalyKind, ResourceKind, Namespace, Name)` that uniquely identifies an anomaly for deduplication purposes. Two **Anomaly Reports** with the same **Dedupe Key** are considered the same underlying anomaly regardless of which **Observation Cycle** produced them.
_Avoid_: Anomaly Report primary key, resource identity

**Dedupe State**:
The in-memory `ConcurrentDictionary<DedupKey, ActiveAnomalyState>` that tracks which anomalies are currently active, their first-seen and last-seen cycle numbers, and their most recent **Severity**. The **Dedupe State** is the source of truth for flapping suppression and resolution decisions, not remediation work-in-flight idempotency.
_Avoid_: Approval grant set, audit ledger, persistent database

**Suppression Window**:
A configurable number of consecutive **Observation Cycles** (default `5`) within which repeated detection of the same anomaly is suppressed — the **Anomaly Observer** skips emitting redundant **Anomaly Reports**. After the window elapses, persistent anomalies re-emit.
_Avoid_: cooldown period, debounce interval

**Resolution Emission**:
When an active anomaly tracked in the **Dedupe State** is absent from **Anomaly Reports** for a configurable number of consecutive cycles (default `2`), the **Anomaly Observer** emits one **Anomaly Report** with `Status = Resolved` and `Severity = Low`, then removes the **Dedupe Key** from the **Dedupe State**.
_Avoid_: cleanup event, archive notification

### Remediation

**Remediation Planner**:
A non-human MCP client that consumes **Anomaly Reports** from the **Anomaly Observer**, reasons about candidate remediations with LLM assistance, and proposes one **Mutation Intent** per acted-on **Anomaly** through the gateway's `propose_plan` tool. The **Remediation Planner** does not execute mutations and does not bypass any **Pre-Execution Gate**.
_Avoid_: Approver, Approval Authority, Remediation Executor, Domain Adapter

**Planner Task**:
A **Remediation Planner**-owned durable A2A task representing one remediation work item, with `contextId = AnomalyId`. It is the authoritative work-in-flight idempotency layer: a duplicate **Anomaly Handoff** for the same context does not create or enqueue a second task. Its lifecycle is `Submitted` → `Working` → `AuthRequired` while awaiting operator approval → terminal (`Completed`, `Failed`, or `Rejected`). When `propose_plan` succeeds, the task carries the **Plan Identifier** as an artifact reference; the **Plan Envelope** remains the source of truth for the remediation decision.
_Avoid_: Plan Envelope, Approval Challenge, approval status, Observer Dedupe State

**Remediation Executor**:
A non-human MCP client that receives synchronous A2A dispatches carrying a **Plan Identifier** from the **Remediation Planner**, blocks on `wait_for_plan_approval`, calls `execute_approved_plan` only after the **Approval Grant** is issued, and returns the outcome to the Planner. The **Remediation Executor** does not produce **Plan Envelopes** and does not influence the **Challenge Outcome**.
_Avoid_: Approver, Approval Authority, Remediation Planner, `IDomainPlanExecutor`

**Operator Approval Policy**:
An **Approval Policy** subtype permitting any authenticated subject in a configured operator group to record an approved **Challenge Outcome** for a **Plan Envelope** whose **Requester** is a machine identity. Used for **Plan Envelopes** originated through `propose_plan`. Sibling of **Same-Subject Approval**, not a replacement.
_Avoid_: Same-Subject Approval, Authorization Check, Delegated Approval

**Remediation Proposal**:
A **Remediation Planner**-produced reference to one approval-pending **Plan Envelope**, carrying the **Plan Identifier**, the originating **Anomaly Identifier**, and the proposal timestamp. A **Remediation Proposal** is informational output for logging and optional file sinks; synchronous Executor dispatch carries only the **Plan Identifier**. It does not authorize execution and does not carry **Mutation Intent** content.
_Avoid_: Plan Envelope, Approval Grant, Mutation Intent, Anomaly Report

**Approval Access Code**:
A short-lived one-time-use UX token bound to one **Approval Challenge**, generated when a **Plan Envelope** is created via `propose_plan`, delivered out-of-band (email), and exchanged at the **Review Surface**'s code-entry page for a redirect to the **Approval Challenge** page. Authentication of the **Approver** remains performed by the **Identity Provider**; the **Approval Access Code** is routing, not authentication.
_Avoid_: Authentication token, OAuth code, magic-link authentication, session token

**Planner Service Identity**:
The machine identity the **Remediation Planner** uses to authenticate via OAuth client_credentials. Separate from **Requester**, **Approver**, **Gateway Service Identity**, **Observer Service Identity**, and **Executor Service Identity**. Its gateway scopes permit `propose_plan` plus read-only tools, but not execution tools.
_Avoid_: Requester, Approver, Observer Service Identity, Executor Service Identity

**Executor Service Identity**:
The machine identity the **Remediation Executor** uses to authenticate to the gateway via OAuth client_credentials. Authorized only for `wait_for_plan_approval` and `execute_approved_plan`. Separate from **Planner Service Identity** so that a compromised **Remediation Executor** cannot create new **Plan Envelopes** and a compromised **Remediation Planner** cannot execute approved plans.
_Avoid_: Planner Service Identity, Approver, Requester, Gateway Service Identity

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
- A **Review Surface** renders the immutable review snapshot identified by the **Review Digest** to the **Approver**, not model-supplied approval content
- A **Plan Envelope** may produce one or more **Approval Challenges**
- An **Approval Challenge** may be pending with no **Challenge Outcome**
- A terminal **Approval Challenge** records exactly one **Challenge Outcome**
- An approved **Approval Challenge** produces or references one **Approval Grant**
- A non-approved **Approval Challenge** does not produce an **Approval Grant**
- An **Approval Grant** is bound to one **Plan Envelope**
- An **Approval Grant** records one **Approver**
- An **Approval Grant** is bound to the **Plan Identifier**, **Requester**, **Approver**, **Intent Digest**, **Review Digest**, **Approval Policy**, expiry, and reuse constraints
- An **Approval Challenge** does not define the **Mutation Intent**
- An **Approval Authority** creates **Approval Challenges** and records **Challenge Outcomes**
- An **Identity Provider** authenticates **Requesters** and **Approvers** but does not create **Approval Challenges** or issue **Approval Grants**
- An **Identity Provider** may authenticate the **Gateway Service Identity** independently from **Requesters** and **Approvers**
- A **Gateway Service Identity** is not a **Requester** or an **Approver**
- A **Gateway Service Identity** does not replace **Approval Policy**, **Authorization Checks**, or **Approval Grants**
- A **Gateway Service Identity** authenticates private downstream calls before discovery or execution behavior is exposed
- A **Gateway Service Identity** proves the private downstream caller, not the authority to request, approve, or execute a **Mutation Intent**
- An **Audit Trail** records the lifecycle of **Plan Envelopes**, **Approval Challenges**, **Challenge Outcomes**, **Approval Grants**, and **Execution Attempts**
- An **Audit Spine** defines the generic lifecycle events in an **Audit Trail**
- An **Adapter Audit Payload** may be attached to an **Audit Spine** event
- Grant validation proof belongs to pre-execution gate audit events, not to `execution.started`
- An **Audit Stream** is owned by exactly one runtime component
- Three runtime components currently own an **Audit Stream**: the **Approval Authority**, the **Anomaly Observer**, and the **Remediation Planner**
- An **Audit Stream** carries its own tamper-evident hash chain over its own rows
- An **Audit Stream** is correlated to other **Audit Streams** by IDs, not by a shared hash chain
- An **Audit Stream** is written transactionally with the state mutation it describes, when one exists
- The **Approval Authority**'s **Audit Stream** is the persistent representation of the **Audit Trail**
- The **Anomaly Observer**'s **Audit Stream** does not extend the **Audit Spine** and does not produce **Audit Spine** events
- The **Remediation Planner**'s **Audit Stream** does not extend the **Audit Spine** and does not produce **Audit Spine** events
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
- The **Generic Approval Core** may host a **Review Surface**, but **Domain Adapters** supply the domain-specific **Evidence Artifacts** rendered there
- InfraGate is currently an **Experimental Reference Implementation** for a possible **Mutation Approval Profile**
- A **Run Profile** describes one target runnable environment
- A **Run Profile** does not own CI/CD triggers, job ordering, or release policy
- A **Run Profile** may declare one or more **Domain Adapters**
- InfraGate run profiles currently support only one **Domain Adapter**: the **Kubernetes Adapter**
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

### Anomaly Observation

- An **Anomaly Observer** runs zero or more **Observation Cycles**
- An **Observation Cycle** reads exactly one **Snapshot** and produces zero or more **Anomaly Reports**
- An **Anomaly Report** describes exactly one **Anomaly**
- An **Anomaly Report** carries exactly one **Severity**
- An **Anomaly Report** carries exactly one **Status** (`Active` or `Resolved`)
- An **Anomaly Report** is produced by one or more **Detection Rules**
- An **Anomaly Observer** authenticates with exactly one **Observer Service Identity**
- An **Observer Service Identity** is not a **Requester** or an **Approver**
- An **Observer Service Identity** does not produce **Plan Envelopes**, **Approval Grants**, or **Challenge Outcomes**
- An **Anomaly Observer** does not bypass any **Pre-Execution Gate** and does not call execution tools
- An **Anomaly Handoff** carries exactly one **Anomaly Report** per A2A message
- An **Anomaly Handoff** is not an **Approval Grant** and does not authorize **Approval-Bound Execution**
- An **Anomaly Report** emission may be suppressed by the **Suppression Window** in the **Dedupe State**
- An **Anomaly Report** emission may be a **Resolution Emission** when the tracked anomaly is absent beyond the resolution threshold
- A **Dedupe Key** uniquely identifies an **Anomaly** within the **Dedupe State**
- The **Dedupe State** tracks active anomalies across **Observation Cycles** and drives the **Suppression Window** and **Resolution Emission**

### Remediation

- A **Remediation Planner** consumes **Anomaly Reports** from the **Anomaly Observer** through the **Anomaly Handoff**
- A **Remediation Planner** produces zero or more **Remediation Proposals** per `AnomalyHandoffBatch`
- A **Remediation Planner** owns exactly one **Planner Task** per `contextId = AnomalyId`
- A **Planner Task** is the authoritative work-in-flight idempotency layer above the retained in-memory dedupe stores
- A **Planner Task** may carry one **Plan Identifier** artifact reference after `propose_plan`
- A waiting **Planner Task** uses A2A `TaskState.AuthRequired`
- On restart, a **Remediation Planner** checks the approval-core status of waiting **Planner Tasks** and re-dispatches only non-terminal plans
- A **Remediation Planner** calls `propose_plan` to create a **Plan Envelope** with **Operator Approval Policy**
- A **Remediation Planner** authenticates with exactly one **Planner Service Identity**
- A **Remediation Planner** does not call execution tools and does not bypass any **Pre-Execution Gate**
- A **Remediation Executor** receives synchronous A2A dispatches carrying one **Plan Identifier** from the **Remediation Planner**
- A **Remediation Executor** returns an applied, failed, or rejected outcome to the **Remediation Planner**
- A **Remediation Executor** authenticates with exactly one **Executor Service Identity**
- A **Remediation Executor** calls `wait_for_plan_approval` and `execute_approved_plan` through the gateway
- A **Remediation Executor** does not produce **Plan Envelopes** and does not influence the **Challenge Outcome**
- `propose_plan` generates exactly one **Approval Access Code** per **Plan Envelope** and delivers it out-of-band
- An **Approval Access Code** routes to exactly one **Approval Challenge** but does not authenticate the **Approver**
- An **Approval Access Code** has a lifetime bounded by the **Challenge TTL** of its **Approval Challenge**
- The **Operator Approval Policy** is an **Approval Policy** subtype evaluated by the **Generic Approval Core** alongside **Same-Subject Approval**
- A **Planner Service Identity** is not a **Requester** in the lifecycle sense and is not an **Approver**
- An **Executor Service Identity** is not a **Requester** and is not an **Approver**
- A **Planner Service Identity** is authorized for `propose_plan` plus read-only tools, but not execution tools
- An **Executor Service Identity** is authorized only for `wait_for_plan_approval` and `execute_approved_plan`
- A **Plan Envelope** originated by `propose_plan` always declares **Operator Approval Policy**
- A **Plan Envelope** originated by a human-driven MCP client may declare **Same-Subject Approval**

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
- "authorization" was treated as if it were approval policy — resolved: **Authorization Check** is separate from **Approval Policy** and **Challenge Outcomes**.
- "policy validation" was treated as approval policy — resolved: **Domain Policy Check** is adapter-owned and separate from **Approval Policy** and **Authorization Check**.
- "approved" was treated as sufficient to execute — resolved: **Approval-Bound Execution** still requires **Pre-Execution Gates** immediately before mutation.
- "approval" was treated as a boolean flag — resolved: resolving an **Approval Challenge** records a **Challenge Outcome**, and only an approved challenge produces or references an **Approval Grant** bound to the **Plan Identifier**, **Requester**, **Approver**, **Intent Digest**, **Review Digest**, policy, expiry, and reuse constraints.
- "plan id" was treated as a possible integrity mechanism — resolved: **Plan Identifier** is opaque workflow identity; **Intent Digest** proves same executable intent and **Review Digest** proves same reviewed snapshot.
- "generic core" was treated as if it might be only terminology — resolved: **Generic Approval Core** owns the reusable lifecycle, while **Domain Adapters** own mutation meaning and evidence.
- "Kubernetes adapter" was treated as if it might own approval lifecycle behavior — resolved: **Kubernetes Adapter** owns Kubernetes mutation meaning and evidence, while **Generic Approval Core** owns approval lifecycle behavior.
- "approval page" was treated as a UI detail only — resolved: **Review Surface** rendering is implementation-specific, but it must present the trusted **Review Digest** snapshot instead of model-supplied approval content.
- "standard" was too strong for current positioning — resolved: InfraGate is currently an **Experimental Reference Implementation** for a possible **Mutation Approval Profile**.
- "gateway-to-server auth" was treated as user authorization — resolved: **Gateway Service Identity** authenticates the private downstream call and does not carry **Requester** or **Approver** authority.
- "observer" could read as a Kubernetes controller, generic monitoring system, or human reviewer — resolved: **Anomaly Observer** is a non-human MCP client bound by gateway read-only tools, separate from the approval lifecycle, and not authorized for any mutation.
- "finding" was informally used for **Plan Evidence** policy findings — resolved: the **Anomaly Observer** emits **Anomaly Reports**, not findings, so the two concepts stay separate.
- "executor" could read as `IDomainPlanExecutor` (the generic-core type that the **Kubernetes Adapter** implements) or as an autonomous agent — resolved: `IDomainPlanExecutor` is the generic-core type; **Remediation Executor** is the agent.
- "code" could read as a cryptographic authentication code or an OAuth authorization code — resolved: **Approval Access Code** is a UX routing token; **Approver** authentication remains the **Identity Provider**'s job.
- "propose" was used loosely in the profile narrative for the general "AI proposes" step — resolved: `propose_plan` is the specific gateway tool used by the **Remediation Planner** to create a **Plan Envelope** with **Operator Approval Policy** and emit an **Approval Access Code**.
