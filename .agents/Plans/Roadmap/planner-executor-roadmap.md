# Remediation Planner + Executor Implementation Roadmap

**Purpose:** Implementation plan for the autonomous remediation pair — `InfraGate.Planner` (consumes `AnomalyReport`s from the **Anomaly Observer**, picks a mutation operation, calls a new gateway tool `propose_plan` to create an approval-pending **Plan Envelope**) and `InfraGate.Executor` (consumes the planId, parks on `wait_for_plan_approval`, calls `execute_approved_plan` once the **Approval Grant** is issued). Approval is bound to a human operator out-of-band via email + a one-time **Approval Access Code** + the existing **Review Surface**.

**Source:** This plan is the output of a `grill-with-docs` session that walked every design branch one decision at a time. Every choice below is intentional and traceable to a grilling Q. The plan is sized per [`planning-and-task-breakdown`](../../skills/planning-and-task-breakdown/SKILL.md), follows [`code-standards`](../../skills/code-standards/SKILL.md), uses the architecture vocabulary from [`improve-codebase-architecture`](../../skills/improve-codebase-architecture/SKILL.md), and respects [`verify-readme-docs`](../../skills/verify-readme-docs/SKILL.md) for all doc changes.

---

## 0. Executive Summary

InfraGate today proves the read side of the autonomous loop: the **Anomaly Observer** watches the cluster through the gateway's read-only tools and emits structured `AnomalyReport`s through `IAnomalyHandoffSink`. The action side is currently absent — `AnomalyReport`s have no automated consumer, and no autonomous MCP client can request a mutation because the gateway's only **Approval Policy** is **Same-Subject Approval** (which would require the requester and approver to be the same authenticated subject).

This roadmap closes the loop by introducing:

- **`InfraGate.Planner`** — an LLM-driven agent that consumes `AnomalyHandoffBatch` via HTTPS, reasons about a remediation, and calls a new gateway tool `propose_plan(operationType, arguments)` to create a **Plan Envelope** with the new **Operator Approval Policy** and dispatch an out-of-band email to a configured operator address.
- **`InfraGate.Executor`** — a thin agent that consumes `RemediationProposalBatch` via HTTPS, parks on `wait_for_plan_approval(planId)`, and calls `execute_approved_plan(planId)` once the **Approval Grant** is issued.
- **Gateway extensions**: a new `propose_plan` MCP tool, a new **Operator Approval Policy** subtype, a new **Approval Access Code** subsystem (Razor Page + one-time-code store + email sender), new scopes (`mcp:tools.propose`, `mcp:tools.execute`), new audit identities (`service:planner`, `service:executor`).
- **Glossary additions** to `CONTEXT.md` covering **Remediation Planner**, **Remediation Executor**, **Operator Approval Policy**, **Remediation Proposal**, **Approval Access Code**, **Planner Service Identity**, **Executor Service Identity** (already applied as a `### Remediation` subsection in draft form; finalised in Phase 10).

What this is **not**:

- It is **not** a new approval lifecycle. Every existing **Pre-Execution Gate** still runs. Every audit-spine event still fires. The autonomous path joins the existing approval profile; it does not bypass it.
- It is **not** a full delegated-approval framework. **Operator Approval Policy** is the first generic-core sibling of **Same-Subject Approval**, not a multi-policy engine.
- It is **not** a fix for the **Anomaly Observer**. The Observer's shape is locked. The Planner/Executor consume it through the existing `IAnomalyHandoffSink` contract.
- It is **not** a multi-Executor / multi-region / multi-tenant story. v1 is one Observer, one Planner, one Executor, one operator group, one Keycloak realm.

What this **is**:

- Three ADRs codify the load-bearing choices: ADR-0017 (two-process split), ADR-0018 (`propose_plan` as a new tool), ADR-0019 (Operator Approval Policy as a new subtype).
- A vertical-sliced task list that ships the smallest end-to-end demo first (restart_deployment via the existing `examples/failing-deployment/` example) and grows from there.

---

## 1. Architecture Decisions (Locked)

Every decision below was made deliberately during grilling. Numbering is for reference only — not implementation order.

### 1.1 Language, runtime, project layout

| # | Decision | Rationale |
|---|---|---|
| 1.1.1 | C# / .NET 10, inheriting `Directory.Build.props` (TreatWarningsAsErrors=true, Meziantou analysers) | Matches every existing runtime project. |
| 1.1.2 | Two new projects: `src/InfraGate.Planner/`, `src/InfraGate.Executor/`, plus one contracts project `src/InfraGate.Remediation.Contracts/` | Mirrors Observer's `InfraGate.Observer/` + `InfraGate.Observer.Contracts/` layout. |
| 1.1.3 | Both hosted as ASP.NET `WebApplication` with `IHostedService` for any per-process work | Each needs an HTTP surface (`/handoff/...`, `/health`); WebApplication is the consistent shape. |
| 1.1.4 | Listening ports: `3004` Planner, `3005` Executor | Avoids collision (3001 gateway, 3002 reserved, 3003 observer). |
| 1.1.5 | Single `/health` endpoint per agent (no liveness/readiness split) | Matches Observer per ADR-0015's adjacent Phase 8 decision. |

### 1.2 LLM SDK and model selection (Planner only)

| # | Decision | Rationale |
|---|---|---|
| 1.2.1 | `Microsoft.Extensions.AI` provider-agnostic abstraction (same as Observer §1.2.1) | One SDK; provider swap is configuration. |
| 1.2.2 | Default provider Anthropic, default model `claude-sonnet-4-6` | Best price/tool-use balance in 2026; matches Observer default. |
| 1.2.3 | Provider configurable via env (`INFRA_GATE_PLANNER_LLM_PROVIDER`, `INFRA_GATE_PLANNER_LLM_MODEL`, `INFRA_GATE_PLANNER_LLM_API_KEY`) | Mirrors Observer naming, swapped prefix. |
| 1.2.4 | **Executor does not embed an LLM** | Executor is deterministic: wait, execute. Adding an LLM would dilute the scope-split argument in ADR-0017. |

### 1.3 Agent topology and transport

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.3.1 | Two-process split: `InfraGate.Planner` and `InfraGate.Executor` are distinct binaries, distinct identities, distinct scopes | Defense-in-depth from scope split | ADR-0017, Q7 |
| 1.3.2 | **Eager handoff**: Planner pushes the `planId` to the Executor immediately after `propose_plan` returns. Executor calls `wait_for_plan_approval(planId)` and parks until the grant fires | No new gateway primitive; mirrors Observer→consumer shape | Q3a |
| 1.3.3 | Handoff transport is **HTTPS push** for both Observer→Planner and Planner→Executor | Symmetric pattern, debuggable, no new infrastructure | Q8 |
| 1.3.4 | Receiving endpoint per consumer: `POST /handoff/anomalies` on Planner, `POST /handoff/proposals` on Executor. Both return `202 Accepted` immediately; processing is async via an internal channel | No long-blocking HTTP; backpressure handled at the channel | Q8 |
| 1.3.5 | Mutual auth via OAuth `client_credentials` bearer (same `InfraGate.ClientCredentials` library used by Observer and DownstreamAuth) | ADR-0016 already justified the shared library; this is its third consumer | Q8 |

### 1.4 Gateway tool surface extensions

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.4.1 | New MCP tool `propose_plan(operationType, arguments)` returning `{ planId, accessCodeSent, codeExpiresAt }` | Per ADR-0018; clean per-caller-type contract separation from `request_*` | ADR-0018, Q2 |
| 1.4.2 | `propose_plan` is the **only** tool the Planner is authorised to call (plus the existing read-only inspection tools — same whitelist the Observer uses) | Defense-in-depth | ADR-0017 |
| 1.4.3 | Executor's allowed tools: `wait_for_plan_approval` + `execute_approved_plan` only | Defense-in-depth | ADR-0017 |
| 1.4.4 | v1 `operationType` allowlist: `restart_deployment`, `scale_deployment` | Low-risk operations, covers two AnomalyKinds, shows LLM choice without `apply_manifest`'s blast radius | Q11 |
| 1.4.5 | `set_image`, `apply_manifest`, `delete_resource` deferred to v2+ | `set_image` needs structured `RemediationHint` evolution; `apply_manifest` needs much stronger gating; `delete_resource` not currently justified | Q11 |
| 1.4.6 | `request_*` tools are unchanged | ADR-0018's load-bearing property: human-driven callers see no behaviour change | ADR-0018 |

### 1.5 Approval Policy extension (generic core)

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.5.1 | Extend `ApprovalPolicy(string Type)` with `Parameters: IReadOnlyDictionary<string, string>?`. `SameSubject` leaves `Parameters` null/empty; `OperatorApproval` sets `Type = "operator-approval"` and `Parameters["operatorGroup"]`. Parameter-dictionary shape chosen over polymorphic record hierarchy or per-policy nullable fields — C# has no native tagged union; the dictionary keeps `CanonicalJson` flat and additive. | Per ADR-0019 (clarified during plan review) | ADR-0019, Q4 |
| 1.5.2 | `propose_plan`-originated Plan Envelopes always declare `OperatorApproval`; the Planner does not pick the policy per call | Keeps Planner dumb; centralises policy assignment at the gateway | Q4, ADR-0018 |
| 1.5.3 | Operator group name is gateway-side config: env `INFRA_GATE_OPERATOR_GROUP` (default `kubernetes-operators`) | Planner has no operational knowledge of who approves | Q4 |
| 1.5.4 | Grant validation gains a per-`Type` branch in `ApprovalGrantValidation`: `SameSubject` (existing) vs `OperatorApproval` (new — reads `policy.Parameters["operatorGroup"]` and checks the approver's Keycloak `groups` claim contains it) | Bucket-1 pre-execution gates already centralised in `GetGrantedPlanAsync` (per ADR-0006) | ADR-0019 |
| 1.5.5 | Delegated/Multi-Party policies are explicit future extension points, not v1 | Q4 considered and rejected for v1 |
| 1.5.6 | `Authorization Check` for `OperatorApproval` plans is a separate per-policy `IAuthorizationCheck` resolver — keeps "who approves" distinct from "who executes" per CONTEXT.md | Preserves the CONTEXT.md distinction explicitly | ADR-0019 |

### 1.6 Out-of-band notification (email + Approval Access Code)

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.6.1 | New seam `IApprovalAccessCodeStore` for one-time-code persistence (in-memory + Postgres mirror) | Mirrors `IApprovalPersistence` pattern | Q5 |
| 1.6.2 | Code format: 8 chars, alphabet `ABCDEFGHJKMNPQRSTUVWXYZ23456789` (no ambiguous 0/O, I/L/1), generated via `RandomNumberGenerator` | Operator-friendly typing; CSPRNG | Q5 |
| 1.6.3 | Code TTL is bounded by the **Challenge TTL** of its **Approval Challenge** — no second clock | One clock, one expiry semantic | Q5 |
| 1.6.4 | Code is single-use: marked `Consumed` on first successful validation | Replay prevention | Q5 |
| 1.6.5 | New seam `IApprovalEmailSender` with one implementation `SmtpApprovalEmailSender` (using `System.Net.Mail`, no new package) | Per ADR-... informally captured in Q6; mailpit handles dev side | Q6 |
| 1.6.6 | Dev environment uses Mailpit sidecar in `deploy/local-oauth/compose.yaml` — same SMTP code path everywhere | One code path, dev/prod parity for the email subsystem | Q6 |
| 1.6.7 | Email body: plaintext, ≤10 lines, includes code, plan one-line summary (from the Review Digest snapshot), approval URL, expiry. No HTML, no tracking, no images | Preserves the **Review Surface** trust model | Q6 |
| 1.6.8 | Operator address: single configured value via env `INFRA_GATE_OPERATOR_EMAIL` (default required, no fallback) | Simplest v1 routing — anyone in the operator group may approve; group email gets the heads-up | Q4 |
| 1.6.9 | Code entry UI: Blazor static-SSR component `src/InfraGate.ApprovalUi/Components/Code.razor` rendered via the existing `HtmlRenderer` (same path as `ApprovalPageContent.razor` and `DecisionPage.razor`); exposed at `GET /approvals/code` with a single input + submit | Matches the existing `InfraGate.ApprovalUi` stack — `Microsoft.NET.Sdk.Razor` + `Microsoft.AspNetCore.Components.Web` Blazor components, not Razor Pages | Q5 |
| 1.6.10 | `POST /approvals/code` Minimal API handler validates via `Consume`; success redirects 302 to `/approvals/{challengeId}`; failure re-renders `Code.razor` with a structured error model | Reuses existing **Review Surface**; code is routing, not authentication | Q5 |

### 1.7 Identity, scopes, audit identity

| # | Decision | Rationale |
|---|---|---|
| 1.7.1 | New Keycloak client `infra-gate-planner` (grant=client_credentials), scope `mcp:tools.propose` | Distinct from Observer's `mcp:tools.readonly` and human `mcp:tools` |
| 1.7.2 | New Keycloak client `infra-gate-executor` (grant=client_credentials), scope `mcp:tools.execute` | Distinct from Planner's scope |
| 1.7.3 | Audit identities: `service:planner`, `service:executor`, emitted via extension of `GatewayAuditIdentityResolver` registered service-client list | Matches Observer's `service:observer` pattern |
| 1.7.4 | Gateway scope-to-tool map: `mcp:tools.propose` → `propose_plan` only; `mcp:tools.execute` → `wait_for_plan_approval` + `execute_approved_plan` only; `mcp:tools` (human) continues to cover all mutation tools | Defense-in-depth at the gateway, not just the agents |
| 1.7.5 | Planner is **also** authorised for the existing read-only tools by adding `mcp:tools.readonly` as a secondary scope on the `infra-gate-planner` client | Planner may need to inspect cluster state for richer reasoning |
| 1.7.6 | Executor is **not** authorised for read-only tools — it has no use for them | Defense-in-depth |

### 1.8 Handoff contract

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.8.1 | Observer→Planner: reuses existing `InfraGate.Observer.Contracts.AnomalyHandoffBatch` directly — no wrapper, no transform | The Observer designed it for this purpose | Q9b |
| 1.8.2 | New `src/InfraGate.Remediation.Contracts/` project holds Planner→Executor types | Mirrors `InfraGate.Observer.Contracts` — pure types, zero internal refs | Q9 |
| 1.8.3 | Lean payload: `RemediationProposal { PlanId, AnomalyId, ProposedAt }`; batched as `RemediationProposalBatch { CycleId, EmittedAt, Proposals: IReadOnlyList<RemediationProposal> }` | Smallest contract; no duplicated state; record-extensible | Q9a |
| 1.8.4 | Seam `IRemediationProposalSink` (mirrors `IAnomalyHandoffSink`) with `HttpRemediationProposalSink`, `LoggingRemediationProposalSink` (always on), `JsonFileRemediationProposalSink` (opt-in), `CompositeRemediationProposalSink` (fan-out with failure isolation) | Same shape Observer ships; same testability properties | Q8 |
| 1.8.5 | Fire-and-forget reliability in v1 — sink throw → log + counter + move on; `LoggingRemediationProposalSink` is always-on forensic safety net | Same model as Observer §1.9.5 | Q8 |

### 1.9 Planner behaviour

| # | Decision | Rationale |
|---|---|---|
| 1.9.1 | Planner is **event-driven**, not cycle-based — no fixed cadence loop. Reacts to each `AnomalyHandoffBatch` received | Different from Observer (which is cycle-based by design); the Planner has no reason to poll. |
| 1.9.2 | Per-batch processing: filter (drop Resolved status, drop unacted-on AnomalyKinds) → per-anomaly LLM call → per-anomaly `propose_plan` call → publish a `RemediationProposalBatch` to the Executor | Sequential per-anomaly inside a batch; bounded by batch size which is bounded by the Observer's per-cycle anomaly count |
| 1.9.3 | Per-anomaly wall-clock cap: 30s (LLM call + propose_plan call); per-batch cap: 5 minutes | Bounds total batch processing time; prevents runaway loops |
| 1.9.4 | Dedupe: skip anomalies already seen with a pending or granted plan (track by `AnomalyId`, bounded LRU `ConcurrentDictionary<AnomalyId, ActivePlanState>` capacity 1000) | Don't propose multiple plans for the same anomaly; entries cleared when plan reaches terminal state |
| 1.9.5 | Bounded LLM tool-call iterations: 4 per anomaly (read-only inspection only) | Smaller than Observer's 8 because Planner's decision is more constrained |
| 1.9.6 | System prompt embedded as `src/InfraGate.Planner/Prompts/PlannerSystemPrompt.md` (`<EmbeddedResource>`) | Matches Observer §1.7.5 |
| 1.9.7 | LLM-proposed `operationType` is validated against the v1 allowlist (`restart_deployment`, `scale_deployment`); LLM output that doesn't match is dropped + counted via metric, no propose_plan call | Hard guardrail; defense-in-depth on top of the gateway's server-side allowlist |
| 1.9.8 | LLM-proposed arguments are validated against per-operation schemas before propose_plan | `scale_deployment` requires `name`, `namespace`, `replicas` ≥ 0; `restart_deployment` requires `name`, `namespace` |

### 1.10 Executor behaviour

| # | Decision | Rationale |
|---|---|---|
| 1.10.1 | Executor is **event-driven** — reacts to `RemediationProposalBatch` arriving at `/handoff/proposals` | Mirrors Planner's shape |
| 1.10.2 | Per `RemediationProposal`: spawn a tracked Task that calls `wait_for_plan_approval(planId, timeoutSeconds=900)` (15-minute parked call); on grant, calls `execute_approved_plan(planId)`; on timeout/expiry, logs + counts and drops | Eager subscription per Q3a; one parked call per pending plan |
| 1.10.3 | Concurrency cap: 64 in-flight parked plans (`SemaphoreSlim` bound). Excess proposals are rejected with `429 Too Many Requests` from the handoff endpoint | Bounds memory + HTTP connection use |
| 1.10.4 | Dedupe: skip duplicate `planId` (track by `ConcurrentDictionary<PlanId, ActiveExecutionState>` capacity 1000, entries cleared on terminal state) | Replay safety |
| 1.10.5 | Failure handling: any failure during `wait_for_plan_approval` or `execute_approved_plan` is logged + counted + dropped; the gateway already records `execution.failed` / `execution.blocked` on the Audit Spine | Executor doesn't second-guess the gateway's audit trail |
| 1.10.6 | No retry on failure in v1 — the **Domain Adapter** owns retry semantics per CONTEXT.md | Don't blur the boundary |

### 1.11 Observability

| # | Decision | Rationale |
|---|---|---|
| 1.11.1 | Reuse `InfraGate.Observability` Serilog configuration (console + file sinks) | Aligns with Gateway, McpServer, Observer |
| 1.11.2 | Per-anomaly enrichment: `LogContext.PushProperty("AnomalyId", id)` in Planner; per-plan: `LogContext.PushProperty("PlanId", id)` in Executor | Greppable end-to-end |
| 1.11.3 | Structured event taxonomy (see §6.1) via `[LoggerMessage]` source-generated methods | Per `code-standards` for high-frequency paths |
| 1.11.4 | Metrics via `System.Diagnostics.Metrics` — `Meter("InfraGate.Planner", "1.0")` and `Meter("InfraGate.Executor", "1.0")` | Matches Observer §1.10.4 |
| 1.11.5 | **Audit Spine separation**: Planner and Executor write structured Serilog only, never through `IApprovalAuditPublisher`. The gateway's `propose_plan`, `execute_approved_plan`, pre-execution gate, and execution-attempt events are the authoritative spine entries | Same discipline as ADR-0015 for the Observer; Planner/Executor are operational layers, not spine producers |
| 1.11.6 | No OpenTelemetry SDK and no distributed tracing in v1 — same rationale as Observer §1.10.5 | Add when an OTel collector is in the environment |

### 1.12 Tests

| # | Decision | Rationale |
|---|---|---|
| 1.12.1 | Test layers per agent: `tests/InfraGate.Planner.Tests/` (unit), `tests/InfraGate.Planner.IntegrationTests/` (in-process Gateway TestHost + stub MCP fixtures + `FixtureChatClient`), `tests/InfraGate.Executor.Tests/`, `tests/InfraGate.Executor.IntegrationTests/`. Existing `tests/InfraGate.McpGateway.Tests/` covers the new gateway tool + Operator Approval Policy + Approval Access Code surface | Matches Observer test layering |
| 1.12.2 | One opt-in E2E test project `tests/InfraGate.Remediation.E2E.Tests/` gated by `INFRA_GATE_RUN_REMEDIATION_E2E=1`, runs the full Observer→Planner→Approval→Executor loop against a developer-provided cluster + Keycloak + Mailpit Testcontainer | Matches Observer §1.11.1 opt-in pattern |
| 1.12.3 | LLM stubbed by default via `FixtureChatClient : IChatClient`; opt-in real LLM via `INFRA_GATE_PLANNER_REAL_LLM=1` | CI fast/free/deterministic; real LLM run catches prompt regressions |
| 1.12.4 | Pass criteria are **structural** — assert on operation type, arguments shape, planId issuance, grant flow, execution outcome. No assertions on LLM `Summary` prose or `RemediationHint` content | Avoids LLM-induced flake |
| 1.12.5 | Demo scenario reuses `examples/failing-deployment/` with two YAML variants: one for restart_deployment (rollout stuck), one for scale_deployment (replicas=0) | Per Q11 |

### 1.13 Deployment

| # | Decision | Rationale |
|---|---|---|
| 1.13.1 | Dockerfile per agent: `src/InfraGate.Planner/Dockerfile`, `src/InfraGate.Executor/Dockerfile` — `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` runtime, `mcr.microsoft.com/dotnet/sdk:10.0-alpine` build | Matches gateway/server/observer pattern |
| 1.13.2 | Extend the existing `deploy/local-oauth/compose.yaml` with `planner`, `executor`, and `mailpit` services | No new compose files |
| 1.13.3 | New `PlannerProfile.cs` and `ExecutorProfile.cs` component-profile records in `src/InfraGate.RunProfiles/`, sibling to `GatewayProfile`/`KubernetesAdapterProfile`/`ObserverProfile` | Matches existing run-profiles pattern |
| 1.13.4 | Env-var schema prefixes `INFRA_GATE_PLANNER_*` and `INFRA_GATE_EXECUTOR_*`, wired through existing `InfraGateEnvVarMappings` | Consistent with `InfraGate.RuntimeSafety` conventions |
| 1.13.5 | Mailpit web UI exposed on host port `8025` for inspection | Standard Mailpit default |
| 1.13.6 | Bind-mount `./.mcp-remediation/proposals` on the host for the JSON file sink (parallels `.mcp-observer/findings`) | Optional dev-time durability for the Planner→Executor handoff |

### 1.14 VS Code companion (lowest priority — optional)

| # | Decision | Rationale |
|---|---|---|
| 1.14.1 | An additional VS Code custom agent file `agents/remediation-planner.agent.md` triggers a manual handoff to a local Planner | Lowest-priority companion |

---

## 2. Glossary Delta

The following changes were applied to `CONTEXT.md` during grilling with `DRAFT` markers (Q10). They are finalised in Phase 10 Task 10.6 by removing the draft markers.

- **New subsection** `### Remediation` under Language with seven term definitions: **Remediation Planner**, **Remediation Executor**, **Operator Approval Policy**, **Remediation Proposal**, **Approval Access Code**, **Planner Service Identity**, **Executor Service Identity**.
- **New subsection** `### Remediation` under Relationships with ~19 relationship bullets, including hard-line statements that the Planner/Executor identities are not Requesters or Approvers, do not bypass any Pre-Execution Gate, and that `propose_plan`-originated plans always declare **Operator Approval Policy**.
- **Updated entry** `Same-Subject Approval` to cross-reference **Operator Approval Policy**.
- **Updated entry** `Anomaly Handoff` to describe the v1 HTTPS transport.
- **Three new entries** in Flagged Ambiguities: "executor" (generic-core type vs agent), "code" (UX routing token vs cryptographic), "propose" (specific tool vs general narrative).

No further glossary work is required during implementation unless new concepts surface during code review.

---

## 3. Out of Scope (v1)

Explicit non-goals so future readers don't re-litigate during implementation:

- **`apply_manifest`, `set_image`, `delete_resource` as Planner-selectable operations.** v1 is `restart_deployment` + `scale_deployment` only. `set_image` needs structured `RemediationHint` evolution (a typed `SuggestedImage`); `apply_manifest` needs much stronger gating and verification work; `delete_resource` is not currently justified.
- **`Delegated Approval Policy` (explicit per-plan approver list).** ADR-0019 explicitly considered and rejected for v1.
- **`Multi-Party Approval Policy` (N-of-M signatures).** Real lifecycle extension — Approval Challenge would have to model non-terminal partial outcomes. Deferred.
- **Persistent Planner state.** Dedupe state is in-memory only; restart = clean slate. (Same trade-off as Observer §1.6.8.)
- **Persistent Executor state.** Parked `wait_for_plan_approval` calls are lost on restart; the gateway's grant remains valid and a human can re-trigger via the existing approval URL, but the Executor will not auto-recover the parked call.
- **Multi-Executor coordination / claim-then-execute split.** v1 is one Executor instance; race avoidance is "don't run two".
- **Multi-tenant operator groups / per-namespace policies.** Single configured group in v1.
- **Production secret management** for Planner/Executor client secrets (K8s `Secret`, SPIFFE, Workload Identity). Env-var only for v1.
- **Production-grade `/healthz` + `/readyz` split.** Single `/health` v1, matching Observer.
- **OpenTelemetry SDK, distributed tracing, OTLP exporters.**
- **Email retries / queueing.** SMTP send is fire-and-forget; failure is logged + counted.
- **HTML email, attachments, signing, DKIM.** Plain text v1.
- **One-click email magic-link URL.** Email contains a code; operator visits the code-entry page. (The clickable variant is a small follow-up, not v1.)
- **Adaptive cadence / cost limits.** Per-anomaly + per-batch caps are fixed config; rolling p95 adaptation is deferred.
- **Planner LLM call cancellation on Resolved.** If an `AnomalyReport` arrives with `Status=Resolved` for an anomaly the Planner is currently reasoning about, v1 lets the in-flight LLM call complete; the resulting plan is created and may or may not still be needed (the operator can decline). Cleaner cancellation is a v2 candidate.
- **Per-tool LLM iteration timeouts.** Per-anomaly wall-clock cap covers it.
- **VS Code agent for the Executor.** Only Planner gets a companion in v1.

---

## 4. Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| LLM proposes an invalid operationType or argument shape | Medium | Strict client-side validation (§1.9.7, §1.9.8) before any `propose_plan` call; gateway server-side allowlist as second line; both increment a metric counter on rejection |
| LLM cost runaway on a noisy cluster | Medium | Per-anomaly wall-clock cap (30s), per-batch cap (5m), `infragate.planner.llm.tokens` metric for visibility, configurable caps |
| Email send fails (SMTP down, Mailpit down in dev) | Medium | `propose_plan` returns the planId regardless; failure is logged + counted (`infragate.gateway.email.failed`); operator can still reach the approval page via the existing direct URL (returned in the `propose_plan` response body alongside `planId`) |
| Approval Access Code is leaked from the operator's inbox | Low — Code is routing, not auth | Approver still authenticates via Keycloak before approving; a leaked code only lets an attacker land on the approval page, not approve from it |
| Executor parked-call leak on restart | Low | Documented limitation; operator can re-trigger via the existing approval URL; v2 candidate: persistent plan watcher |
| Two Executors running simultaneously race on the same plan | Medium if it ever happened | v1 documents "one Executor only"; if a second instance starts, both park; whichever wakes first calls `execute_approved_plan`; the gateway's execution reuse policy (Single-Execution Plan) blocks the second |
| Operator group claim missing from approver's JWT | Medium — would always reject | OperatorApproval grant validation rejects with structured error containing the missing-claim reason; documented in `docs/configuration.md`; integration test asserts the rejection path |
| Operator group misconfigured between gateway and Keycloak | Low | Startup validation: gateway logs the configured group at startup; Keycloak realm export script asserts the group exists |
| `InfraGate.ClientCredentials` extension breaks Observer or DownstreamAuth | Low | Any extension is in the same task; existing test suites must remain green; integration test exercises all three consumers |
| Audit Spine cross-contamination from Planner or Executor | Medium if it happened | Architecturally enforced: neither project references `InfraGate.Approvals` for audit publishing; project-reference assertion in unit tests |
| Planner produces a `RemediationProposal` for an anomaly that has been Resolved by the next Observer cycle | Low | Plan envelope remains valid until approved/expired; operator sees the proposal, can decline; Resolved anomalies are not re-proposed (Planner dedupe filters them) |
| Sink failure cascades into Planner cycle abort | Low | `CompositeRemediationProposalSink` isolates per-sink failures (try/catch per sink, log + counter, continue) |
| Stubbed-LLM tests diverge from real-LLM behaviour over time | Medium | `INFRA_GATE_PLANNER_REAL_LLM=1` opt-in path runs the same suite against the configured provider |
| Mailpit not running locally blocks demo | Low | Compose `depends_on` ensures Planner waits for Mailpit; documented in failing-deployment README |

---

## 5. Task List

Phases are vertical-sliced from Phase 1 onward. Each task is small enough for a focused session. Numbering is for reference, not implementation order — see §10 for execution order.

### Phase 1: Foundation — Gateway extensions

#### Task 1.1: Add `OperatorApproval` policy to `InfraGate.Approvals`

**Description:** Extend the `ApprovalPolicy` type with a `Parameters: IReadOnlyDictionary<string, string>?` dictionary (per decision 1.5.1). Update `ApprovalCanonicalJson` to include parameters in the Review Digest canonicalisation. Update `ApprovalGrantValidation.Validate` with a per-`Type` branch (decision 1.5.4). Extend `IDomainPlanBuilder.BuildAsync` signature to accept `ApprovalPolicy` — every adapter's `Build*Async` method receives the policy and stamps it on the envelope. Update `SameSubjectAuthorizationCheck` registration to be resolved via a per-policy `IAuthorizationCheck` resolver. This is a generic-core extension and the load-bearing piece of ADR-0019.

**Acceptance criteria:**

- [ ] `ApprovalPolicy` gains `Parameters: IReadOnlyDictionary<string, string>?`; `SameSubject` leaves it null; `OperatorApproval` sets `Parameters["operatorGroup"]`.
- [ ] Existing Same-Subject behaviour unchanged — every existing test passes.
- [ ] `IDomainPlanBuilder.BuildAsync` signature extended with `ApprovalPolicy approvalPolicy` parameter; all 25+ `Build*Async` implementations in `KubernetesPlanBuilder` updated.
- [ ] New `OperatorApproval` variant. Grant validation reads `policy.Parameters["operatorGroup"]` and checks the approver's `groups` claim; rejects with appropriate reason code if absent or mismatched.
  > **Implementation note (verified Phase 1):** The operator-group claim check is done in `GatewayApprovalService.IsActorAuthorizedForChallengeOutcome` during the approval-browser UI flow, not inside `ApprovalGrantValidation.Validate`. Grant validation itself (`ApprovalGrantValidation.ValidatePolicy`) confirms `policy.Parameters["operatorGroup"]` exists. This is a necessary separation — grant validation has no access to the approver's JWT at execution time. The AC above describes the combined security property; the actual check is split across two points in the flow.
- [ ] `ApprovalCanonicalJson` includes the policy variant tag and parameters; the Review Digest changes deterministically when policy changes.
- [ ] In-memory `ApprovalStore` mirrors the new shape.
- [ ] `PostgresApprovalPersistence` migration adds `policy_kind` + `operator_group` columns; old rows default to `same-subject` on read.
- [ ] In-flight pending plans: deploy drains outstanding pollers before the schema change, or the migration writes a `same-subject` default for all existing rows (their ReviewDigest remains valid because the JSON serialisation of a null-Parameters `SameSubject` is backward-compatible).
- [ ] New unit tests in `tests/InfraGate.Approvals.Tests/` cover both variants positive + negative.

**Verification:** `dotnet test InfraGate.slnx` green; specifically `tests/InfraGate.Approvals.Tests/`, `tests/InfraGate.Approvals.Postgres.Tests/`, `tests/InfraGate.McpGateway.Tests/`.

**Dependencies:** None.

**Files likely touched:** `src/InfraGate.Approvals/ApprovalPolicy.cs`, `src/InfraGate.Approvals/ApprovalCanonicalJson.cs`, `src/InfraGate.Approvals/ApprovalGrantValidation.cs`, `src/InfraGate.Approvals/ApprovalConventions.cs` (ReasonCodes), `src/InfraGate.Approvals/IAuthorizationCheck.cs`, `src/InfraGate.Approvals/IDomainPlanBuilder.cs` (new parameter), `src/InfraGate.Approvals/ApprovalStore.cs`, `src/InfraGate.Approvals.Postgres/PostgresApprovalMigrationRunner.cs`, `src/InfraGate.Approvals.Postgres/PostgresApprovalPersistence.cs`, `src/InfraGate.McpGateway/SameSubjectAuthorizationCheck.cs` (now per-policy resolver), plus every adapter `Build*Async` method.

**Estimated scope:** Large (touches the generic-core boundary; many tests).

---

#### Task 1.2: Approval Access Code subsystem

**Description:** Introduce `IApprovalAccessCodeStore` with `Generate(challengeId, ttl) -> code` and `Consume(code) -> challengeId | null`. Backed by in-memory `ConcurrentDictionary<Code, AccessCodeEntry>` plus a Postgres mirror. Add a Razor Page at `src/InfraGate.ApprovalUi/Pages/Code.cshtml` rendered as `/approvals/code` with a single text input + submit form. POST handler validates via `Consume`, marks used, redirects 302 to `/approvals/{challengeId}` on success; renders an error page on failure (expired, used, unknown).

**Acceptance criteria:**

- [ ] `IApprovalAccessCodeStore.Generate` produces an 8-char code from the constrained alphabet (no `0/O/I/L/1`), CSPRNG-backed.
- [ ] `Consume` is atomic + single-use; concurrent calls with the same code give exactly one success.
- [ ] Code TTL equals the Challenge TTL of its bound challenge; lookups past expiry return null + count via `infragate.gateway.code.expired`.
- [ ] Razor Page `/approvals/code` renders the form; POST handler routes to validation; success redirects 302 to `/approvals/{challengeId}`; failure renders structured error page.
- [ ] Unit tests cover generation alphabet, single-use enforcement, expiry, concurrent consume.

**Verification:** `dotnet test tests/InfraGate.ApprovalUi.Tests/` and `tests/InfraGate.McpGateway.Tests/` green; manual: visit `/approvals/code` locally and exercise success + error paths.

**Dependencies:** Task 1.1 (Operator Approval Policy must exist before access codes have a use).

**Files likely touched:** `src/InfraGate.Approvals/IApprovalAccessCodeStore.cs` (new), `src/InfraGate.Approvals/ApprovalAccessCode.cs` (new record), `src/InfraGate.Approvals/InMemoryApprovalAccessCodeStore.cs` (new), `src/InfraGate.Approvals.Postgres/PostgresApprovalAccessCodeStore.cs` (new) + migration, `src/InfraGate.ApprovalUi/Pages/Code.cshtml` (new) + handler, `src/InfraGate.McpGateway/GatewayCodeEndpoints.cs` (new), `src/InfraGate.McpGateway/McpGatewayConventions.cs` (Approvals.CodeRoute constant).

**Estimated scope:** Large (new subsystem + new UI page + persistence + tests).

---

#### Task 1.3: Email sender (`IApprovalEmailSender` + Mailpit dev path)

**Description:** New seam `IApprovalEmailSender.SendAsync(ApprovalEmailContent, ct)`. One implementation `SmtpApprovalEmailSender` using `System.Net.Mail.SmtpClient` configured from `INFRA_GATE_GATEWAY_SMTP_HOST/PORT/FROM/USER/PASSWORD`. Body template renders plaintext per §1.6.7. Add `mailpit` service to `deploy/local-oauth/compose.yaml` with SMTP on `1025` and web UI on `8025`.

**Acceptance criteria:**

- [ ] `IApprovalEmailSender` has one method, one record argument (`ApprovalEmailContent { ToAddress, Subject, BodyPlaintext }`).
- [ ] `SmtpApprovalEmailSender` uses `SmtpClient`; configuration validated at startup (missing host fails fast).
- [ ] Plaintext body template renders deterministically from `(planId, planSummary, accessCode, approvalUrl, expiresAt)`.
- [ ] Mailpit service runs in dev compose; Planner-triggered emails are visible at `http://localhost:8025`.
- [ ] Send failure raises a single structured warning + increments `infragate.gateway.email.failed`; does not throw out of `propose_plan` (the planId is still returned).

**Verification:** Unit tests against a fake `SmtpClient`; manual: bring up compose, trigger `propose_plan` end-to-end (Task 1.4), see email in Mailpit web UI.

**Dependencies:** None (other than the compose file existing — which it does).

**Files likely touched:** `src/InfraGate.McpGateway/Email/IApprovalEmailSender.cs` (new), `src/InfraGate.McpGateway/Email/SmtpApprovalEmailSender.cs` (new), `src/InfraGate.McpGateway/Email/ApprovalEmailContent.cs` (new record), `src/InfraGate.McpGateway/Email/ApprovalEmailRenderer.cs` (new), `src/InfraGate.McpGateway/McpGatewayConventions.cs` (env var keys + configuration paths), `deploy/local-oauth/compose.yaml` (mailpit service).

**Estimated scope:** Medium.

---

#### Task 1.4: New MCP tool `propose_plan`

**Description:** Per ADR-0018. Add `McpGatewayConventions.ToolNames.ProposePlan = "propose_plan"` and the corresponding scope `ToolScopeRequirements.ProposeScope = "mcp:tools.propose"`. Implement the handler as a thin orchestration: scope check → `IDomainPlanBuilder.BuildAsync(operationType, arguments, requester=ServicePlannerRequester, policy=OperatorApproval)` → `IApprovalAccessCodeStore.Generate` → `IApprovalEmailSender.SendAsync` → return `{ planId, accessCodeSent, codeExpiresAt, approvalUrl }`. The `operationType` is validated against the v1 allowlist (`restart_deployment`, `scale_deployment`).

**Acceptance criteria:**

- [ ] `propose_plan` registered as an MCP tool with the documented signature.
- [ ] Tool requires `mcp:tools.propose` scope; rejected with structured error otherwise.
- [ ] `operationType` outside `{restart_deployment, scale_deployment}` is rejected before any Plan Envelope is built.
- [ ] `Requester` on the Plan Envelope is the caller's subject (`service:planner` for the Planner client).
- [ ] `ApprovalPolicy` on the Plan Envelope is `OperatorApproval { GroupName = INFRA_GATE_OPERATOR_GROUP }`.
- [ ] On success: code generated, email dispatched, response body returned. Email failure does not fail the call (per Risk row).
- [ ] On any failure after the plan is created: structured audit event + plan remains createable for retry via existing approval URL.
- [ ] Unit tests: scope rejection, operationType rejection, happy path with stub email sender.
- [ ] Integration test (in-process Gateway): full path producing a Plan Envelope with the expected policy + a code in the store + a captured email.

**Verification:** `dotnet test tests/InfraGate.McpGateway.Tests/`.

**Dependencies:** Tasks 1.1, 1.2, 1.3.

**Files likely touched:** `src/InfraGate.McpGateway/McpGatewayConventions.cs` (tool name + scope), `src/InfraGate.McpGateway/GatewayToolDispatcher.cs` (tool registration + dispatch), `src/InfraGate.McpGateway/ProposePlanHandler.cs` (new), `src/InfraGate.McpGateway/GatewayAuthConventions.cs` (Scope additions).

**Estimated scope:** Large.

---

#### Task 1.5: New scope `mcp:tools.execute` + scope-to-tool map for Executor tools

**Description:** Add `ToolScopeRequirements.ExecuteScope = "mcp:tools.execute"`. Map `wait_for_plan_approval` and `execute_approved_plan` to accept either `mcp:tools` (existing human path) or `mcp:tools.execute`. Mutation tools (`request_*`, `propose_plan`) reject `mcp:tools.execute`-only tokens.

**Acceptance criteria:**

- [ ] Token carrying only `mcp:tools.execute` succeeds on `wait_for_plan_approval` and `execute_approved_plan`.
- [ ] Token carrying only `mcp:tools.execute` is rejected on every mutation tool and on read-only tools.
- [ ] Token carrying `mcp:tools` continues to work everywhere (no regression).
- [ ] Audit event records the rejected scope on denial.

**Verification:** `dotnet test tests/InfraGate.McpGateway.Tests/` with new scope-mapping tests.

**Dependencies:** None (independent of Task 1.4 — both can land in parallel).

**Files likely touched:** `src/InfraGate.McpGateway.Auth/GatewayAuthConventions.cs`, `src/InfraGate.McpGateway/GatewayToolDispatcher.cs`, `src/InfraGate.McpGateway/McpGatewayConventions.cs`.

**Estimated scope:** Small.

---

#### Task 1.6: Extend `GatewayAuditIdentityResolver` for `service:planner` and `service:executor`

**Description:** Add `infra-gate-planner` and `infra-gate-executor` to the registered service-client list. Emit identities `service:planner` and `service:executor`. Mirrors the Observer's `service:observer` extension done in the observer roadmap Task 1.4.

**Acceptance criteria:**

- [ ] Registered service-client list grows by two entries.
- [ ] `azp=infra-gate-planner` surfaces as `service:planner`; `azp=infra-gate-executor` surfaces as `service:executor`.
- [ ] No regression for human or observer tokens.

**Verification:** `dotnet test tests/InfraGate.McpGateway.Tests/`.

**Dependencies:** None.

**Files likely touched:** `src/InfraGate.McpGateway.Auth/GatewayAuditIdentityResolver.cs`, `src/InfraGate.McpGateway.Auth/GatewayAuthConventions.cs`, `tests/InfraGate.McpGateway.Tests/`.

**Estimated scope:** Small.

---

#### Task 1.7: Create `InfraGate.Remediation.Contracts` project

**Description:** Pure-types project holding `RemediationProposal`, `RemediationProposalBatch`, `IRemediationProposalSink`. No behaviour, no MCP/LLM deps. All types are `sealed record` per code standards.

**Acceptance criteria:**

- [ ] Project references zero internal projects.
- [ ] `RemediationProposal { PlanId, AnomalyId, ProposedAt }` matches §1.8.3 exactly.
- [ ] `RemediationProposalBatch { CycleId, EmittedAt, Proposals: IReadOnlyList<RemediationProposal> }`.
- [ ] `IRemediationProposalSink.PublishAsync` signature matches the locked contract.
- [ ] Public-API snapshot test committed.

**Verification:** `dotnet build src/InfraGate.Remediation.Contracts/InfraGate.Remediation.Contracts.csproj`.

**Dependencies:** None.

**Files likely touched:** `src/InfraGate.Remediation.Contracts/InfraGate.Remediation.Contracts.csproj` (new), and the records and seam listed above.

**Estimated scope:** Small.

---

#### Checkpoint: Foundation Complete

- [ ] All Phase 1 tasks merged.
- [ ] `dotnet build InfraGate.slnx` clean; `dotnet test InfraGate.slnx` green.
- [ ] No new `NoWarn` or analyser suppressions.
- [ ] Manual: from a human MCP client, `propose_plan` is callable with a token carrying `mcp:tools.propose` and produces a Plan Envelope visible at `/approvals/{challengeId}` after entering the code at `/approvals/code`.
- [ ] CONTEXT.md `### Remediation` subsections still in place (still DRAFT).

---

### Phase 2: Planner skeleton — connect, authenticate, log

#### Task 2.1: Bootstrap `InfraGate.Planner` project

**Description:** Create `src/InfraGate.Planner/InfraGate.Planner.csproj` as an ASP.NET `WebApplication` listening on port 3004. Bind `PlannerOptions` from configuration via `InfraGateEnvVarMappings`. Register `InfraGate.Observability` for Serilog. Reference `InfraGate.Observer.Contracts`, `InfraGate.Remediation.Contracts`, `InfraGate.ClientCredentials`, `InfraGate.Observability`, `InfraGate.RuntimeSafety`. **Do not** reference `InfraGate.Approvals` (audit-spine separation per §1.11.5).

**Acceptance criteria:** Builds clean; `PlannerOptions` record holds every key from §1.13.4; cadence/cap bounds validated at startup; project-reference assertion test asserts no `InfraGate.Approvals` reference.

**Verification:** `dotnet run --project src/InfraGate.Planner/InfraGate.Planner.csproj` starts; `/health` returns 503 until token cache is warm.

**Dependencies:** Task 1.7.

**Files likely touched:** `src/InfraGate.Planner/InfraGate.Planner.csproj`, `src/InfraGate.Planner/Program.cs`, `src/InfraGate.Planner/PlannerOptions.cs`, `src/InfraGate.Planner/PlannerConventions.cs`, `src/InfraGate.Planner/GlobalUsings.cs`, `InfraGate.slnx`.

**Estimated scope:** Medium.

---

#### Task 2.2: Wire MCP HTTP client with OAuth bearer (Planner)

**Description:** Register `ModelContextProtocol` HTTP client pointed at `INFRA_GATE_PLANNER_GATEWAY_BASE_URL`. Inject `ClientCredentialsBearerHandler` configured for `infra-gate-planner` + `mcp:tools.propose` + `mcp:tools.readonly`. Confirm end-to-end auth by calling `get_allowed_namespaces` at startup.

**Acceptance criteria:** MCP client established at startup; tool whitelist (read-only + `propose_plan`) enforced client-side; startup log `planner.startup.connected` with allowed-namespaces response.

**Verification:** With local gateway + Keycloak up, Planner starts and logs `planner.startup.connected`.

**Dependencies:** Tasks 1.6, 2.1, plus Phase 8 Keycloak client registration (Task 8.4 for the actual `infra-gate-planner` client) — for development, a hand-rolled Keycloak client config works.

**Files likely touched:** `src/InfraGate.Planner/Mcp/PlannerMcpClient.cs`, `src/InfraGate.Planner/Mcp/PlannerToolWhitelist.cs`, `src/InfraGate.Planner/Program.cs`.

**Estimated scope:** Medium.

---

#### Task 2.3: Handoff endpoint `POST /handoff/anomalies`

**Description:** Map `POST /handoff/anomalies` accepting `AnomalyHandoffBatch` (from `InfraGate.Observer.Contracts`). Validate bearer token (must be issued for the Observer's client; reject otherwise). Drop the batch onto an internal `Channel<AnomalyHandoffBatch>` for async processing. Return `202 Accepted` immediately.

**Acceptance criteria:** Endpoint returns `202 Accepted`; invalid auth returns `401`; non-Observer-azp returns `403`; well-formed batches reach the channel; malformed JSON returns `400`.

**Verification:** Integration test posts a valid batch + asserts channel receives it; unit test asserts auth rejection paths.

**Dependencies:** Tasks 2.1, 2.2.

**Files likely touched:** `src/InfraGate.Planner/Endpoints/HandoffEndpoint.cs`, `src/InfraGate.Planner/Cycle/AnomalyBatchQueue.cs` (Channel wrapper).

**Estimated scope:** Medium.

---

#### Task 2.4: Single `/health` endpoint (Planner)

**Description:** `/health` returns 200 once the token cache has a non-expired token; 503 otherwise. Identical shape to Observer §1.1.5.

**Acceptance criteria:** 503 on cold start; 200 after token acquisition; 503 again if token cache invalidated.

**Verification:** `curl http://localhost:3004/health`.

**Dependencies:** Task 2.2.

**Files likely touched:** `src/InfraGate.Planner/Endpoints/HealthEndpoint.cs`.

**Estimated scope:** Small.

---

#### Checkpoint: Planner Skeleton Connected

- [ ] Planner authenticates to gateway, receives batches at `/handoff/anomalies`, logs structured events. No outbound `propose_plan` calls yet.

---

### Phase 3: Planner detection — pick a mutation and call propose_plan

#### Task 3.1: Wire `Microsoft.Extensions.AI` `IChatClient` (Planner)

**Description:** Mirror Observer Task 3.2 with prefix swap (`INFRA_GATE_PLANNER_LLM_*`). Provider switch supports `anthropic` for v1; `openai`, `google`, `azure`, `ollama` as `NotImplementedException` arms. The `IChatClient` is registered as a DI service that `BatchProcessor` (Task 3.2) consumes.

**Acceptance criteria:** Provider factory returns the correct `IChatClient` for `anthropic`; missing API key fails fast at startup.

**Verification:** Unit test asserts factory returns correct type per provider value.

**Dependencies:** Task 2.1.

**Files likely touched:** `src/InfraGate.Planner/Llm/IChatClientFactory.cs`, `src/InfraGate.Planner/Llm/ChatClientFactory.cs`, `src/InfraGate.Planner/Llm/LlmProvider.cs`, `src/InfraGate.Planner/Program.cs`.

**Estimated scope:** Medium.

---

#### Task 3.2: BatchProcessor — internal pipeline

**Description:** Background `IHostedService` consumes the channel from Task 2.3. Per `AnomalyHandoffBatch`, the pipeline runs inline (no extracted `IAnomalyFilter`, `IRemediationDecider`, or `IPlanProposer` seams — these are private/internal methods within `BatchProcessor`):

1. **Filter** — drops `Status=Resolved`, drops Kinds outside v1 (`PodUnhealthy`, `DeploymentUnavailable`, `ServiceNoEndpoints`, `WarningEvent`), drops anomalies already tracked in dedupe store (`ConcurrentDictionary<AnomalyId, ActivePlanState>` capacity 1000). `DeploymentUnavailable` is intentionally included because the Planner Detection checkpoint expects the `examples/failing-deployment/` scenario to produce a `restart_deployment` proposal.
2. **Author system prompt** — loads `PlannerSystemPrompt.md` (embedded resource) and caches it.
3. **Per-anomaly LLM decision** — calls the `IChatClient` with system prompt + AnomalyReport JSON + bounded read-only tools per anomaly. Output parsed as `RemediationDecision { OperationType, Arguments, Reasoning }`. Validates `OperationType` against v1 allowlist, validates argument shapes against per-operation schemas. Returns null + counters on invalid output. Per-anomaly wall-clock cap via `CancellationTokenSource.CancelAfter(30s)`.
4. **Propose** — calls `propose_plan` on the gateway through the MCP client. Updates dedupe store with new active plan state.
5. **Collect** — gathers successful proposals into a `RemediationProposalBatch`; publishes to `IRemediationProposalSink` (Phase 4).

Per-batch cap (5 minutes) enforced. Sequential per-anomaly processing inside a batch.

**Acceptance criteria:** Filter is pure + deterministic; dedupe state evicts on terminal status; valid LLM output produces a `RemediationDecision`; invalid `OperationType` returns null + increments `infragate.planner.decision.invalid_operation`; invalid arguments returns null + increments `infragate.planner.decision.invalid_arguments`; wall-clock cap fires + increments `infragate.planner.decision.timeout`; successful gateway call returns a `RemediationProposal`; failure path increments `infragate.planner.propose.failed`; empty results don't trigger a publish.

**Verification:** Integration test against in-process gateway TestHost exercises a 3-anomaly batch end-to-end, asserting correct plan creation with `OperatorApproval` and `Requester=service:planner`. Unit tests for filter branches with `FixtureChatClient`.

**Dependencies:** Task 3.1, Task 1.4, Phase 4 sinks.

**Files likely touched:** `src/InfraGate.Planner/Cycle/BatchProcessor.cs`, `src/InfraGate.Planner/Prompts/PlannerSystemPrompt.md` (new embedded resource), `src/InfraGate.Planner/InfraGate.Planner.csproj` (EmbeddedResource), `src/InfraGate.Planner/Dedupe/PlannerDedupeStore.cs`, `src/InfraGate.Planner/Dedupe/ActivePlanState.cs`, `src/InfraGate.Planner/Decision/RemediationDecision.cs`, `src/InfraGate.Planner/Decision/OperationArgumentValidator.cs`.

**Estimated scope:** Large.

---

#### Checkpoint: Planner Detection End-to-End

- [ ] Manual: apply the failing-deployment YAML, Observer reports `DeploymentUnavailable`, Planner receives the batch and proposes `restart_deployment` via `propose_plan`, code email arrives at Mailpit, no Executor calls yet.

---

### Phase 4: Planner → Executor handoff sinks

#### Task 4.1: `LoggingRemediationProposalSink`

**Description:** Always-on sink that emits one `[LoggerMessage]` line per proposal: `CycleId`, `AnomalyId`, `PlanId`, `ProposedAt`.

**Acceptance criteria:** One structured log line per proposal at `Information`; no string interpolation.

**Verification:** Unit test with `CapturingLogger`.

**Dependencies:** Task 1.7.

**Files likely touched:** `src/InfraGate.Planner/Handoff/LoggingRemediationProposalSink.cs`.

**Estimated scope:** Small.

---

#### Task 4.2: `JsonFileRemediationProposalSink` (opt-in)

**Description:** Opt-in sink that writes `{cycleId}.json` to `Planner:FileSink:Root`. Atomic write (`.tmp` + rename). No rotation in v1.

**Acceptance criteria:** Sink registered only when root is set; atomic write; one file per batch.

**Verification:** Unit test against temp directory.

**Dependencies:** Task 1.7.

**Files likely touched:** `src/InfraGate.Planner/Handoff/JsonFileRemediationProposalSink.cs`, `src/InfraGate.Planner/Handoff/JsonFileSinkOptions.cs`.

**Estimated scope:** Small.

---

#### Task 4.3: `HttpRemediationProposalSink`

**Description:** Pushes `RemediationProposalBatch` via `POST` to `INFRA_GATE_PLANNER_EXECUTOR_HANDOFF_URL` with `InfraGate.ClientCredentials` bearer. `202 Accepted` is success; other status codes log + count + drop (fire-and-forget per §1.8.5).

**Acceptance criteria:** Successful POST counted; non-202 increments `infragate.planner.handoff.http_failed`; `429` from the Executor is logged separately as backpressure.

**Verification:** Unit test with `HttpMessageHandler` fake.

**Dependencies:** Task 1.7.

**Files likely touched:** `src/InfraGate.Planner/Handoff/HttpRemediationProposalSink.cs`, `src/InfraGate.Planner/Handoff/HttpHandoffOptions.cs`.

**Estimated scope:** Medium.

---

#### Task 4.4: `CompositeRemediationProposalSink` + DI registration

**Description:** Composes registered sinks; per-sink failure isolated via try/catch + log + counter. Registered as the entry-point `IRemediationProposalSink`.

**Acceptance criteria:** One throwing sink does not prevent others; each invocation tagged with `SinkName`.

**Verification:** Unit test with deliberately-throwing fake sink.

**Dependencies:** Tasks 4.1, 4.2, 4.3.

**Files likely touched:** `src/InfraGate.Planner/Handoff/CompositeRemediationProposalSink.cs`, `src/InfraGate.Planner/Program.cs`.

**Estimated scope:** Small.

---

#### Task 4.5: Add `HttpAnomalyHandoffSink` to `InfraGate.Observer`

**Description:** Symmetric of Task 4.3: pushes `AnomalyHandoffBatch` to `INFRA_GATE_OBSERVER_PLANNER_HANDOFF_URL` with bearer. Registers conditionally when the URL env var is set.

**Acceptance criteria:** Sink registered only when URL is set; successful POST counted; non-202 increments `infragate.observer.handoff.http_failed`.

**Verification:** Observer integration test asserts an HTTP request is sent to the configured URL when an anomaly batch is published.

**Dependencies:** None within this roadmap (extension to existing Observer); needs Task 2.3 in Planner to receive the calls.

**Files likely touched:** `src/InfraGate.Observer/Handoff/HttpAnomalyHandoffSink.cs` (new), `src/InfraGate.Observer/Handoff/HttpHandoffOptions.cs` (new), `src/InfraGate.Observer/Program.cs` (DI conditional).

**Estimated scope:** Medium.

---

#### Checkpoint: Handoff Delivered End-to-End

- [ ] With Observer's `HttpAnomalyHandoffSink` pointed at the local Planner and Planner's `HttpRemediationProposalSink` pointed at a curl-driven listener, the full pipeline emits `RemediationProposal`s downstream.

---

### Phase 5: Executor skeleton

#### Task 5.1: Bootstrap `InfraGate.Executor` project

**Description:** Create `src/InfraGate.Executor/InfraGate.Executor.csproj` as an ASP.NET `WebApplication` on port 3005. Bind `ExecutorOptions` from configuration via `InfraGateEnvVarMappings`. Reference `InfraGate.Remediation.Contracts`, `InfraGate.ClientCredentials`, `InfraGate.Observability`, `InfraGate.RuntimeSafety`. **Do not** reference `InfraGate.Approvals` or `InfraGate.Observer.Contracts` (clean ownership per Q9 follow-up).

**Acceptance criteria:** Builds clean; project-reference assertion test asserts the absence of `InfraGate.Approvals` and `InfraGate.Observer.Contracts`.

**Verification:** `dotnet run --project src/InfraGate.Executor/InfraGate.Executor.csproj` starts.

**Dependencies:** Task 1.7.

**Files likely touched:** `src/InfraGate.Executor/InfraGate.Executor.csproj`, `src/InfraGate.Executor/Program.cs`, `src/InfraGate.Executor/ExecutorOptions.cs`, `src/InfraGate.Executor/ExecutorConventions.cs`, `src/InfraGate.Executor/GlobalUsings.cs`, `InfraGate.slnx`.

**Estimated scope:** Medium.

---

#### Task 5.2: Wire MCP HTTP client with OAuth bearer (Executor)

**Description:** Mirror Task 2.2 with `infra-gate-executor` client and `mcp:tools.execute` scope. Whitelist: `wait_for_plan_approval`, `execute_approved_plan` only.

**Acceptance criteria:** MCP client established; whitelist enforced client-side; startup log `executor.startup.connected`.

**Verification:** With local gateway + Keycloak up, Executor starts and authenticates.

**Dependencies:** Tasks 1.5, 1.6, 5.1.

**Files likely touched:** `src/InfraGate.Executor/Mcp/ExecutorMcpClient.cs`, `src/InfraGate.Executor/Mcp/ExecutorToolWhitelist.cs`, `src/InfraGate.Executor/Program.cs`.

**Estimated scope:** Medium.

---

#### Task 5.3: Handoff endpoint `POST /handoff/proposals`

**Description:** Map `POST /handoff/proposals` accepting `RemediationProposalBatch`. Validate bearer token (must be Planner identity). Drop onto internal channel `Channel<RemediationProposal>`. Return `202 Accepted` (or `429` if the watcher concurrency cap is saturated).

**Acceptance criteria:** Endpoint returns 202 on success; 401 on missing auth; 403 on non-Planner azp; 429 when at the concurrency cap (`SemaphoreSlim` exhausted); proposals reach the channel.

**Verification:** Integration test.

**Dependencies:** Tasks 5.1, 5.2.

**Files likely touched:** `src/InfraGate.Executor/Endpoints/HandoffEndpoint.cs`, `src/InfraGate.Executor/Queue/ProposalQueue.cs`.

**Estimated scope:** Medium.

---

#### Task 5.4: Single `/health` endpoint (Executor)

**Description:** Mirror Task 2.4.

**Verification:** `curl http://localhost:3005/health`.

**Dependencies:** Task 5.2.

**Files likely touched:** `src/InfraGate.Executor/Endpoints/HealthEndpoint.cs`.

**Estimated scope:** Small.

---

#### Checkpoint: Executor Skeleton Connected

- [ ] Executor authenticates, receives proposals via `/handoff/proposals`, logs structured events. No `wait_for_plan_approval` calls yet.

---

### Phase 6: Executor lifecycle — wait + execute

#### Task 6.1: Plan watcher

**Description:** Background `IHostedService` consumes from the proposal channel (Task 5.3). For each `RemediationProposal`, spawns a tracked Task that:

1. Records the planId in `IExecutorDedupeStore` (skip if already tracked).
2. Calls `wait_for_plan_approval(planId, timeoutSeconds=900)`.
3. On approval: calls `execute_approved_plan(planId)`, logs the outcome.
4. On timeout/expiry: logs + counts + cleans up.
5. On error: logs + counts + cleans up.

The `SemaphoreSlim` from Task 5.3 is acquired before spawning, released when the Task completes.

**Acceptance criteria:** End-to-end test: a proposal arrives, watcher parks, approval is recorded in the gateway, watcher resumes and calls `execute_approved_plan`, gateway records `execution.succeeded`. Watcher dedupe prevents double-execution on duplicate proposals. Timeout path increments `infragate.executor.watch.timeout`.

**Verification:** Integration test with stub MCP server + manual gateway approval injection.

**Dependencies:** Tasks 5.2, 5.3.

**Files likely touched:** `src/InfraGate.Executor/Watch/PlanWatcher.cs`, `src/InfraGate.Executor/Watch/IExecutorDedupeStore.cs`, `src/InfraGate.Executor/Watch/ExecutorDedupeStore.cs`, `src/InfraGate.Executor/Watch/ActiveExecutionState.cs`.

**Estimated scope:** Large.

---

#### Checkpoint: Loop Closes End-to-End

- [ ] Manual: failing-deployment scenario → Observer emits anomaly → Planner proposes restart → operator visits Mailpit, retrieves code, enters at `/approvals/code`, lands on `/approvals/{challengeId}`, authenticates via Keycloak, clicks approve → Executor wakes, executes restart, gateway audits execution.succeeded → next Observer cycle emits Status=Resolved.

---

### Phase 7: Observability

#### Task 7.1: Structured event taxonomy via `[LoggerMessage]` (Planner)

**Description:** Define `PlannerLogEvents` with one partial method per event:

- `planner.startup.connected`
- `planner.handoff.batch_received`
- `planner.filter.dropped` (with reason)
- `planner.decision.completed`
- `planner.decision.invalid_operation`
- `planner.decision.invalid_arguments`
- `planner.decision.timeout`
- `planner.propose.succeeded`
- `planner.propose.failed`
- `planner.handoff.published` (per sink)
- `planner.handoff.failed` (per sink)

**Acceptance criteria:** All call sites use source-generated methods; `AnomalyId` and `PlanId` properties always present where relevant.

**Verification:** `dotnet build` zero logging warnings; unit test with `CapturingLogger`.

**Dependencies:** Tasks 3.4, 3.5, 3.6, 4.4.

**Files likely touched:** `src/InfraGate.Planner/Diagnostics/PlannerLogEvents.cs`, plus call-site replacements.

**Estimated scope:** Medium.

---

#### Task 7.2: Structured event taxonomy via `[LoggerMessage]` (Executor)

**Description:** Define `ExecutorLogEvents`:

- `executor.startup.connected`
- `executor.handoff.batch_received`
- `executor.watch.started` (per planId)
- `executor.watch.approved` (per planId)
- `executor.watch.timeout`
- `executor.watch.failed`
- `executor.execute.succeeded`
- `executor.execute.failed`
- `executor.execute.blocked`

**Acceptance criteria:** Mirrors Task 7.1 with `PlanId` enrichment.

**Dependencies:** Task 6.1.

**Files likely touched:** `src/InfraGate.Executor/Diagnostics/ExecutorLogEvents.cs`.

**Estimated scope:** Small–Medium.

---

#### Task 7.3: Metrics meters

**Description:** `PlannerMetrics` static class with `Meter("InfraGate.Planner", "1.0")` plus counters and histograms for every event in §7.1. Includes `infragate.planner.llm.tokens` histogram for LLM token usage visibility (referenced in §4 Risks). `ExecutorMetrics` mirrors for §7.2.

**Acceptance criteria:** Every metric counted exactly once; tag names lowercase snake-case; `dotnet-counters monitor` shows live values during a manual run.

**Verification:** `dotnet-counters monitor -n InfraGate.Planner --counters InfraGate.Planner` and equivalent for Executor.

**Dependencies:** Tasks 7.1, 7.2.

**Files likely touched:** `src/InfraGate.Planner/Diagnostics/PlannerMetrics.cs`, `src/InfraGate.Executor/Diagnostics/ExecutorMetrics.cs`.

**Estimated scope:** Medium.

---

#### Task 7.4: `AnomalyId` / `PlanId` log enrichment

**Description:** Ensure every Planner log line scoped to one anomaly carries `AnomalyId`; every Executor log line scoped to one plan carries `PlanId`. Implemented via `LogContext.PushProperty` inside the per-anomaly / per-plan scope.

**Acceptance criteria:** Manual: tail log output; correlation properties consistently present in scope and absent outside.

**Verification:** Manual.

**Dependencies:** Tasks 7.1, 7.2.

**Files likely touched:** Planner batch processor, Executor plan watcher.

**Estimated scope:** Small.

---

#### Checkpoint: Observable Operation

- [ ] `dotnet-counters` shows all expected metrics for both agents.
- [ ] Logs correlated by `AnomalyId` (Planner) and `PlanId` (Executor) end-to-end.
- [ ] Neither agent's log goes through `IApprovalAuditPublisher` — manual review + project-reference assertion test passes.

---

### Phase 8: Deployment

#### Task 8.1: `PlannerProfile` + `ExecutorProfile` records + run-profiles integration

**Description:** Add `src/InfraGate.RunProfiles/PlannerProfile.cs` and `src/InfraGate.RunProfiles/ExecutorProfile.cs` as component-profile records. Extend `deploy/run-profiles.yaml` `local-docker` and `local-host` with planner + executor configuration. `EnvFileRenderer` and `AppSettingsRenderer` pick them up via existing convention.

**Acceptance criteria:** `dotnet run --project src/InfraGate.RunProfiles -- generate --profile local-docker` produces `.env` with every `INFRA_GATE_PLANNER_*` and `INFRA_GATE_EXECUTOR_*` key; both validate; Docker profile uses internal DNS (`keycloak:8080`, `gateway:3001`, `planner:3004`, `executor:3005`, `mailpit:1025`).

**Verification:** `dotnet test tests/InfraGate.RunProfiles.Tests/` passes new profile-render tests.

**Dependencies:** Tasks 1.7, 2.1, 5.1.

**Files likely touched:** `src/InfraGate.RunProfiles/PlannerProfile.cs`, `src/InfraGate.RunProfiles/ExecutorProfile.cs`, `src/InfraGate.RunProfiles/RunProfileConventions.cs`, `src/InfraGate.RunProfiles/RunProfileDocument.cs`, `src/InfraGate.RunProfiles/EnvFileRenderer.cs`, `src/InfraGate.RunProfiles/AppSettingsRenderer.cs`, `deploy/run-profiles.yaml`, `tests/InfraGate.RunProfiles.Tests/`.

**Estimated scope:** Medium–Large.

---

#### Task 8.2: Planner + Executor Dockerfiles

**Description:** Multi-stage Dockerfiles using alpine sdk + aspnet images. Set `ASPNETCORE_URLS=http://+:3004` and `:3005` respectively. Non-root user. Expose the port.

**Acceptance criteria:** Both `docker build` succeed; non-root; graceful failure on missing env vars.

**Verification:** Manual: build + run, observe expected startup-validation failure.

**Dependencies:** Tasks 2.1, 5.1.

**Files likely touched:** `src/InfraGate.Planner/Dockerfile`, `src/InfraGate.Planner/.dockerignore`, `src/InfraGate.Executor/Dockerfile`, `src/InfraGate.Executor/.dockerignore`.

**Estimated scope:** Small.

---

#### Task 8.3: Extend `deploy/local-oauth/compose.yaml`

**Description:** Add `planner`, `executor`, and `mailpit` services. Mount `./.mcp-remediation/proposals` as the JSON file sink root (parallels `.mcp-approvals`, `.mcp-observer/findings`). `depends_on` chains observer → planner; planner → executor; planner → mailpit. Env source from `.env` rendered by Task 8.1.

**Acceptance criteria:** `docker compose -f deploy/local-oauth/compose.yaml up` brings up all five services (keycloak, gateway, server, observer, planner, executor, mailpit); Mailpit UI on `:8025`; JSON sink dir created on host after a cycle.

**Verification:** Manual.

**Dependencies:** Tasks 8.1, 8.2, plus Task 1.3 (Mailpit was added there).

**Files likely touched:** `deploy/local-oauth/compose.yaml`.

**Estimated scope:** Small–Medium.

---

#### Task 8.4: Register `infra-gate-planner` and `infra-gate-executor` Keycloak clients

**Description:** Add both clients to the realm export. Grant type: `client_credentials`. Scopes: `mcp:tools.propose` + `mcp:tools.readonly` (planner); `mcp:tools.execute` (executor). Add a `kubernetes-operators` group + one demo user as member (for the approval flow). Document secrets as `INFRA_GATE_PLANNER_CLIENT_SECRET` and `INFRA_GATE_EXECUTOR_CLIENT_SECRET` in `docs/configuration.md`.

**Acceptance criteria:** Realm import includes both clients + the operator group; `curl` to Keycloak `/token` with each client returns a JWT with the expected `azp` + scopes; demo user logs in and is a member of `kubernetes-operators`.

**Verification:** End-to-end manual flow.

**Dependencies:** Tasks 1.5, 1.6.

**Files likely touched:** `deploy/local-oauth/realm-export.json` (or equivalent), `docs/configuration.md`.

**Estimated scope:** Medium.

---

#### Checkpoint: One-Command Demo

- [ ] `docker compose -f deploy/local-oauth/compose.yaml up` brings up the full stack.
- [ ] Apply `examples/failing-deployment/deployment.yaml`.
- [ ] Observer detects DeploymentUnavailable → Planner proposes restart → email arrives in Mailpit (`http://localhost:8025`) → operator copies code → visits `http://localhost:3001/approvals/code` → enters code → lands on `/approvals/{challengeId}` → logs in as the operator user → clicks approve → Executor wakes → restart executed → next Observer cycle emits Status=Resolved.
- [ ] Audit log: zero unauthorised tool calls.

---

### Phase 9: Tests

#### Task 9.1: Generic core tests for Operator Approval Policy

**Description:** Unit + integration tests in `tests/InfraGate.Approvals.Tests/` and `tests/InfraGate.Approvals.Postgres.Tests/` covering the new `OperatorApproval` variant: canonicalisation determinism, grant validation positive + negative (group present, group absent, mismatched group, JWT without `groups` claim), persistence round-trip.

**Acceptance criteria:** Every grant-validation branch covered with positive + negative case.

**Verification:** `dotnet test tests/InfraGate.Approvals.Tests/` and `tests/InfraGate.Approvals.Postgres.Tests/`.

**Dependencies:** Task 1.1.

**Files likely touched:** `tests/InfraGate.Approvals.Tests/`, `tests/InfraGate.Approvals.Postgres.Tests/`.

**Estimated scope:** Medium.

---

#### Task 9.2: Gateway tests for `propose_plan` + Approval Access Code + email

**Description:** Tests in `tests/InfraGate.McpGateway.Tests/` covering scope enforcement, operationType allowlist enforcement, happy path producing a Plan Envelope + a code in the store + a captured email, email failure does not fail the call, code consume happy + expired + already-used paths.

**Acceptance criteria:** Each path covered; stub `IApprovalEmailSender` used in tests; `CapturingLogger` asserts structured logs.

  > **Implementation note (verified Phase 1):** Existing gateway test fixtures (`GatewayHttpMcpIntegrationTests`, `GatewayApprovalServiceTests`, `GatewayToolDispatcherTests`, `GatewayDiWiringTests`) still register `SameSubjectAuthorizationCheck` rather than `ApprovalPolicyAuthorizationCheck`. These fixtures must be updated to use `ApprovalPolicyAuthorizationCheck` (which handles both `SameSubject` and `OperatorApproval`), and new tests must cover the `OperatorApproval` authorization path through `GatewayApprovalService` — not just through `ApprovalGrantValidationTests`.

**Verification:** `dotnet test tests/InfraGate.McpGateway.Tests/`.

**Dependencies:** Tasks 1.2, 1.3, 1.4, 1.5.

**Files likely touched:** `tests/InfraGate.McpGateway.Tests/UnitTests/ProposePlanTests.cs`, `tests/InfraGate.McpGateway.Tests/UnitTests/ApprovalAccessCodeTests.cs`, `tests/InfraGate.McpGateway.Tests/UnitTests/EmailSenderTests.cs`.

**Estimated scope:** Large.

---

#### Task 9.3: Planner unit tests

**Description:** Tests in `tests/InfraGate.Planner.Tests/` for filter branches, decider with `FixtureChatClient` (happy + each rejection path), proposer with stub MCP client, dedupe state machine, sink fan-out.

**Acceptance criteria:** Every Severity rule / filter / decision branch covered with positive + negative case.

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/`.

**Dependencies:** Tasks 3.3, 3.4, 3.5, 4.4.

**Files likely touched:** `tests/InfraGate.Planner.Tests/` (new project).

**Estimated scope:** Large.

---

#### Task 9.4: Planner integration tests

**Description:** Tests in `tests/InfraGate.Planner.IntegrationTests/` against in-process Gateway TestHost + stub MCP fixtures + `FixtureChatClient`. Exercise full handoff → filter → decider → propose → publish.

**Acceptance criteria:** Failing-deployment fixture produces an expected `RemediationProposalBatch`; AnomalyId-to-PlanId correlation asserted.

**Verification:** `dotnet test tests/InfraGate.Planner.IntegrationTests/`.

**Dependencies:** Tasks 9.3, 4.5.

**Files likely touched:** `tests/InfraGate.Planner.IntegrationTests/` (new project).

**Estimated scope:** Large.

---

#### Task 9.5: Executor unit tests

**Description:** Tests in `tests/InfraGate.Executor.Tests/` for handoff endpoint auth, dedupe, plan watcher state machine, concurrency cap.

**Verification:** `dotnet test tests/InfraGate.Executor.Tests/`.

**Dependencies:** Tasks 5.3, 6.1.

**Files likely touched:** `tests/InfraGate.Executor.Tests/` (new project).

**Estimated scope:** Medium–Large.

---

#### Task 9.6: Executor integration tests

**Description:** In-process Gateway TestHost + stub MCP fixtures. Exercise: proposal → wait → approve (injected) → execute → outcome.

**Verification:** `dotnet test tests/InfraGate.Executor.IntegrationTests/`.

**Dependencies:** Task 9.5.

**Files likely touched:** `tests/InfraGate.Executor.IntegrationTests/` (new project).

**Estimated scope:** Large.

---

#### Task 9.7: Opt-in E2E remediation test

**Description:** `tests/InfraGate.Remediation.E2E.Tests/` gated by `INFRA_GATE_RUN_REMEDIATION_E2E=1`. Real Keycloak (Testcontainer), real Mailpit (Testcontainer), real Gateway TestHost, developer-provided K8s cluster, stubbed LLM by default with `INFRA_GATE_PLANNER_REAL_LLM=1` opt-in. Full Observer→Planner→Approval→Executor loop.

**Verification:** `INFRA_GATE_RUN_REMEDIATION_E2E=1 dotnet test tests/InfraGate.Remediation.E2E.Tests/`.

**Dependencies:** Task 9.6 + Phase 8 complete.

**Files likely touched:** `tests/InfraGate.Remediation.E2E.Tests/` (new project).

**Estimated scope:** Large.

---

#### Checkpoint: Test Suite Green

- [ ] All non-E2E tests pass in CI.
- [ ] Opt-in E2E passes locally with developer cluster.
- [ ] Code coverage acceptable on Planner + Executor + new gateway surfaces.

---

### Phase 10: Documentation

#### Task 10.1: `src/InfraGate.Planner/README.md`

**Description:** Brief README following the per-project pattern. Sections: Runtime Flow, Important Contracts, Settings (link to `docs/configuration.md`), Verification.

**Acceptance criteria:** Matches existing per-project README style; lists the v1 operation menu; cross-links to ADRs 0017–0019.

**Dependencies:** All Planner implementation tasks merged.

**Estimated scope:** Small.

---

#### Task 10.2: `src/InfraGate.Executor/README.md`

**Description:** Mirror Task 10.1 for the Executor.

**Dependencies:** All Executor implementation tasks merged.

**Estimated scope:** Small.

---

#### Task 10.3: Update root `README.md` and `AGENTS.md`

**Description:** Add Planner + Executor to the "Runtime projects" list. Update the Solution Map in `AGENTS.md`. Add ADR-0017/0018/0019 to any project listing.

**Estimated scope:** Small.

---

#### Task 10.4: Update `examples/failing-deployment/README.md`

**Description:** Add a "Remediation demo" section alongside the existing Observer demo. Steps: bring up the stack, apply the deployment, wait for proposed plan, open Mailpit, copy code, approve, observe execution + Resolved.

**Estimated scope:** Small.

---

#### Task 10.5: Extend `docs/configuration.md`

**Description:** Document every new env var: `INFRA_GATE_PLANNER_*`, `INFRA_GATE_EXECUTOR_*`, `INFRA_GATE_GATEWAY_SMTP_*`, `INFRA_GATE_OPERATOR_GROUP`, `INFRA_GATE_OPERATOR_EMAIL`, `INFRA_GATE_OBSERVER_PLANNER_HANDOFF_URL`, `INFRA_GATE_PLANNER_EXECUTOR_HANDOFF_URL`. Default, range, production guidance per env var.

**Acceptance criteria:** `rg -n 'INFRA_GATE_PLANNER_\|INFRA_GATE_EXECUTOR_' README.md docs src/*/README.md` — every match outside `docs/configuration.md` is a link, not a duplicate definition.

**Estimated scope:** Small–Medium.

---

#### Task 10.6: Finalise `CONTEXT.md` — remove DRAFT markers

**Description:** Remove the `> **DRAFT** — added during grilling...` lines from both `### Remediation` subsections and the three new Flagged Ambiguity entries. Confirm via `rg -n 'DRAFT — added during grilling'` returning zero matches.

**Acceptance criteria:** Zero DRAFT markers remain in `CONTEXT.md`; all `### Remediation` content otherwise unchanged from Phase 0 grilling output.

**Verification:** `rg DRAFT CONTEXT.md` returns no matches.

**Dependencies:** All implementation tasks merged (so the language has proven stable).

**Files likely touched:** `CONTEXT.md`.

**Estimated scope:** XS.

---

#### Checkpoint: Documentation Verified

- [ ] Every README claim maps to a real code construct.
- [ ] `docs/configuration.md` is the single source of truth for env vars.
- [ ] CONTEXT.md draft markers removed.

---

### Phase 11: VS Code companion (lowest priority — optional)

#### Task 11.1: `agents/remediation-planner.agent.md`

**Description:** Create a VS Code custom agent file that triggers a manual handoff to a local Planner. Body explains the persona's scope: "I curate AnomalyReports and hand them to the local Planner; I do not call mutation tools directly."

**Acceptance criteria:** `.agent.md` frontmatter valid; manual: open VS Code, invoke `@remediation-planner`, observe successful handoff trigger.

**Dependencies:** Phase 8 complete.

**Estimated scope:** Small.

---

#### Checkpoint: Optional Companion Available

- [ ] VS Code custom agent invokes the deployed Planner successfully.

---

## 6. Cross-Cutting Code Standards Reminders

Pulled from `code-standards` for emphasis during implementation. All apply identically to Planner and Executor:

- **File-scoped namespaces** in every new file.
- **`sealed` by default** on classes; only leave open when subclassing is intentional.
- **`record` / `record struct`** for DTOs (every type in `InfraGate.Remediation.Contracts`).
- **Primary constructors** where applicable.
- **`var`** only when the right-hand side makes the type obvious; explicit otherwise; never for primitives.
- **`Async` suffix** on async methods; **`CancellationToken`** on all async I/O.
- **`ConfigureAwait(false)`** on every awaited task in library/tool code.
- **`IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>`** on public surfaces.
- **`[LoggerMessage]`** source generator on every Planner and Executor log call.
- **Magic strings**: every MCP tool name, env-var key, scope name, audit identity prefix goes into a named conventions class (`PlannerConventions`, `ExecutorConventions`, `RemediationConventions`).
- **One meaningful top-level type per file.** No `#region`.
- **`GlobalUsings.cs`** per project.
- **Booleans named as questions** (`HasActivePlan`, `IsTruncated`).
- **Catch specific exceptions**, not `Exception` (except top-level batch/cycle boundary).
- **Test naming**: `Method_State_ExpectedResult`; `[Theory]` + `[InlineData]` over duplicated `[Fact]`.

---

## 7. Cross-Cutting Architecture Reminders

Pulled from `improve-codebase-architecture`:

- **Deepening seams**: `IRemediationProposalSink` mirrors `IAnomalyHandoffSink` exactly. `IApprovalEmailSender` and `IApprovalAccessCodeStore` are deep modules — small interface, substantial behaviour (template rendering + SMTP, CSPRNG + persistence + expiry).
- **Locality**: `propose_plan` orchestration concentrates in `ProposePlanHandler`. The Planner's per-anomaly orchestration concentrates in `BatchProcessor`. The Executor's per-plan orchestration concentrates in `PlanWatcher`. Cap enforcement, dedupe, sink fan-out happen at each owner — not scattered.
- **Avoid shallow modules**: do not extract single-use helpers from `BatchProcessor` or `PlanWatcher` "for testability." The orchestrator interface is what the integration tests assert against. Per plan review, `IAnomalyFilter`, `IRemediationDecider`, and `IPlanProposer` (Phase 3) are inlined into `BatchProcessor` as private/internal methods rather than extracted seams — test via `BatchProcessor`'s public contract.
- **The two-process boundary is the architectural seam.** Don't punch holes through it by importing Planner-internal types into Executor or vice versa. Contracts live in `InfraGate.Remediation.Contracts`. Anomaly types live in `InfraGate.Observer.Contracts` and the Planner is the only Remediation-side consumer.

---

## 8. Open Questions (deferred — not blocking v1)

Surfaced during grilling but explicitly deferred. None block v1 implementation:

- **One-click email magic-link URL** as an alternative or supplement to the code-entry page. Trivial extension — `GET /approvals/code/{code}` validates and redirects.
- **`set_image` operation** for `PodUnhealthy / ImagePullBackOff` scenarios. Requires structuring `RemediationHint` with a typed `SuggestedImage` field on the Observer side.
- **Cancellation of in-flight Planner LLM call when a Resolved AnomalyReport arrives for the same anomaly.** v1 lets the in-flight call complete.
- **Persistent Planner dedupe state** (Postgres / Redis / file). Restart resets — same trade-off Observer ships with.
- **Persistent Executor parked-call state.** Restart loses parked calls; operator can re-trigger via approval URL.
- **Multi-Executor coordination** — claim-then-execute split or queue with single-consumer semantics.
- **Delegated Approval Policy** (per-plan named approvers).
- **Multi-Party Approval Policy** (N-of-M signatures with non-terminal Approval Challenge outcomes).
- **Per-namespace operator groups** (different groups approving different namespaces).
- **OpenTelemetry exporters** for both agents.
- **Email retries / queue** for SMTP send failures.
- **HTML email / templating engine / DKIM signing.**
- **Production secret management** for Planner/Executor client secrets.
- **VS Code agent for the Executor.**

---

## 9. References

- `CONTEXT.md` — canonical glossary including the `### Remediation` subsections (DRAFT until Task 10.6).
- `docs/adr/0017-two-process-planner-executor-split.md` — defense-in-depth from scope split.
- `docs/adr/0018-propose-plan-as-new-mcp-tool.md` — clean per-caller-type contract separation.
- `docs/adr/0019-operator-approval-policy.md` — new ApprovalPolicy subtype.
- `docs/adr/0016-extract-infragate-clientcredentials-shared-library.md` — `InfraGate.ClientCredentials` infrastructure reused.
- `docs/adr/0015-anomaly-observer-excluded-from-audit-spine.md` — discipline mirrored for Planner and Executor.
- `docs/adr/0012-hybrid-severity-llm-proposes-rules-win.md` — analogous LLM-vs-deterministic decision (deterministic guardrails win on operationType + arguments).
- `docs/adr/0006-mcpgateway-pure-generic-approval-core-dynamic-domain-adapter-seam.md` — Pre-Execution Gate ownership split that Operator Approval Policy participates in.
- `docs/mutation-approval-profile.md` — context for why the new policy plugs into the generic core.
- `docs/mutation-approval-flow.md` — the existing approval lifecycle the Planner + Executor join, not replace.
- `.agents/Plans/Roadmap/anomaly-observer-roadmap.md` — direct precedent for project layout, test layering, and deployment patterns.
- `.agents/skills/planning-and-task-breakdown/SKILL.md` — task-sizing rules.
- `.agents/skills/code-standards/SKILL.md` — conventions to apply throughout.
- `.agents/skills/improve-codebase-architecture/SKILL.md` — deepening / seam vocabulary.
- `.agents/skills/verify-readme-docs/SKILL.md` — workflow for Phase 10.
- `examples/failing-deployment/` — canonical demo scenario.

---

## 10. Suggested Execution Order

1. **Phase 1 (Foundation)** — Task 1.1 first (other tasks depend on the policy variant); then 1.2, 1.3, 1.5, 1.6, 1.7 in parallel; then 1.4 (depends on 1.1, 1.2, 1.3).
2. **Phase 2 (Planner Skeleton)** — Tasks 2.1 → 2.2 → 2.3 → 2.4 in sequence.
3. **Phase 3 (Planner Detection)** — Task 3.1 first (IChatClient infrastructure); then Task 3.2 (BatchProcessor, depends on 3.1 + 1.4 + Phase 4).
4. **Phase 4 (Handoff Sinks)** — Tasks 4.1 + 4.2 + 4.3 in parallel; then 4.4; then 4.5 (Observer extension, can land any time after Phase 2 makes the Planner endpoint reachable).
5. **Phase 5 (Executor Skeleton)** — Tasks 5.1 → 5.2 → 5.3 → 5.4 in sequence. Can run in parallel with Phases 3 and 4.
6. **Phase 6 (Executor Lifecycle)** — Task 6.1.
7. **Phase 7 (Observability)** — Tasks 7.1 + 7.2 in parallel; then 7.3; then 7.4.
8. **Phase 8 (Deployment)** — Task 8.1, then 8.2 + 8.4 in parallel, then 8.3.
9. **Phase 9 (Tests)** — Tasks 9.1 + 9.2 in parallel after Phase 1; 9.3 + 9.5 in parallel after Phases 3 + 6; 9.4 + 9.6 in parallel after 9.3 / 9.5; 9.7 last (gated on Phase 8).
10. **Phase 10 (Docs)** — Tasks 10.1–10.5 in parallel after all implementation phases; 10.6 last (after the language has proven stable through implementation).
11. **Phase 11 (VS Code)** — Optional; runs whenever Phase 8 is done.

Major checkpoints (Foundation, Planner Skeleton, Planner Decides, Handoff Delivered, Executor Skeleton, Loop Closes, Observable, One-Command Demo, Test Suite Green, Docs Verified) are explicit go/no-go gates. Do not skip checkpoints.
