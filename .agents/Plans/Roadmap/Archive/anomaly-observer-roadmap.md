# Anomaly Observer Implementation Roadmap

**Purpose:** Implementation plan for the new `InfraGate.Observer` — an LLM-driven, deployable agent that periodically inspects Kubernetes through the MCP gateway's read-only tools and emits structured Anomaly Reports for a future executor to consume.

**Source:** This plan is the output of a `grill-with-docs` session that walked every design branch one decision at a time. Every choice below is intentional. The plan is sized per [`planning-and-task-breakdown`](../../skills/planning-and-task-breakdown/SKILL.md), follows [`code-standards`](../../skills/code-standards/SKILL.md) throughout, uses the architecture vocabulary from [`improve-codebase-architecture`](../../skills/improve-codebase-architecture/SKILL.md), and respects [`verify-readme-docs`](../../skills/verify-readme-docs/SKILL.md) for all doc changes.

---

## 0. Executive Summary

InfraGate today is a mutation-approval reference implementation: AI proposes Kubernetes changes, humans approve them, and the gateway enforces deterministic execution. The **Anomaly Observer** is the complementary read-side agent. It runs continuously, watches the cluster through the same gateway interface any other MCP client uses, classifies anomalies with LLM-assisted reasoning bounded by deterministic Severity rules, and publishes structured **Anomaly Reports** to a downstream sink so a future executor can act on them within the existing approval flow.

What this is **not**:

- It is not part of the **Generic Approval Core**, the **Audit Spine**, or any **Pre-Execution Gate**.
- It is not authorised to call mutation tools — gateway OAuth and client-side whitelist both enforce that.
- It is not the executor. Handoff transport is contract-shape-only in v1.
- It is not a Kubernetes operator or controller. It is a peer MCP client.

What this **is**:

- A `Microsoft.Extensions.AI`-based hybrid agent (deterministic baseline fetch + agentic LLM analysis with capped tool calls + deterministic Severity classification) deployed as a long-running `IHostedService` next to the gateway.
- Bound by the same OAuth, namespace allowlist, and bounded-read guardrails as any other MCP client — it eats its own dogfood.

---

## 1. Architecture Decisions (Locked)

Every decision below was made deliberately during grilling. They are the source of truth for downstream task interpretation. Numbering is for reference only — these are not implementation order.

### 1.1 Language, runtime, project layout

| # | Decision | Rationale |
|---|---|---|
| 1.1.1 | C# / .NET 10, inheriting the repo's `Directory.Build.props` (`TreatWarningsAsErrors=true`, Meziantou all-warnings, latest analysers) | Matches every existing runtime project. Avoids a polyglot CI lane. |
| 1.1.2 | New project at `src/InfraGate.Observer/` | Mirrors `InfraGate.McpGateway`, `InfraGate.McpServer` naming. |
| 1.1.3 | Hosted as an ASP.NET `WebApplication` with an `IHostedService` poll loop | Needed for `/observe-now` and `/health` HTTP surface anyway. |
| 1.1.4 | Listening port `3003` (3001 = gateway, 3002 reserved, 3003 = observer) | Avoids collision with existing services. |
| 1.1.5 | Single `/health` endpoint | Per Q11 (d) — split liveness/readiness deferred. |

### 1.2 LLM SDK and model selection

| # | Decision | Rationale |
|---|---|---|
| 1.2.1 | `Microsoft.Extensions.AI` provider-agnostic abstraction | One SDK; provider swap is configuration, not code. |
| 1.2.2 | Default provider Anthropic, default model `claude-sonnet-4-6` | Best price/tool-use balance in 2026. |
| 1.2.3 | Provider configurable via env vars (`INFRA_GATE_OBSERVER_LLM_PROVIDER`, `INFRA_GATE_OBSERVER_LLM_MODEL`) | Supports Anthropic, OpenAI/GPT (incl. code-focused models), Google Gemini, Azure OpenAI, and Ollama local. |
| 1.2.4 | LLM API key via env var `INFRA_GATE_OBSERVER_LLM_API_KEY` | Never logged. Production secret strategy deferred. |

### 1.3 MCP transport and observer architecture

| # | Decision | Rationale |
|---|---|---|
| 1.3.1 | MCP client uses `ModelContextProtocol` C# SDK over HTTP | The same SDK the gateway is already built on. |
| 1.3.2 | Observer is a **peer MCP client** of the gateway, not embedded in the gateway | Conflating roles would muddy the **Approval Authority** boundary defined in `CONTEXT.md`. |
| 1.3.3 | Hybrid analysis architecture (not pure tool-loop, not pure pipeline) | Code fetches one baseline snapshot per cycle; LLM analyses and may call capped follow-up read-only tools. Bounded cost + agentic deep-dive. |
| 1.3.4 | Tool whitelist enforced **client-side** in addition to OAuth scope | Defense-in-depth. |

### 1.4 Authentication and authorisation

| # | Decision | Rationale |
|---|---|---|
| 1.4.1 | OAuth client_credentials flow to Keycloak | Machine identity; matches existing patterns. |
| 1.4.2 | New Keycloak client `infra-gate-observer` | First entry on the service-client list. |
| 1.4.3 | New OAuth scope `mcp:tools.readonly`, sibling of existing `mcp:tools` | Gateway maps each tool to a required scope; mutation tools require `mcp:tools`. Defense-in-depth — even a compromised Observer binary cannot mutate. |
| 1.4.4 | Audit identity `service:observer`, emitted via extension of `GatewayAuditIdentityResolver` | Distinguishes machine calls from human calls in `GuardrailAuditEvent` records. |
| 1.4.5 | Extract `InfraGate.ClientCredentials` shared library now | Holds token acquisition + bearer-injection HTTP handler. Both `InfraGate.DownstreamAuth` and `InfraGate.Observer` consume it. Cleanliness over YAGNI — avoids future tangling. |
| 1.4.6 | Client secret via env var `INFRA_GATE_OBSERVER_CLIENT_SECRET` for local dev | Production approach (K8s `Secret`, SPIFFE, Workload Identity) left open. |
| 1.4.7 | Keycloak issuer discipline: Docker uses `keycloak:8080` internal DNS; host uses `127.0.0.1:3010`; token `iss` is always `keycloak:8080` regardless | Matches existing `Docker Keycloak issuer mismatch pattern` memory note. Run-profile rendering injects correct values per profile. |

### 1.5 Domain language and CONTEXT.md additions

Nine new terms added to `CONTEXT.md` in a new `### Anomaly Observation` subsection under Language, with mirrored subsection in Relationships and two Flagged Ambiguity entries:

| Term | Why coined / why disambiguated |
|---|---|
| **Anomaly Observer** | "Observer" alone could read as a K8s controller or human reviewer. Full name pins the role. |
| **Anomaly** | New domain term — not application error, performance trend, or security incident. |
| **Observation Cycle** | New term — explicitly not a "reconciliation loop." |
| **Snapshot** | New term — deterministic input to a single cycle's analysis step. |
| **Detection Rule** | "Rule" not "Policy" — `Policy` is overloaded in this repo (`Approval Policy`, `Domain Policy Check`, `Freshness Policy`). |
| **Anomaly Report** | "Report" not "Finding" — `Finding` already informally used for policy findings in **Plan Evidence**. |
| **Severity** | Three-level scale `High` / `Medium` / `Low`. |
| **Observer Service Identity** | New sibling of `Gateway Service Identity`; reusing the existing term would have weakened it. |
| **Anomaly Handoff** | Contract-shape-only in v1; transport defined when executor is in scope. |

Status enum on `Anomaly Report` is `Active | Resolved` for v1. `Persistent` is a strong v2 candidate when an escalation signal is needed.

### 1.6 Observation cycle temporal behaviour

| # | Decision | Default | Rationale |
|---|---|---|---|
| 1.6.1 | Cycle cadence | `60s` | Each cycle is ≥ 1 LLM call; this balances responsiveness vs. token spend. |
| 1.6.2 | Cadence bounds | min `10s`, max `3600s` | Below 10s hammers gateway + LLM; above 1h is not observation. |
| 1.6.3 | Per-cycle wall-clock cap | `20s` | Below `/observe-now` HTTP timeout (30s) so HTTP layer never bites first. Below cadence (60s) so cycles never overlap. See agentmemory `wall-clock-cap` entry for the full reasoning. |
| 1.6.4 | Max LLM tool-call iterations per cycle | `8` | Bounds agentic loops independent of clock time. Catches "fast but infinite" loops. |
| 1.6.5 | Behaviour on cap fire | Cycle marked truncated; structured log + telemetry counter; **no AnomalyReports emitted from a truncated cycle**; dedupe state unchanged; next cycle runs normally | Partial findings would give false confidence ("only one issue"). |
| 1.6.6 | Dedupe key | `(AnomalyKind, ResourceKind, Namespace, Name)` | Minimum tuple identifying a recurring symptom. |
| 1.6.7 | Dedupe window | `5 cycles` (≈ 5 min at default cadence) | Long enough to suppress noise; short enough to re-emit if missed. |
| 1.6.8 | State storage | **In-memory only (v1)** — `ConcurrentDictionary<DedupKey, ActiveAnomalyState>` | No DB dep; restart = clean slate. Persistence is a v2 candidate. |
| 1.6.9 | Cold start | First cycle emits every currently-anomalous resource as a fresh report | Explicit and predictable. |
| 1.6.10 | Restart behaviour | Same as cold start — all active anomalies re-emit on first post-restart cycle | Documented limitation; v2 = persisted dedupe. |
| 1.6.11 | Resolution emission | When an active anomaly is absent for `2 consecutive cycles`, emit one `AnomalyReport` with `Status = Resolved`, `Severity = Low`, then forget the dedupe entry | Executor needs clean Active→Resolved lifecycle. |
| 1.6.12 | `/observe-now` semantics | Synchronous; blocks until cycle completes (HTTP timeout 30s); returns `AnomalyReport[]`; **does not reset** the next scheduled tick | Most useful for human debugging. |

### 1.7 Anomaly classification

| # | Decision | Rationale |
|---|---|---|
| 1.7.1 | `AnomalyKind` coarse enum: `PodUnhealthy`, `DeploymentUnavailable`, `ServiceNoEndpoints`, `WarningEvent` | Maps to the gateway's read-only tools. Stable enum; sub-classification lives in `Annotations`. |
| 1.7.2 | Sub-classification (e.g. `OOMKilled`, `CrashLoopBackOff`) goes in `Annotations["PodCondition"]` | Avoids breaking the enum every time a new failure mode is recognised. |
| 1.7.3 | Severity assignment authority is **hybrid**: LLM proposes, code-applied rules win on conflict, disagreement counter incremented | Deterministic safety net + LLM nuance + telemetry signal of LLM value. |
| 1.7.4 | Severity rules — `High`: Service has 0 ready endpoints; Deployment `availableReplicas == 0` while `spec.replicas > 0`; all pods of a workload in `CrashLoopBackOff`/`ImagePullBackOff`. `Medium`: partial Deployment unavailability; single pod in `CrashLoopBackOff`/`ImagePullBackOff`/`OOMKilled` while siblings healthy; sustained `BackOff` events. `Low`: one-off Warning events without ongoing impact; single restart since last cycle; Pending pod within scheduling grace. | Maps directly to objective signals visible via the read-only tools. |
| 1.7.5 | System prompt lives as an embedded markdown resource at `src/InfraGate.Observer/Prompts/ObserverSystemPrompt.md` | Non-devs can review via GitHub diff. Loaded at startup via `Assembly.GetManifestResourceStream`. |
| 1.7.6 | Aggregation: **per-resource reports** (one Deployment + one Service + N Pods + M events) | Simpler classification; stable AnomalyId per resource; executor sees both workload and pod detail. Workload-aggregation is a v2 candidate. |

### 1.8 Detection scope (v1)

Detection is limited to objective Kubernetes health signals visible through the gateway's read-only tools. The four buckets:

1. **Pod unhealthy** — `CrashLoopBackOff`, `ImagePullBackOff`/`ErrImagePull`, container `lastState.terminated.reason = OOMKilled`, `Pending` stuck > threshold, restart count above threshold.
2. **Deployment unavailable** — `status.availableReplicas < spec.replicas`, rollout stuck (old ReplicaSet still has pods past grace), generation mismatch.
3. **Service no endpoints** — Service exists but EndpointSlices have zero `addresses`, or selector matches no Pods.
4. **Warning event** — `events.k8s.io/v1` Warning entries within the last N minutes (`FailedScheduling`, `FailedMount`, `BackOff`, `Unhealthy`, ...).

Explicitly **out of scope for v1**: resource-usage trends (no metrics tool), log-content semantic analysis (expensive, open-ended), GitOps drift (no Git baseline), security/audit anomalies (not exposed).

### 1.9 Handoff contract

| # | Decision | Rationale |
|---|---|---|
| 1.9.1 | New shared project `src/InfraGate.Observer.Contracts/` for all handoff types | Mirrors `InfraGate.Approvals` (contracts shared between gateway and adapter). Executor later references contracts, never references `InfraGate.Observer`. |
| 1.9.2 | Batch per cycle, not per anomaly | `AnomalyHandoffBatch` carries cycle's full set; clean `CycleId` delimiter. |
| 1.9.3 | Seam interface `IAnomalyHandoffSink.PublishAsync(AnomalyHandoffBatch, CancellationToken)` | Single seam; executor plugs in later as another sink registration. |
| 1.9.4 | Default sinks: `LoggingAnomalyHandoffSink` (always on), `JsonFileAnomalyHandoffSink` (opt-in via `Observer:FileSink:Root`), `CompositeAnomalyHandoffSink` (fan-out with failure isolation) | Logging gives always-on visibility; JSON file gives durability for "I missed a cycle"; composite isolates sink failures. |
| 1.9.5 | Fire-and-forget reliability in v1 | Sink throw → log + move on. `AnomalyId` stable across cycles makes re-emission idempotent. v2 candidate: retry/queue. |
| 1.9.6 | `RemediationHint?` included on `AnomalyReport` as optional, non-authoritative LLM hint | Executor gets a starting point. Preserves "AI proposes, human approves" — hint is not a `MutationIntent`. |
| 1.9.7 | `AnomalyId` stable across cycles for the same underlying anomaly (stable hash of `Kind` + `ResourceRef`) | Resolution emission can correlate Active→Resolved; executor can dedupe across batches. |

### 1.10 Observability

| # | Decision | Rationale |
|---|---|---|
| 1.10.1 | Reuse `InfraGate.Observability` Serilog configuration (console + file sinks) | Aligns with `McpGateway` and `McpServer` operational story. |
| 1.10.2 | Per-cycle `CycleId` enrichment via `LogContext.PushProperty` | Greppable end-to-end. |
| 1.10.3 | Structured event taxonomy (see §6.1) emitted via `[LoggerMessage]` source-generated methods | Per `code-standards` for high-frequency paths. |
| 1.10.4 | Metrics via built-in `System.Diagnostics.Metrics` (`Meter("InfraGate.Observer", "1.0")`) | No upfront dependency; visible to `dotnet-counters`; OpenTelemetry can subscribe later without code change. |
| 1.10.5 | No OpenTelemetry SDK and no distributed tracing in v1 | Most value (per-stage latency) is already in the cycle duration histogram + tool-call counter. |
| 1.10.6 | **Hard separation from `Audit Spine`** — Observer never writes through `IApprovalAuditPublisher` or `ApprovalAuditEvent` | Keeps approval-lifecycle audit semantics clean. Enforced architecturally (no project reference). |

### 1.11 Tests

| # | Decision | Rationale |
|---|---|---|
| 1.11.1 | Three test layers: `tests/InfraGate.Observer.Tests/` (unit, always on), `tests/InfraGate.Observer.IntegrationTests/` (in-process Gateway TestHost + stub MCP fixtures + mocked LLM, always on), `tests/InfraGate.Observer.E2E.Tests/` (opt-in via `INFRA_GATE_RUN_OBSERVER_E2E=1`; Keycloak Testcontainer + Gateway + K8s cluster + stub-or-real LLM) | Matches existing repo pattern (Safety.E2E.Tests). |
| 1.11.2 | LLM stubbed by default via `FixtureChatClient : IChatClient`; opt-in real LLM via `INFRA_GATE_OBSERVER_REAL_LLM=1` | CI fast, free, deterministic. Real-LLM run catches prompt regressions. |
| 1.11.3 | Pass criteria are **structural** — assert on `Kind`, `Severity`, `ResourceRef`, `Status`, `AnomalyId` stability, metric increments, audit-log zero-mutation invariant. No assertions on `Summary` prose or `RemediationHint` content. | Avoids LLM-induced flake. |
| 1.11.4 | Demo scenario reuses `examples/failing-deployment/` | Canonical image-typo failure already in repo. |

### 1.12 Deployment

| # | Decision | Rationale |
|---|---|---|
| 1.12.1 | Dockerfile at `src/InfraGate.Observer/Dockerfile` using `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` runtime and `mcr.microsoft.com/dotnet/sdk:10.0-alpine` build | Matches gateway/server pattern. |
| 1.12.2 | Extend single existing `deploy/local-oauth/compose.yaml` (one-command `docker compose up`) | No new compose files. |
| 1.12.3 | New `ObserverProfile.cs` as a component-profile record in `src/InfraGate.RunProfiles/`, sibling to `GatewayProfile`/`KubernetesAdapterProfile` | Folded into existing run profiles (`local-docker`, `local-host`); no new top-level profiles. |
| 1.12.4 | Bind-mount `.mcp-observer/findings` on the host for the JSON sink | Parallels existing `.mcp-approvals` bind-mount. Host-visible for inspection. |
| 1.12.5 | Env-var schema prefix `INFRA_GATE_OBSERVER_*`, wired through existing `InfraGateEnvVarMappings` | Consistent with `InfraGate.RuntimeSafety` conventions. |

### 1.13 Optional VS Code companion

| # | Decision | Rationale |
|---|---|---|
| 1.13.1 | An additional VS Code custom agent file `agents/anomaly-observer.agent.md` calls `/observe-now` or invokes the same read-only MCP tools directly | Lowest-priority companion feature. The deployed Observer is the product. |

---

## 2. Glossary Delta (already applied to `CONTEXT.md`)

The following changes were committed to `CONTEXT.md` during the grilling session:

- **New section** `### Anomaly Observation` under Language with 9 term definitions.
- **New subsection** `### Anomaly Observation` under Relationships with 12 relationship bullets, including hard-line statements that the Observer/Observer Service Identity does not bypass the **Pre-Execution Gate**, does not produce **Plan Envelopes**/**Approval Grants**/**Challenge Outcomes**, and does not authorise **Approval-Bound Execution**.
- **Two new entries** in Flagged Ambiguities: the "observer" disambiguation, and the "finding" vs. "Anomaly Report" disambiguation.

No further glossary work is required during implementation unless new concepts surface during code review.

---

## 3. Out of Scope (v1)

Explicit non-goals so future readers don't re-litigate during implementation:

- Executor implementation — only the handoff contract shape exists in v1.
- Persistent dedupe state (DB, Redis, file) — in-memory only.
- `Persistent` / `Flapping` / `Acknowledged` / `Suppressed` statuses — only `Active` and `Resolved`.
- OpenTelemetry SDK, distributed tracing, OTLP exporters.
- Per-tool timeouts (cap is per-cycle only).
- Adaptive cap based on rolling p95.
- Workload-aggregated reporting (per-resource only in v1).
- Resource-usage anomaly detection (no metrics tool exposed).
- Log-content semantic anomaly detection (open-ended; v2 candidate behind a feature flag).
- GitOps drift detection (no Git baseline).
- Production secret management (K8s `Secret`, SPIFFE, Workload Identity) — env-var only for v1.
- Production-grade health probe split (`/healthz` + `/readyz`) — single `/health` v1.
- Gateway → McpServer HTTP path — not on the roadmap; `InfraGate.DownstreamAuth` continues to be infrastructure for a deferred feature.

---

## 4. Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| LLM cost runaway from cycle volume | Medium | Wall-clock cap (`20s`), tool-iteration cap (`8`), `infragate.observer.llm.tokens` metric for visibility, configurable cadence. |
| LLM hallucinates an anomaly | Medium | Hybrid Severity: rules-derived classification is the source of truth; LLM disagreement is logged and counted but does not change emitted `Severity`. |
| Restart re-emission storm on a noisy cluster | Low | Documented as a v1 limitation; v2 persistence is earmarked. Dedupe window suppresses repeats within minutes; cold-start cost is bounded by the four-bucket detection scope. |
| OAuth token expiry race during a long cycle | Low | Token cache refreshes near-expiry (~30s before); HTTP handler retries once on 401. |
| Tool whitelist drift if gateway adds tools | Low | Client-side whitelist explicit; integration test asserts Observer never invokes any tool outside the whitelist; gateway `mcp:tools.readonly` scope is the second line. |
| Keycloak issuer mismatch between Docker and host runs | Low | Run-profile rendering injects correct values per profile; documented in README; matches existing `project_keycloak_issuer_mismatch` memory pattern. |
| `InfraGate.ClientCredentials` extraction breaks `InfraGate.DownstreamAuth` consumers | Low | Migrate `DownstreamAuth` in the same task as extraction; existing `DownstreamAuth.Tests` suite must remain green; integration test exercises both consumers. |
| Audit Spine accidental cross-contamination | Medium (if it happened) | Architecturally enforced: Observer project does **not** reference `InfraGate.Approvals` for audit publishing; no `IApprovalAuditPublisher` registration; lint check via project-reference assertion in unit tests. |
| Per-resource reports overwhelming a large cluster | Low (v1 scope is one namespace) | Namespace allowlist + dedupe; v2 candidate: configurable workload-aggregation mode. |
| Sink failure cascades into cycle abort | Low | `CompositeAnomalyHandoffSink` isolates per-sink failures (try/catch per sink, log + counter, continue). |
| Stubbed-LLM tests diverge from real-LLM behaviour over time | Medium | `INFRA_GATE_OBSERVER_REAL_LLM=1` opt-in path runs the same suite against the configured provider; encouraged in nightly CI when available. |

---

## 5. Task List

Tasks are sized per the planning skill (S/M/L). Phases are vertical-sliced from Phase 2 onward; Phase 1 is unavoidably foundational. Each task is small enough for a focused session.

### Phase 1: Foundation (must complete before any Observer code)

#### Task 1.1: Extract `InfraGate.ClientCredentials` shared library

**Description:** Create a new project `src/InfraGate.ClientCredentials/` containing the OAuth client_credentials token acquisition, in-memory token cache with refresh-near-expiry, and an `HttpMessageHandler` that injects the bearer header. Migrate `InfraGate.DownstreamAuth` to reference it. The library exposes one primary seam (`IClientCredentialsTokenProvider`) and one HTTP handler (`ClientCredentialsBearerHandler`). This is a **deep module** in the `improve-codebase-architecture` sense: small interface (acquire token, inject header), substantial behaviour behind it (caching, refresh, 401 retry, thread safety).

**Acceptance criteria:**

- [ ] New project compiles with the inherited analyser strictness; no `NoWarn` introduced.
- [ ] `IClientCredentialsTokenProvider.GetTokenAsync(CancellationToken)` returns a cached valid token or acquires a fresh one.
- [ ] Token refresh fires at `expires_in - 30s`.
- [ ] `ClientCredentialsBearerHandler` retries exactly once on 401 with a forced refresh.
- [ ] `InfraGate.DownstreamAuth` references and consumes the shared library; its public surface is unchanged.
- [ ] No new top-level types in `InfraGate.DownstreamAuth` that duplicate the extracted ones.

**Verification:**

- [ ] `dotnet build src/InfraGate.ClientCredentials/InfraGate.ClientCredentials.csproj` succeeds.
- [ ] `dotnet test tests/InfraGate.DownstreamAuth.Tests/InfraGate.DownstreamAuth.Tests.csproj` passes unchanged.
- [ ] `dotnet test tests/InfraGate.ClientCredentials.Tests/InfraGate.ClientCredentials.Tests.csproj` (new — see Task 9.1).

**Dependencies:** None.

**Files likely touched:**

- `src/InfraGate.ClientCredentials/InfraGate.ClientCredentials.csproj` (new)
- `src/InfraGate.ClientCredentials/IClientCredentialsTokenProvider.cs` (new)
- `src/InfraGate.ClientCredentials/ClientCredentialsTokenProvider.cs` (new)
- `src/InfraGate.ClientCredentials/ClientCredentialsTokenOptions.cs` (new record)
- `src/InfraGate.ClientCredentials/ClientCredentialsBearerHandler.cs` (new)
- `src/InfraGate.ClientCredentials/ClientCredentialsServiceCollectionExtensions.cs` (new)
- `src/InfraGate.ClientCredentials/ClientCredentialsConventions.cs` (new)
- `src/InfraGate.DownstreamAuth/*.cs` (migrated to consume shared library)
- `InfraGate.slnx` (register new project)

**Estimated scope:** Medium (5 files).

---

#### Task 1.2: Create `InfraGate.Observer.Contracts` project

**Description:** Pure-types project holding every type the (future) executor will need to reference. No behaviour, no MCP/LLM deps. Contains `AnomalyReport`, `AnomalyHandoffBatch`, `AnomalyKind`, `AnomalyStatus`, `Severity`, `ResourceRef`, `EvidenceItem`, `RemediationHint`, and the `IAnomalyHandoffSink` seam. All types are `sealed record` per code standards.

**Acceptance criteria:**

- [ ] Project references zero internal projects.
- [ ] No package references beyond what the analyser requires.
- [ ] `AnomalyReport` carries exactly the fields enumerated in CONTEXT.md (`AnomalyId`, `CycleId`, `DetectedAt`, `Kind`, `Target`, `Severity`, `Status`, `Summary`, `Evidence`, `Suggested`, `Annotations`).
- [ ] All collections on public surface are `IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>`.
- [ ] All records are `sealed`.
- [ ] `AnomalyKind` enum values match the four-bucket detection scope exactly.
- [ ] `AnomalyStatus` is `Active | Resolved` (and nothing else in v1).
- [ ] `Severity` is `High | Medium | Low`.
- [ ] `IAnomalyHandoffSink.PublishAsync` signature matches the locked contract.

**Verification:**

- [ ] `dotnet build src/InfraGate.Observer.Contracts/InfraGate.Observer.Contracts.csproj` succeeds.
- [ ] Public-API snapshot test (xUnit + `PublicApiGenerator` or equivalent) — committed baseline.

**Dependencies:** None.

**Files likely touched:**

- `src/InfraGate.Observer.Contracts/InfraGate.Observer.Contracts.csproj` (new)
- `src/InfraGate.Observer.Contracts/AnomalyReport.cs` (new)
- `src/InfraGate.Observer.Contracts/AnomalyHandoffBatch.cs` (new)
- `src/InfraGate.Observer.Contracts/AnomalyKind.cs` (new)
- `src/InfraGate.Observer.Contracts/AnomalyStatus.cs` (new)
- `src/InfraGate.Observer.Contracts/Severity.cs` (new)
- `src/InfraGate.Observer.Contracts/ResourceRef.cs` (new)
- `src/InfraGate.Observer.Contracts/EvidenceItem.cs` (new)
- `src/InfraGate.Observer.Contracts/RemediationHint.cs` (new)
- `src/InfraGate.Observer.Contracts/IAnomalyHandoffSink.cs` (new)
- `src/InfraGate.Observer.Contracts/AnomalyObserverConventions.cs` (new — shared constants)
- `InfraGate.slnx` (register new project)

**Estimated scope:** Medium (5–8 files but all trivial DTOs).

---

#### Task 1.3: Add `mcp:tools.readonly` scope to the gateway

**Description:** Introduce a tool-to-scope mapping in `InfraGate.McpGateway`. Mutation tools require `mcp:tools` (status quo for humans); read-only tools accept either `mcp:tools` or `mcp:tools.readonly`. The scope is checked during MCP tool dispatch in `GatewayToolDispatcher`. This is a deepening change: the existing single-scope check becomes a small lookup table keyed by tool name.

**Acceptance criteria:**

- [ ] New `mcp:tools.readonly` scope known to `GatewayAuthConventions`.
- [ ] Tool-to-required-scope mapping is a single named constant table (no magic strings scattered in dispatch code).
- [ ] A token carrying only `mcp:tools.readonly` is accepted on all 8 read-only tools and rejected with structured error on any mutation tool.
- [ ] A token carrying `mcp:tools` continues to work on all 14 tools (no regression).
- [ ] Tool name list mapping is derived from `McpGatewayConventions.ToolNames` (no duplication).
- [ ] Rejection produces an audit event with `Outcome=Denied` and the offending tool name.

**Verification:**

- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` passes including new scope tests.
- [ ] Manual: token with `mcp:tools.readonly` scope cannot invoke `request_scale_deployment`.

**Dependencies:** None.

**Files likely touched:**

- `src/InfraGate.McpGateway.Auth/GatewayAuthConventions.cs`
- `src/InfraGate.McpGateway/GatewayToolDispatcher.cs`
- `src/InfraGate.McpGateway/McpGatewayConventions.cs` (scope table)
- `tests/InfraGate.McpGateway.Tests/UnitTests/...ScopeTests.cs` (new)

**Estimated scope:** Medium (4 files).

---

#### Task 1.4: Extend `GatewayAuditIdentityResolver` for `service:*` identities

**Description:** Recognise the `azp` (authorised party / client ID) claim. When it matches a registered service-client list (initially `["infra-gate-observer"]`), emit a `GatewayAuditIdentity` with kind `Service` and value `service:observer`. Human subjects are unchanged. This keeps the audit log unambiguous about whether a call came from a human or a service.

**Acceptance criteria:**

- [ ] Registered service-client list is a single named convention constant.
- [ ] `GatewayAuditIdentity` distinguishes `Service` from `Human` identity kind.
- [ ] An `infra-gate-observer` token surfaces as `service:observer` in every `GuardrailAuditEvent`.
- [ ] No regression for human tokens — existing tests continue to pass with their existing identities.

**Verification:**

- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`.
- [ ] Audit log inspected end-to-end in Task 8.4 demo verification.

**Dependencies:** None.

**Files likely touched:**

- `src/InfraGate.McpGateway.Auth/GatewayAuditIdentity.cs`
- `src/InfraGate.McpGateway.Auth/GatewayAuditIdentityResolver.cs`
- `src/InfraGate.McpGateway.Auth/GatewayAuthConventions.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/...IdentityResolverTests.cs`

**Estimated scope:** Small (3–4 files).

---

#### Checkpoint: Foundation Complete

- [ ] All Phase 1 tasks merged.
- [ ] `dotnet build InfraGate.slnx` clean.
- [ ] `dotnet test InfraGate.slnx` green (gateway + downstream-auth + new client-credentials + contracts).
- [ ] No new `NoWarn`, disabled nullable contexts, or analyser suppressions.
- [ ] CONTEXT.md unchanged since grilling — confirm the Anomaly Observation section is still intact.

---

### Phase 2: Observer skeleton — connect, authenticate, log

#### Task 2.1: Bootstrap `InfraGate.Observer` project

**Description:** Create `src/InfraGate.Observer/InfraGate.Observer.csproj` as an ASP.NET `WebApplication` listening on port 3003. Wire `IHostedService` skeleton (`ObservationCycleLoop`) that ticks on a fixed cadence and currently does nothing. Bind `ObserverOptions` from configuration via `InfraGateEnvVarMappings`. Register `InfraGate.Observability` for Serilog. Reference `InfraGate.Observer.Contracts`, `InfraGate.ClientCredentials`, `InfraGate.Observability`, `InfraGate.RuntimeSafety`.

**Acceptance criteria:**

- [ ] Project builds clean under inherited strict analysers.
- [ ] `Program.cs` uses `WebApplication.CreateBuilder` (file-scoped namespace).
- [ ] `ObserverOptions` record holds every key from §1.12.5 with `IOptionsMonitor<T>` binding.
- [ ] Cadence bounds (10s ≤ cadence ≤ 3600s) validated at startup; fail-fast on misconfiguration.
- [ ] `ObservationCycleLoop` runs every `CycleIntervalSeconds` and logs `observer.cycle.started` + `observer.cycle.completed` (no other work yet).
- [ ] No reference to `InfraGate.Approvals` — enforced architecturally.

**Verification:**

- [ ] `dotnet run --project src/InfraGate.Observer/InfraGate.Observer.csproj` starts; `/health` returns 200; logs show ticking cycles.

**Dependencies:** Task 1.2.

**Files likely touched:**

- `src/InfraGate.Observer/InfraGate.Observer.csproj`
- `src/InfraGate.Observer/Program.cs`
- `src/InfraGate.Observer/ObserverOptions.cs`
- `src/InfraGate.Observer/ObserverConventions.cs`
- `src/InfraGate.Observer/Cycle/ObservationCycleLoop.cs`
- `src/InfraGate.Observer/GlobalUsings.cs`
- `InfraGate.slnx`

**Estimated scope:** Medium (6–7 files).

---

#### Task 2.2: Wire MCP HTTP client with OAuth bearer

**Description:** Register the `ModelContextProtocol` HTTP client pointed at `INFRA_GATE_OBSERVER_GATEWAY_BASE_URL`. Inject the `ClientCredentialsBearerHandler` from `InfraGate.ClientCredentials`. Confirm end-to-end auth by calling `get_allowed_namespaces` during startup and logging the result.

**Acceptance criteria:**

- [ ] MCP client established at startup; failure logs structured error with diagnostics (token endpoint, scope, audience).
- [ ] Tool whitelist constant lists the 8 read-only tools from §1.8 (referenced from existing `McpGatewayConventions.ToolNames`).
- [ ] Client-side whitelist enforcer wraps every tool call; attempts to call any non-whitelisted tool throw `InvalidOperationException` before HTTP.
- [ ] Successful startup log: `observer.startup.connected` with the allowed-namespaces response.

**Verification:**

- [ ] With local gateway + Keycloak up, Observer starts and logs `observer.startup.connected` with namespace list.

**Dependencies:** Tasks 1.1, 1.3, 1.4, 2.1.

**Files likely touched:**

- `src/InfraGate.Observer/Mcp/ObserverMcpClient.cs`
- `src/InfraGate.Observer/Mcp/ToolWhitelist.cs`
- `src/InfraGate.Observer/Program.cs` (DI wiring)
- `src/InfraGate.Observer/ObserverConventions.cs` (whitelist constant)

**Estimated scope:** Medium (4 files).

---

#### Task 2.3: Snapshot fetch step

**Description:** Implement `ISnapshotFetcher.FetchAsync(string namespaceName, CancellationToken)` that calls `get_k8s_status` and `get_k8s_events` per allowed namespace and assembles a `SnapshotDocument` record. This is the deterministic baseline for each cycle. The fetcher is a **deep module**: callers pass a namespace, receive an aggregated snapshot; the implementation handles parallelism, error degradation, and serialisation.

**Acceptance criteria:**

- [ ] One snapshot per cycle per allowed namespace.
- [ ] Tool calls executed in parallel within a namespace (status + events).
- [ ] Errors on a single tool degrade gracefully — partial snapshot is still emitted with structured warning, not a cycle failure.
- [ ] `SnapshotDocument` is a serialisable record suitable for passing to the LLM as JSON.

**Verification:**

- [ ] Unit test against stub MCP client returns expected `SnapshotDocument`.
- [ ] Integration test against in-process Gateway returns a snapshot from the failing-deployment example.

**Dependencies:** Task 2.2.

**Files likely touched:**

- `src/InfraGate.Observer/Snapshot/ISnapshotFetcher.cs`
- `src/InfraGate.Observer/Snapshot/SnapshotFetcher.cs`
- `src/InfraGate.Observer/Snapshot/SnapshotDocument.cs`

**Estimated scope:** Medium (3 files).

---

#### Task 2.4: Single `/health` endpoint

**Description:** Map `GET /health` returning 200 when the process is up and the most recent token acquisition succeeded. On startup before first acquisition, return 503 with `{"status": "starting"}`.

**Acceptance criteria:**

- [ ] `/health` → 200 once token cache has a non-expired token.
- [ ] Token acquisition failure surfaces as 503 with structured body.

**Verification:**

- [ ] `curl http://localhost:3003/health` returns 200 once Keycloak + gateway are reachable.

**Dependencies:** Task 2.2.

**Files likely touched:**

- `src/InfraGate.Observer/Endpoints/HealthEndpoint.cs`
- `src/InfraGate.Observer/Program.cs`

**Estimated scope:** Small (2 files).

---

#### Checkpoint: Skeleton Connected

- [ ] `docker compose up` (after Task 8.x is done) or local dev: Observer registers with Keycloak, authenticates to the gateway, fetches a snapshot, logs structured cycle events.
- [ ] No mutation tool calls in gateway audit log from `service:observer`.
- [ ] Health endpoint reflects token state.

---

### Phase 3: Detection — make cycles actually detect

#### Task 3.1: Embed and load the system prompt

**Description:** Author `src/InfraGate.Observer/Prompts/ObserverSystemPrompt.md` containing the role description, the four `AnomalyKind` definitions, the Severity rules table, the tool whitelist, the input/output schema, and the explicit "do not call mutation tools" constraints. Register the file as an `<EmbeddedResource>` in the csproj. Implement `ISystemPromptProvider.Get(string namespaceName, int maxToolIterations)` that loads the resource and substitutes the `{NAMESPACE}` and `{MAX_TOOL_ITERATIONS}` placeholders.

**Acceptance criteria:**

- [ ] Prompt file lives at `src/InfraGate.Observer/Prompts/ObserverSystemPrompt.md` and is reviewable as plain markdown.
- [ ] Embedded resource is loaded once at startup and cached.
- [ ] Substitution covers exactly the two placeholders.
- [ ] Prompt explicitly forbids any tool starting with `request_`, `execute_`, `apply_`, `delete_`, `scale_`, `restart_`, or `set_`.
- [ ] Output schema in the prompt matches the C# `AnomalyReport` record field-for-field.

**Verification:**

- [ ] Unit test: prompt round-trips through placeholder substitution with no remaining `{...}` patterns.

**Dependencies:** Task 1.2.

**Files likely touched:**

- `src/InfraGate.Observer/Prompts/ObserverSystemPrompt.md` (new)
- `src/InfraGate.Observer/InfraGate.Observer.csproj` (EmbeddedResource entry)
- `src/InfraGate.Observer/Prompts/ISystemPromptProvider.cs`
- `src/InfraGate.Observer/Prompts/SystemPromptProvider.cs`

**Estimated scope:** Small (4 files).

---

#### Task 3.2: Wire `Microsoft.Extensions.AI` `IChatClient`

**Description:** Register an `IChatClient` selected by `INFRA_GATE_OBSERVER_LLM_PROVIDER` (initial support: `anthropic` only; `openai`, `google`, `azure`, `ollama` left as switch arms with clear `NotImplementedException` so adding them is small later). The provider implementation is the official `Microsoft.Extensions.AI.*` package for that provider. Configure model from `INFRA_GATE_OBSERVER_LLM_MODEL` and API key from `INFRA_GATE_OBSERVER_LLM_API_KEY`.

**Acceptance criteria:**

- [ ] Provider switch is one small named factory; no magic strings in DI registration.
- [ ] Anthropic provider returns a working `IChatClient` for `claude-sonnet-4-6`.
- [ ] Missing API key fails fast at startup with structured error.

**Verification:**

- [ ] Unit test: factory returns the correct `IChatClient` type per provider value.

**Dependencies:** Task 2.1.

**Files likely touched:**

- `src/InfraGate.Observer/Llm/IChatClientFactory.cs`
- `src/InfraGate.Observer/Llm/ChatClientFactory.cs`
- `src/InfraGate.Observer/Llm/LlmProvider.cs` (enum)
- `src/InfraGate.Observer/Program.cs`

**Estimated scope:** Medium (4 files).

---

#### Task 3.3: Rules-derived `Severity` classifier

**Description:** Implement `ISeverityClassifier.Classify(AnomalyEvidence)` that applies the Severity rules table from §1.7.4 deterministically. This is the source of truth for emitted `Severity`. The classifier takes structured evidence (resource state + condition reasons + event counts) and returns `Severity` plus the matched rule name (for telemetry).

**Acceptance criteria:**

- [ ] Every row of the Severity rules table is a single named branch with a unit test.
- [ ] Classifier is pure (no I/O, no logging) — testable as a function table.
- [ ] Returns `(Severity, string MatchedRule)`.

**Verification:**

- [ ] `[Theory]` tests cover every rule with both positive and negative cases.

**Dependencies:** Task 1.2.

**Files likely touched:**

- `src/InfraGate.Observer/Classification/ISeverityClassifier.cs`
- `src/InfraGate.Observer/Classification/SeverityClassifier.cs`
- `src/InfraGate.Observer/Classification/AnomalyEvidence.cs`

**Estimated scope:** Medium (3 files, but exhaustive tests).

---

#### Task 3.4: Cycle orchestrator (hybrid LLM + rules)

**Description:** Implement `IObservationCycleRunner.RunAsync(CancellationToken)` that orchestrates one cycle: fetch snapshot, call LLM with system prompt + snapshot + bounded tools, parse output, run each LLM-proposed report through the rules-derived `Severity` classifier, log disagreements, and emit final `AnomalyReport[]`. Enforces the wall-clock cap and max tool iteration cap via `CancellationToken`.

**Acceptance criteria:**

- [ ] Wall-clock cap and tool-iteration cap both enforced; truncated cycles emit no reports.
- [ ] LLM-proposed `Severity` is replaced with rules-derived `Severity` on conflict; the disagreement is logged + counted (`infragate.observer.severity.disagreement`).
- [ ] `AnomalyId` is the stable hash of `(Kind, Target.ApiVersion, Target.Kind, Target.Namespace, Target.Name)`.
- [ ] `RemediationHint` is preserved as-is from LLM output (no modification, no validation beyond shape).
- [ ] `CycleId` is a fresh GUID per cycle, propagated via `LogContext`.

**Verification:**

- [ ] Unit tests with `FixtureChatClient` validate the full orchestration including severity-disagreement path.

**Dependencies:** Tasks 2.3, 3.1, 3.2, 3.3.

**Files likely touched:**

- `src/InfraGate.Observer/Cycle/IObservationCycleRunner.cs`
- `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs`
- `src/InfraGate.Observer/Cycle/CycleResult.cs`

**Estimated scope:** Large (orchestration logic + many tests).

---

#### Checkpoint: Detection Works End-to-End (single cycle)

- [ ] Manual: apply `examples/failing-deployment/deployment.yaml`, trigger one cycle via `/observe-now` (after Task 7.1) or wait for tick, observe correct `AnomalyReport[]` with `Severity` derived from rules.
- [ ] `infragate.observer.cycle.count{result=completed}` increments.
- [ ] `infragate.observer.llm.tokens` increments.

---

### Phase 4: State, scheduling, lifecycle

#### Task 4.1: Dedupe state machine

**Description:** Implement `IAnomalyDedupeStore` backed by `ConcurrentDictionary<DedupKey, ActiveAnomalyState>`. Stores `FirstSeenCycle`, `LastSeenCycle`, `AnomalyId`, `LastSeverity`. Provides `MarkSeen`, `IsWithinSuppressionWindow`, and `CollectAbsent(int absentCycleThreshold)` for the Resolved-emission path.

**Acceptance criteria:**

- [ ] Thread-safe via `ConcurrentDictionary`.
- [ ] Dedupe window enforced via cycle count, not wall-clock (per Q5(b)).
- [ ] `CollectAbsent(2)` returns entries that were Active last cycle and absent this cycle for the configured threshold.
- [ ] State cleared after Resolved emission for that key.

**Verification:**

- [ ] Unit tests exercise the state machine across simulated cycle sequences.

**Dependencies:** Task 1.2.

**Files likely touched:**

- `src/InfraGate.Observer/State/IAnomalyDedupeStore.cs`
- `src/InfraGate.Observer/State/AnomalyDedupeStore.cs`
- `src/InfraGate.Observer/State/DedupKey.cs`
- `src/InfraGate.Observer/State/ActiveAnomalyState.cs`

**Estimated scope:** Medium (4 files, many tests).

---

#### Task 4.2: Wire dedupe + resolution emission into the cycle runner

**Description:** After `ObservationCycleRunner` produces raw reports, the dedupe store decides which are new (emit), which are suppressed (skip), and which previously-active anomalies should be emitted as `Status = Resolved` (emit with `Severity = Low`). Truncated cycles do not update the dedupe store.

**Acceptance criteria:**

- [ ] Active anomalies within the suppression window do not appear in the published batch.
- [ ] Absent anomalies for 2 consecutive cycles produce one `Resolved` report and disappear from state.
- [ ] Truncated cycles bypass all dedupe updates.

**Verification:**

- [ ] Unit tests simulate 5-cycle sequences and assert exactly which reports appear in each cycle's batch.

**Dependencies:** Tasks 3.4, 4.1.

**Files likely touched:**

- `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs` (extend)
- `src/InfraGate.Observer/Cycle/CycleResult.cs` (extend)

**Estimated scope:** Small (extending existing files).

---

#### Task 4.3: Activate the scheduled loop

**Description:** Replace the no-op `ObservationCycleLoop` body from Task 2.1 with calls into `IObservationCycleRunner`. Cancellation is per-cycle (`CancellationTokenSource` with `CancelAfter(WallClockCapSeconds)`); shutdown is cooperative.

**Acceptance criteria:**

- [ ] Loop runs continuously at configured cadence.
- [ ] Each cycle creates a fresh per-cycle `CancellationTokenSource` linked to the host shutdown token.
- [ ] Host shutdown cancels in-flight cycle cleanly.

**Verification:**

- [ ] Integration test: 3 cycles complete in sequence with expected metrics.

**Dependencies:** Task 4.2.

**Files likely touched:**

- `src/InfraGate.Observer/Cycle/ObservationCycleLoop.cs`

**Estimated scope:** Small (1 file, careful cancellation wiring).

---

#### Checkpoint: Continuous Operation

- [ ] Observer runs for N minutes without leaks (manual run + `dotnet-counters`).
- [ ] Dedupe suppresses repeated active anomalies.
- [ ] Resolution emission fires when failing deployment is fixed.

---

### Phase 5: Handoff sinks

#### Task 5.1: `LoggingAnomalyHandoffSink`

**Description:** Always-registered sink that emits one structured log line per report via `[LoggerMessage]` source generator. Includes `CycleId`, `AnomalyId`, `Kind`, `Severity`, `Status`, `Target`, `Summary`.

**Acceptance criteria:**

- [ ] Logs each report at `Information` level with the structured properties above.
- [ ] No `string` interpolation in log calls.

**Verification:**

- [ ] Unit test asserts log output via `CapturingLogger` (existing pattern in `tests/InfraGate.McpServer.Tests/`).

**Dependencies:** Task 1.2.

**Files likely touched:**

- `src/InfraGate.Observer/Handoff/LoggingAnomalyHandoffSink.cs`

**Estimated scope:** Small (1 file).

---

#### Task 5.2: `JsonFileAnomalyHandoffSink`

**Description:** Opt-in sink that writes `{cycleId}.json` to the directory specified by `Observer:FileSink:Root`. Atomic write (write to `.tmp` + rename). No file rotation in v1 (operator owns cleanup).

**Acceptance criteria:**

- [ ] Sink is only registered when `Observer:FileSink:Root` is set and non-empty.
- [ ] Files written atomically.
- [ ] JSON serialisation uses `System.Text.Json` with `JsonSerializerDefaults.Web`.
- [ ] Each file contains exactly one `AnomalyHandoffBatch`.

**Verification:**

- [ ] Unit test against a temp directory verifies file is created and parses back to an equivalent batch.

**Dependencies:** Task 1.2.

**Files likely touched:**

- `src/InfraGate.Observer/Handoff/JsonFileAnomalyHandoffSink.cs`
- `src/InfraGate.Observer/Handoff/JsonFileSinkOptions.cs`

**Estimated scope:** Small (2 files).

---

#### Task 5.3: `CompositeAnomalyHandoffSink` with failure isolation

**Description:** Wraps registered sinks; calls each in sequence; any sink throw is caught, logged (`observer.handoff.failed`), and counted; remaining sinks continue.

**Acceptance criteria:**

- [ ] One sink throwing does not prevent others from running.
- [ ] Each sink invocation tagged with `SinkName` for telemetry.

**Verification:**

- [ ] Unit test with a deliberately-throwing fake sink.

**Dependencies:** Tasks 5.1, 5.2.

**Files likely touched:**

- `src/InfraGate.Observer/Handoff/CompositeAnomalyHandoffSink.cs`
- `src/InfraGate.Observer/Program.cs` (composite registration)

**Estimated scope:** Small (2 files).

---

#### Task 5.4: Batch publication from cycle runner

**Description:** After dedupe, the cycle runner constructs an `AnomalyHandoffBatch` and invokes `IAnomalyHandoffSink.PublishAsync`. Empty batches (no reports this cycle) are still published if any sink opts into empty-batch notifications via a future flag (v1: skip empty batches).

**Acceptance criteria:**

- [ ] Non-empty batches published once per cycle.
- [ ] Empty batches not published in v1.

**Verification:**

- [ ] Integration test: failing-deployment cycle produces a non-empty batch reaching all registered sinks.

**Dependencies:** Tasks 4.2, 5.3.

**Files likely touched:**

- `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs` (extend)

**Estimated scope:** Small.

---

#### Checkpoint: Handoff Delivered

- [ ] With `Observer:FileSink:Root=./.mcp-observer/findings`, JSON files appear per cycle.
- [ ] Logs include one structured line per report.
- [ ] A deliberately-broken sink does not crash the cycle.

---

### Phase 6: Observability

#### Task 6.1: Structured event taxonomy via `[LoggerMessage]`

**Description:** Define source-generated logging methods for every event in §1.10. Single static class `ObserverLogEvents` with one partial method per event. Apply to every existing log call site.

**Acceptance criteria:**

- [ ] One `[LoggerMessage]` per event from the table.
- [ ] All call sites use the source-generated methods, not raw `LogInformation` / `LogWarning`.
- [ ] `CycleId` property always present on cycle-scoped events.

**Verification:**

- [ ] `dotnet build` shows zero warnings related to logging.
- [ ] Unit test with `CapturingLogger` asserts every event fires with expected properties on a representative cycle.

**Dependencies:** Task 4.3.

**Files likely touched:**

- `src/InfraGate.Observer/Diagnostics/ObserverLogEvents.cs`
- All Observer source files (replace ad-hoc log calls).

**Estimated scope:** Medium (touches many files but each change is tiny).

---

#### Task 6.2: Metrics meter and counters

**Description:** Define `ObserverMetrics` static class holding `Meter("InfraGate.Observer", "1.0")` plus every counter/histogram from §1.10. Instrument cycle runner, severity classifier (disagreement counter), and handoff sinks.

**Acceptance criteria:**

- [ ] Every metric from the table is created and instrumented exactly once.
- [ ] Tag names use lowercase snake-case (e.g. `result=completed`, not `Result=Completed`).
- [ ] `dotnet-counters monitor -n InfraGate.Observer --counters InfraGate.Observer` shows live values during a manual run.

**Verification:**

- [ ] Manual: launch observer, run 3 cycles against failing-deployment, observe counters via `dotnet-counters`.
- [ ] Unit tests assert each counter increments via `MeterListener` in test code.

**Dependencies:** Task 5.4.

**Files likely touched:**

- `src/InfraGate.Observer/Diagnostics/ObserverMetrics.cs`
- Cycle runner, severity classifier, sinks (instrumentation).

**Estimated scope:** Medium (one new file + many small instrumentation edits).

---

#### Task 6.3: `CycleId` enrichment via `LogContext`

**Description:** Ensure every log line emitted within an Observation Cycle carries the `CycleId` property automatically. Use `LogContext.PushProperty("CycleId", id)` inside the cycle runner's outer `using` scope.

**Acceptance criteria:**

- [ ] Every log line emitted between cycle start and completion includes `CycleId`.
- [ ] Logs from outside a cycle (startup, shutdown) do not carry a stale `CycleId`.

**Verification:**

- [ ] Manual: tail `dotnet run` log output; confirm `CycleId` in cycle-scoped lines and absent from startup lines.

**Dependencies:** Task 6.1.

**Files likely touched:**

- `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs`

**Estimated scope:** Small (one file).

---

#### Checkpoint: Observable Operation

- [ ] `dotnet-counters` shows all expected metrics.
- [ ] Logs are correlated by `CycleId` end-to-end within a cycle.
- [ ] No Observer log line goes through `IApprovalAuditPublisher` (manual code review + project-reference assertion).

---

### Phase 7: On-demand trigger

#### Task 7.1: `/observe-now` endpoint

**Description:** Map `POST /observe-now`. Synchronously invokes `IObservationCycleRunner.RunAsync` with a 30-second HTTP timeout. Returns the resulting `AnomalyReport[]` as JSON. Does **not** reset the scheduled tick. Concurrency: if a scheduled cycle is in flight, `/observe-now` waits up to a small slack window (e.g. 2s) for it to complete before starting its own cycle.

**Acceptance criteria:**

- [ ] Synchronous response; 30s server-side timeout.
- [ ] Concurrent `/observe-now` calls serialise via a single named semaphore (one cycle at a time).
- [ ] Schedule is unaffected by manual triggers.

**Verification:**

- [ ] Manual: `curl -X POST http://localhost:3003/observe-now` against the failing-deployment scenario returns expected reports.

**Dependencies:** Task 4.3.

**Files likely touched:**

- `src/InfraGate.Observer/Endpoints/ObserveNowEndpoint.cs`
- `src/InfraGate.Observer/Program.cs`
- `src/InfraGate.Observer/Cycle/CycleSerialisation.cs` (named semaphore)

**Estimated scope:** Small–Medium (3 files).

---

#### Checkpoint: Manual Triggering Works

- [ ] `curl` against `/observe-now` returns expected JSON.
- [ ] Manual trigger during a scheduled cycle waits (does not double-run).

---

### Phase 8: Deployment

#### Task 8.1: `ObserverProfile` record + run-profiles integration

**Description:** Add `src/InfraGate.RunProfiles/ObserverProfile.cs` mirroring sibling component-profile records. Extend `deploy/run-profiles.yaml` `local-docker` and `local-host` profiles with observer configuration. `EnvFileRenderer` and `AppSettingsRenderer` pick it up via existing convention.

**Acceptance criteria:**

- [ ] `dotnet run --project src/InfraGate.RunProfiles -- generate --profile local-docker` produces an `.env` file containing every `INFRA_GATE_OBSERVER_*` key.
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate` passes for both profiles.
- [ ] Docker profile uses internal DNS values (`keycloak:8080`, `gateway:3001`); host profile uses external values (`127.0.0.1:3010`, `127.0.0.1:3001`).

**Verification:**

- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests/` passes new profile-render tests.

**Dependencies:** Tasks 1.2, 2.1.

**Files likely touched:**

- `src/InfraGate.RunProfiles/ObserverProfile.cs` (new)
- `src/InfraGate.RunProfiles/RunProfileConventions.cs` (extend constants)
- `src/InfraGate.RunProfiles/RunProfileDocument.cs` (extend container)
- `src/InfraGate.RunProfiles/EnvFileRenderer.cs` (extend rendering)
- `src/InfraGate.RunProfiles/AppSettingsRenderer.cs` (extend rendering)
- `deploy/run-profiles.yaml` (add observer block in each profile)
- `tests/InfraGate.RunProfiles.Tests/...` (new tests)

**Estimated scope:** Medium (6–7 files).

---

#### Task 8.2: Observer Dockerfile

**Description:** Multi-stage Dockerfile under `src/InfraGate.Observer/Dockerfile` using the alpine SDK and aspnet images. Sets `ASPNETCORE_URLS=http://+:3003`. Runs as non-root user. Exposes 3003.

**Acceptance criteria:**

- [ ] `docker build -t infragate-observer:dev -f src/InfraGate.Observer/Dockerfile .` succeeds.
- [ ] Image runs as non-root.
- [ ] `docker run --rm infragate-observer:dev` shows graceful failure on missing env vars (not stack trace).

**Verification:**

- [ ] Manual: build + run, observe expected startup-validation failure.

**Dependencies:** Task 2.1.

**Files likely touched:**

- `src/InfraGate.Observer/Dockerfile`
- `src/InfraGate.Observer/.dockerignore`

**Estimated scope:** Small (2 files).

---

#### Task 8.3: Extend `deploy/local-oauth/compose.yaml`

**Description:** Add an `observer` service depending on `keycloak` and `gateway`. Mount `./.mcp-observer/findings` as the JSON file sink root (parallels the existing `.mcp-approvals` bind-mount). Source env from `.env` rendered by Task 8.1.

**Acceptance criteria:**

- [ ] `docker compose -f deploy/local-oauth/compose.yaml up` brings up the observer alongside the gateway and Keycloak.
- [ ] `./.mcp-observer/findings/` directory is created on host with rendered JSON files after a cycle.
- [ ] Compose `depends_on` ensures the observer starts after the gateway is responding.

**Verification:**

- [ ] Manual: `docker compose up`, apply failing-deployment, observe JSON files on host.

**Dependencies:** Tasks 8.1, 8.2.

**Files likely touched:**

- `deploy/local-oauth/compose.yaml`

**Estimated scope:** Small (1 file).

---

#### Task 8.4: Register `infra-gate-observer` Keycloak client

**Description:** Add the new Keycloak client to the realm export consumed by the local Keycloak container (`deploy/local-oauth/` realm import). Grant type: `client_credentials` only. Scope: `mcp:tools.readonly`. Audit identity: `service:observer`.

**Acceptance criteria:**

- [ ] Realm import includes the new client.
- [ ] Client secret is documented as `INFRA_GATE_OBSERVER_CLIENT_SECRET` in `docs/configuration.md`.
- [ ] Manual: `curl` to Keycloak's `/token` with the new client returns a JWT with `azp=infra-gate-observer` and the `mcp:tools.readonly` scope.

**Verification:**

- [ ] End-to-end: Observer container authenticates to gateway, gateway audit log shows `service:observer` identity, no mutation tools called.

**Dependencies:** Task 1.4.

**Files likely touched:**

- `deploy/local-oauth/realm-export.json` (or equivalent)
- `docs/configuration.md`

**Estimated scope:** Small (2 files).

---

#### Checkpoint: One-Command Demo

- [ ] `docker compose -f deploy/local-oauth/compose.yaml up` brings up Keycloak + Gateway + McpServer + Observer.
- [ ] Apply `examples/failing-deployment/deployment.yaml`.
- [ ] Observe correct JSON files on host, correct logs, correct metrics.
- [ ] Apply `examples/failing-deployment/fix.yaml`.
- [ ] Within 2 cycles, `Status=Resolved` reports appear.
- [ ] Audit log: zero mutation calls from `service:observer`.

---

### Phase 9: Tests

#### Task 9.1: `InfraGate.ClientCredentials.Tests` (new)

**Description:** Unit tests for the extracted shared library. Covers token cache, refresh timing, 401 retry, thread safety, configuration validation. Uses `MockHttp` or hand-rolled `HttpMessageHandler` fakes.

**Acceptance criteria:**

- [ ] All public surface covered.
- [ ] Concurrent acquisition test asserts only one token request fires per refresh window.
- [ ] 401 retry test asserts exactly one forced refresh + one retry.

**Verification:**

- [ ] `dotnet test tests/InfraGate.ClientCredentials.Tests/`.

**Dependencies:** Task 1.1.

**Files likely touched:**

- `tests/InfraGate.ClientCredentials.Tests/...` (new project)

**Estimated scope:** Medium.

---

#### Task 9.2: `InfraGate.Observer.Tests` (unit)

**Description:** Unit tests for the Observer's pure-logic surfaces — severity rules, dedupe state machine, system-prompt template rendering, tool whitelist enforcement, sink fan-out, cycle orchestration with `FixtureChatClient`. Mock `IChatClient` and the MCP client.

**Acceptance criteria:**

- [ ] Every Severity rule has at least one positive and one negative test (`[Theory]` + `[InlineData]`).
- [ ] Dedupe simulation across 5 cycles asserts emission/suppression for each cycle exactly.
- [ ] Severity-disagreement path asserts emitted Severity = rules-derived (not LLM-proposed) and counter incremented.
- [ ] Whitelist enforcement test asserts every mutation tool name is rejected before HTTP.

**Verification:**

- [ ] `dotnet test tests/InfraGate.Observer.Tests/`.

**Dependencies:** Tasks 3.4, 4.2, 5.3, 6.2.

**Files likely touched:**

- `tests/InfraGate.Observer.Tests/...` (new project)

**Estimated scope:** Large.

---

#### Task 9.3: `InfraGate.Observer.IntegrationTests` (in-process)

**Description:** Integration tests against an in-process Gateway TestHost with stub MCP server fixtures (no Keycloak container, no K8s cluster), mocked LLM via `FixtureChatClient`. Exercises full cycle wiring: auth flow with `ClientCredentialsBearerHandler`, MCP HTTP transport, snapshot fetch, classification, sink fan-out.

**Acceptance criteria:**

- [ ] Test bootstraps gateway + stub MCP fixtures + Observer DI graph in-process.
- [ ] Failing-deployment fixture produces the expected per-resource AnomalyReports.
- [ ] AnomalyId stability asserted across two consecutive simulated cycles.
- [ ] After "fix" fixture, Status=Resolved reports appear within 2 cycles.

**Verification:**

- [ ] `dotnet test tests/InfraGate.Observer.IntegrationTests/`.

**Dependencies:** Tasks 7.1, 9.2.

**Files likely touched:**

- `tests/InfraGate.Observer.IntegrationTests/...` (new project)

**Estimated scope:** Large.

---

#### Task 9.4: `InfraGate.Observer.E2E.Tests` (opt-in)

**Description:** Opt-in end-to-end tests via `INFRA_GATE_RUN_OBSERVER_E2E=1`. Mirrors `InfraGate.Safety.E2E.Tests` style: real Keycloak (Testcontainer), real Gateway TestHost, developer-provided Kubernetes cluster, stubbed LLM by default with `INFRA_GATE_OBSERVER_REAL_LLM=1` opt-in.

**Acceptance criteria:**

- [ ] Opt-in env-var gating prevents accidental run in CI.
- [ ] Full pass criteria from §1.11.3 implemented as assertions.
- [ ] Real-LLM path is identical except for the `IChatClient` registration.

**Verification:**

- [ ] `INFRA_GATE_RUN_OBSERVER_E2E=1 dotnet test tests/InfraGate.Observer.E2E.Tests/` (developer-provided cluster).

**Dependencies:** Task 9.3 + Phase 8 complete.

**Files likely touched:**

- `tests/InfraGate.Observer.E2E.Tests/...` (new project)

**Estimated scope:** Large.

---

#### Checkpoint: Test Suite Green

- [ ] All non-E2E tests pass in CI.
- [ ] Opt-in E2E passes locally with developer cluster.
- [ ] Code coverage acceptable on Observer + ClientCredentials.

---

### Phase 10: Documentation (per `verify-readme-docs`)

#### Task 10.1: `src/InfraGate.Observer/README.md` (new)

**Description:** Brief README following the established per-project pattern (`McpGateway`, `McpServer`, etc.). Sections: Runtime Flow, Important Contracts, Settings (link to `docs/configuration.md`), Verification.

**Acceptance criteria:**

- [ ] Matches the existing per-project README style.
- [ ] Links to `docs/configuration.md` for env-var details (no duplication).
- [ ] Lists the four AnomalyKind values and the three Severity levels with their rules.

**Verification:**

- [ ] Manual review against the `verify-readme-docs` skill workflow.

**Dependencies:** All implementation tasks merged.

**Files likely touched:**

- `src/InfraGate.Observer/README.md`

**Estimated scope:** Small.

---

#### Task 10.2: Update root `README.md`

**Description:** Add the Observer to the project list under "Runtime projects" in the existing README. One bullet, linking to the new per-project README.

**Acceptance criteria:**

- [ ] One new bullet, matching the existing format.
- [ ] No restructuring of unrelated sections.

**Verification:**

- [ ] `git diff --check`.

**Dependencies:** Task 10.1.

**Files likely touched:**

- `README.md`
- `AGENTS.md` (Solution Map → Runtime projects)

**Estimated scope:** Small.

---

#### Task 10.3: Update `examples/failing-deployment/README.md`

**Description:** Add an "Observer demo" section alongside the existing approval-flow walkthrough. Steps: bring up the stack, apply `deployment.yaml`, hit `/observe-now` (or wait for tick), inspect JSON file output, apply `fix.yaml`, observe Resolved.

**Acceptance criteria:**

- [ ] Does not replace or move the existing approval-flow demo.
- [ ] Includes the exact `curl` commands and expected response shape.
- [ ] Documents the cleanup step (delete the deployment + service).

**Verification:**

- [ ] Manual walkthrough.

**Dependencies:** Task 8.3.

**Files likely touched:**

- `examples/failing-deployment/README.md`

**Estimated scope:** Small.

---

#### Task 10.4: Extend `docs/configuration.md`

**Description:** Add a new section listing every `INFRA_GATE_OBSERVER_*` env var with default, range, and production guidance. Per `verify-readme-docs`, `docs/configuration.md` is the canonical source.

**Acceptance criteria:**

- [ ] Every env var from §1.12.5 documented with name, default, purpose, production guidance.
- [ ] Cross-link from `src/InfraGate.Observer/README.md`.
- [ ] No duplication of variable docs outside this file (other README files link here).

**Verification:**

- [ ] `rg -n 'INFRA_GATE_OBSERVER_' README.md docs src/*/README.md` — every match outside `docs/configuration.md` is a link, not a duplicate definition.

**Dependencies:** Task 8.1.

**Files likely touched:**

- `docs/configuration.md`

**Estimated scope:** Small–Medium.

---

#### Task 10.5: Update `AGENTS.md` skills list (if applicable)

**Description:** No new skill is created in this implementation, so `AGENTS.md` skills section likely needs no change. Confirm by re-reading the file.

**Acceptance criteria:**

- [ ] If no change needed, document the conclusion in the PR description.

**Dependencies:** None.

**Files likely touched:**

- (none expected)

**Estimated scope:** XS.

---

#### Checkpoint: Documentation Verified

- [ ] All claims in every README map to a real code construct.
- [ ] `docs/configuration.md` is the single source of truth for env vars.
- [ ] No aspirational claims slipped in.

---

### Phase 11: VS Code companion (lowest priority — optional)

#### Task 11.1: `agents/anomaly-observer.agent.md`

**Description:** Create a VS Code custom agent file per the `create-custom-agent` skill. Persona: invokes `/observe-now` against a configured local Observer, summarises findings inline, optionally hands the AnomalyReport set to a downstream `executor` agent via handoff.

**Acceptance criteria:**

- [ ] `.agent.md` frontmatter valid per the skill template.
- [ ] Tool whitelist limited to `fetch` (for HTTP call) and `codebase` (for context).
- [ ] Handoff entry to a `executor` agent exists (even though executor agent is not yet created).
- [ ] Body explains the persona's scope and the explicit "I do not call mutation tools" guardrail.

**Verification:**

- [ ] Manual: open VS Code, invoke `@anomaly-observer`, observe successful cycle trigger.

**Dependencies:** Task 8.3 (Observer running locally).

**Files likely touched:**

- `agents/anomaly-observer.agent.md` (new directory + file)

**Estimated scope:** Small.

---

#### Checkpoint: Optional Companion Available

- [ ] VS Code custom agent invokes the deployed Observer successfully.

---

## 6. Cross-Cutting Code Standards Reminders

Pulled from `code-standards` skill for emphasis during implementation:

- **File-scoped namespaces** in every new file.
- **`sealed` by default** on classes; only leave open when subclassing is intentional.
- **`record` / `record struct`** for DTOs (every type in `InfraGate.Observer.Contracts`).
- **Primary constructors** where applicable.
- **`var`** only when the right-hand side makes the type obvious; explicit otherwise; never for primitives.
- **`Async` suffix** on async methods; **`CancellationToken`** on all async I/O.
- **`ConfigureAwait(false)`** on every awaited task in library/tool code.
- **`IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>`** on public surfaces.
- **`[LoggerMessage]`** source generator on every Observer log call (no `string` interpolation in logs).
- **Magic strings**: every MCP tool name, env-var key, scope name, and audit identity prefix goes into a named conventions class (`ObserverConventions`, `AnomalyObserverConventions`, `ClientCredentialsConventions`). No repeated string literals.
- **One meaningful top-level type per file.** No `#region`.
- **`GlobalUsings.cs`** per project.
- **Booleans named as questions** (`IsTruncated`, `HasActiveAnomaly`).
- **Catch specific exceptions**, not `Exception` (except top-level cycle boundary).
- **Test naming**: `Method_State_ExpectedResult`; `[Theory]` + `[InlineData]` over duplicated `[Fact]`.

## 7. Cross-Cutting Architecture Reminders

Pulled from `improve-codebase-architecture` skill:

- **Deepening**: `InfraGate.ClientCredentials` is a deep module — small interface (`GetTokenAsync` + `HttpMessageHandler`), substantial behaviour (caching, refresh, retry). The deletion test confirms its value: deleting would re-scatter token caching + refresh logic across two consumers.
- **Seams**: `IAnomalyHandoffSink`, `IChatClient` (via Microsoft.Extensions.AI), `ISnapshotFetcher`, `ISeverityClassifier`, `IAnomalyDedupeStore` are the v1 seams. Each starts with one or two adapters — once a second adapter exists (e.g. an HTTP sink for the executor), the seam has proven its keep.
- **Locality**: cycle orchestration concentrates in `ObservationCycleRunner`; cap enforcement, dedupe wiring, severity reconciliation, sink fanout all happen in one place rather than scattered.
- **Avoid shallow modules**: do not extract single-use helpers from `ObservationCycleRunner` purely "for testability" — its interface is what tests assert against.

---

## 8. Open Questions (deferred — not blocking v1)

These were surfaced during grilling but explicitly deferred. None block v1 implementation.

- **Production secret management** — K8s `Secret` vs. SPIFFE vs. Workload Identity. Address with a dedicated ADR when a real production deployment is planned.
- **Persistent dedupe state** — file vs. Redis vs. Postgres reuse of `InfraGate.Approvals.Postgres`. Decide when restart-storm is observed in practice.
- **Additional `Anomaly Report` statuses** (`Persistent`, `Flapping`) — add when executor escalation logic actually needs them.
- **OpenTelemetry exporters** — add when there's an OTel collector in the environment.
- **Per-tool timeouts** vs. per-cycle cap only — add only if a single slow tool dominates cycle aborts.
- **Adaptive cap** based on rolling p95 cycle duration — likely never needed; concrete adaptive overrides via config are simpler.
- **Workload-aggregated reporting mode** — add when a large-cluster operator complains about report volume.
- **Executor contract** beyond shape — separate work, separate plan.
- **Authorization-Check** integration on the Observer's own reads — not needed in v1 (gateway scope is the check).

---

## 9. References

- `CONTEXT.md` — canonical glossary including the `### Anomaly Observation` subsections.
- `docs/configuration.md` — canonical env-var reference (extended in Task 10.4).
- `docs/mutation-approval-flow.md` — context for why the Observer sits outside the approval lifecycle.
- `.agents/skills/planning-and-task-breakdown/SKILL.md` — task-sizing and vertical-slicing rules applied to this plan.
- `.agents/skills/code-standards/SKILL.md` — conventions to apply during every task.
- `.agents/skills/improve-codebase-architecture/SKILL.md` — deepening / seam vocabulary.
- `.agents/skills/verify-readme-docs/SKILL.md` — workflow for Phase 10 doc updates.
- Agentmemory entry `infragate-observer / wall-clock-cap` — full reasoning behind the 20s / 8-iteration caps.
- Agentmemory entry `gateway↔server auth` — context for why `InfraGate.DownstreamAuth` remains as infrastructure for a deferred feature.
- `examples/failing-deployment/` — canonical demo scenario.

---

## 10. Suggested Execution Order

1. **Phase 1 (Foundation)** — sequential within phase: Tasks 1.1 → 1.2 → 1.3 → 1.4. All four must complete before Phase 2 starts.
2. **Phase 2 (Skeleton)** — Tasks 2.1 → 2.2 → 2.3 → 2.4 in sequence (each depends on the previous).
3. **Phase 3 (Detection)** — Tasks 3.1 + 3.2 in parallel; then 3.3; then 3.4 (which depends on all prior).
4. **Phase 4 (State)** — Task 4.1, then 4.2 + 4.3 in sequence.
5. **Phase 5 (Handoff)** — Tasks 5.1 + 5.2 in parallel; then 5.3; then 5.4.
6. **Phase 6 (Observability)** — Tasks 6.1 + 6.2 in parallel; then 6.3.
7. **Phase 7 (On-demand)** — Task 7.1.
8. **Phase 8 (Deployment)** — Task 8.1, then 8.2 + 8.4 in parallel, then 8.3.
9. **Phase 9 (Tests)** — Tasks 9.1 + 9.2 in parallel; then 9.3; then 9.4.
10. **Phase 10 (Docs)** — Tasks 10.1–10.5 in parallel after all implementation phases.
11. **Phase 11 (VS Code)** — Optional; runs whenever Phase 8 is done.

Major checkpoints (Foundation, Skeleton, Detection, Continuous Operation, Handoff, Observable, One-Command Demo, Test Suite Green, Docs Verified) are explicit go/no-go gates. Do not skip checkpoints.
