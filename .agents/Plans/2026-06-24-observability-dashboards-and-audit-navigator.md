# Implementation Plan: Approachable Observability — Telemetry Dashboard + Audit Timeline Navigator

## Overview

InfraGate already emits good telemetry (Serilog + OpenTelemetry traces/metrics via `InfraGate.Observability`, ADR-0026) and keeps a strong tamper-evident audit trail (hash-chained Postgres outbox, `approvals`/`observer`/`planner` streams, ADR-0020). The problem is **approachability**: the telemetry has no backend to render it (no collector/dashboard exists; metrics and traces are emitted into the void unless an operator hand-wires `OTEL_EXPORTER_OTLP_ENDPOINT`), and the audit trail can only be read with raw SQL — there is no way for a human to *follow one incident* across streams.

This plan delivers two complementary, human-facing surfaces:

- **A. Turnkey telemetry dashboard** — a standalone .NET Aspire Dashboard container in the dev compose that receives OTLP and renders traces (including agent-framework spans: model, agent, token counts, tool calls), metrics, and structured logs with zero app code. Plus the wiring fixes that make *all* services actually export.
- **B. Audit Timeline navigator** — a read-only, OAuth-gated page in the existing `InfraGate.ApprovalUi` that takes a `plan_id` (or `anomaly_id`) and stitches the full Human-in-the-Loop lifecycle (anomaly → proposal → plan → challenge → grant → pre-execution gates → execution) into one ordered, correlated timeline. This is the differentiated, security-engineering signal a generic dashboard cannot express.

Plus health/readiness endpoints (basic operational hygiene) and the `docs/observability-model.md` the hardening plan calls for.

## Architecture Decisions

- **Aspire Dashboard, not Grafana LGTM** (user decision). One standalone container (`mcr.microsoft.com/dotnet/aspire-dashboard`), zero-config OTLP receiver, renders traces+metrics+logs and renders agent spans natively. Dev/demo backend only — explicitly **not** a production telemetry stack. Production direction (Tempo/Prometheus/Loki or a hosted OTLP endpoint) is documented as future, not built.
- **The audit trail needs a read side.** `IPostgresAuditOutboxCore` is append-only today. We add a separate, read-only `IAuditStreamReader` rather than overloading the writer — keeps the tamper-evident write path untouched and the read concern isolated. The reader does not, and must not, mutate or re-hash rows.
- **Timeline navigator lives in `InfraGate.ApprovalUi`, hosted by the gateway.** ApprovalUi is a Razor component library rendered to HTML by `ApprovalPageRenderer` and served via `GatewayApprovalEndpoints`. The timeline reuses that exact hosting seam, so it inherits the gateway's OAuth pipeline and DB access. No new host process.
- **Read-only and access-controlled.** The timeline is observability, not a control surface — no actions, no mutations. It sits behind an OAuth policy (audit-viewer scope; see Open Questions on whether to reuse the approval policy or add an admin/auditor policy).
- **Correlation is by ID, matching ADR-0020.** The assembler joins streams on shared IDs (`plan_id`, `anomaly_id`); there is no shared hash chain across streams and we do not invent one.
- **No new magic strings; honor `code-standards`.** Stream names, event names, column names already exist as constants (`AuditOutboxConventions`, `*AuditEvents`); the reader/assembler reuse them.

## Dependency Graph

```
Phase A (telemetry backend)            Phase B (audit navigator)
  A1 aspire-dashboard + OTLP env         B1 IAuditStreamReader (Postgres read side)
  A2 extend telemetry to GW + Executor        │
  A3 health/readiness endpoints          B2 Timeline assembler (correlate streams)
        │                                       │
        └── independent of B               B3 Blazor Timeline page (ApprovalUi)
                                                │
                                           B4 Gateway endpoint + auth + nav link

Phase C (docs) depends on A + B being built (documents real behavior)
```

Phases A and B are independent and can proceed in parallel. Within B, order is strict: B1 → B2 → B3 → B4.

## Task List

### Phase A: Make telemetry visible

#### Task A1: Add Aspire Dashboard to dev compose and route OTLP to it

**Description:** Add a `aspire-dashboard` service to `deploy/local-oauth/compose.yaml` (and the dev profile) exposing the dashboard UI and the OTLP gRPC ingress port (4317). Set `OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:4317` on the `observer` and `planner` services (and the gateway/executor once Task A2 lands). Keep it dev-only; do not add to `deploy/compose/production.yaml`.

**Acceptance criteria:**
- [ ] `aspire-dashboard` container starts in the dev compose; UI reachable on a bound localhost port.
- [ ] `OTEL_EXPORTER_OTLP_ENDPOINT` is set for observer and planner via compose env (not committed secrets).
- [ ] After a demo cycle, agent-framework spans (model, agent, token counts) and the service meters appear in the dashboard.

**Verification:**
- [ ] `docker compose -f deploy/local-oauth/compose.yaml config` validates.
- [ ] Manual: run the failing-deployment demo (`docs/demo-failing-deployment.md`), confirm traces + `infragate.observer.*` / `infragate.planner.*` metrics render.
- [ ] `git diff --check`

**Dependencies:** None.

**Files likely touched:** `deploy/local-oauth/compose.yaml`, `deploy/compose/development.yaml`, `deploy/local-oauth/release.env.example` (document the env var).

**Estimated scope:** Small.

#### Task A2: Register telemetry for the gateway and executor

**Description:** `McpGateway` and `Executor` define meters (`infragate.executor.*`, guardrail counters) but never call `AddInfraGateTelemetry`, so no `MeterProvider`/`TracerProvider` is registered and their signals never export. Wire `AddInfraGateObservability` + `AddInfraGateTelemetry` into both hosts with the correct `ServiceName` and `MeterNames`, mirroring the Observer/Planner blocks.

**Acceptance criteria:**
- [ ] `McpGateway` Program registers telemetry with its guardrail meter name(s).
- [ ] `Executor` Program registers telemetry with `ExecutorMetrics.MeterName`.
- [ ] With A1's endpoint set, executor and guardrail metrics appear in the dashboard.

**Verification:**
- [ ] Build: `dotnet build`
- [ ] Existing tests pass: `run-tests` (gateway + executor tiers).
- [ ] Manual: trigger an execution and a blocked guardrail; confirm `infragate.executor.execute.*` and guardrail counters render.

**Dependencies:** None (but pairs with A1 for the visible check).

**Files likely touched:** `src/InfraGate.McpGateway/Program.cs` (or its configuration extension), `src/InfraGate.Executor/Program.cs`.

**Estimated scope:** Small.

#### Task A3: Add health and readiness endpoints

**Description:** No service maps health checks today. Add `AddHealthChecks` + `MapHealthChecks` for `/healthz` (liveness) and `/readyz` (readiness, includes the Postgres data source check where applicable) to the gateway, observer, planner, and executor hosts. Wire compose `healthcheck` blocks so `depends_on … condition: service_healthy` becomes meaningful.

**Acceptance criteria:**
- [ ] Each host exposes `/healthz` and `/readyz`.
- [ ] Readiness for DB-backed services fails when Postgres is unreachable.
- [ ] Compose healthchecks reference the new endpoints.

**Verification:**
- [ ] Manual: `curl` both endpoints per service returns 200 when up.
- [ ] Manual: stop Postgres, confirm `/readyz` flips to unhealthy for DB-backed services.
- [ ] Build + existing tests pass.

**Dependencies:** None.

**Files likely touched:** the four `Program.cs` (or shared host-extension), `deploy/local-oauth/compose.yaml`.

**Estimated scope:** Medium.

### Checkpoint A: Telemetry is visible

- [ ] Build clean, all existing tests green.
- [ ] One demo run produces visible traces (agent spans), metrics (all four services), and logs in the Aspire Dashboard.
- [ ] Health/readiness endpoints respond correctly.
- [ ] Review with human before starting the navigator UI.

### Phase B: Audit Timeline navigator

#### Task B1: Add a read-only audit stream reader

**Description:** Add `IAuditStreamReader` and a Postgres implementation in `InfraGate.AuditOutbox.Postgres` that queries committed rows from a given stream filtered by a correlation column/value (e.g. `plan_id = @id`), returning ordered `AuditOutboxRow` plus `sequence` and `occurred_at`. Strictly read-only; no writes, no re-hashing. Reuse `AuditOutboxConventions` for schema/column names.

**Acceptance criteria:**
- [ ] `IAuditStreamReader.ReadByCorrelationAsync(stream, column, value, ct)` returns rows ordered by sequence.
- [ ] Implementation parameterizes all input (no SQL injection surface) and touches no write path.
- [ ] Returns empty (not error) for an unknown id.

**Verification:**
- [ ] New unit/integration tests pass (`InfraGate.AuditOutbox.Postgres` integration tier, Postgres container).
- [ ] `run-tests` for the affected tier.
- [ ] Project-reference assertion tests still pass (Observer/Planner must not gain an Approvals reference).

**Dependencies:** None.

**Files likely touched:** `src/InfraGate.AuditOutbox.Postgres/IAuditStreamReader.cs`, `…/PostgresAuditStreamReader.cs`, `…/ServiceCollectionExtensions.cs`, tests under `tests/InfraGate.AuditOutbox*`/integration.

**Estimated scope:** Medium.

#### Task B2: Build the cross-stream timeline assembler

**Description:** Add a small read-side service that takes a `plan_id` (and resolves the related `anomaly_id`/`proposal_id`/`cycle_id` from payloads) and assembles an ordered, typed timeline by reading the `approvals`, `observer`, and `planner` streams via `IAuditStreamReader`. Output is a view model: ordered entries with `OccurredAtUtc`, stream, event name, outcome/reason, actor, and the salient payload fields (digests, gate outcomes, namespace/operation). Pure correlation-by-ID per ADR-0020.

**Acceptance criteria:**
- [ ] Given a `plan_id`, returns a single ordered timeline spanning all three streams.
- [ ] Correlates the upstream anomaly/proposal even though they live in different streams.
- [ ] Distinguishes outcomes (e.g. `execution.blocked`, `apply.denied`, dry-run failure) without leaking secrets/credentials.

**Verification:**
- [ ] Unit tests over a seeded multi-stream fixture assert ordering and correlation.
- [ ] `run-tests` affected tier green.

**Dependencies:** B1.

**Files likely touched:** a new read module or `src/InfraGate.Approvals*` read service + view-model records, matching tests.

**Estimated scope:** Medium.

#### Task B3: Audit Timeline Blazor page in ApprovalUi

**Description:** Add a read-only Razor component (e.g. `AuditTimelinePage.razor` + supporting components) to `InfraGate.ApprovalUi`, rendered through the existing `ApprovalPageRenderer` static-render seam. It displays the assembler's timeline: a vertical, scannable lane per phase with timestamps, actor, outcome badges, digest-binding and gate results inline. No actions, no mutation controls.

**Acceptance criteria:**
- [ ] Page renders a timeline from a supplied view model with clear phase/outcome visual grouping.
- [ ] Renders an empty/not-found state for unknown ids.
- [ ] No credentials or raw secrets shown; matches existing ApprovalUi styling.

**Verification:**
- [ ] `InfraGate.ApprovalUi.Tests` render tests pass (snapshot/markup assertions).
- [ ] `run-tests` ApprovalUi tier green.

**Dependencies:** B2.

**Files likely touched:** `src/InfraGate.ApprovalUi/Components/AuditTimeline*.razor`, `src/InfraGate.ApprovalUi/AuditTimelinePageData.cs`, renderer hook, tests under `tests/InfraGate.ApprovalUi.Tests`.

**Estimated scope:** Medium.

#### Task B4: Gateway endpoint, auth, and navigation

**Description:** Expose the timeline at an authenticated gateway route (e.g. `GET /audit/timeline/{planId}`) in `GatewayApprovalEndpoints`, behind an OAuth policy, wiring the reader → assembler → renderer. Add a discreet "View audit timeline" link from the approval decision page so a reviewer can jump from a plan to its history.

**Acceptance criteria:**
- [ ] Route requires authentication; unauthorized requests are rejected.
- [ ] A valid `planId` renders the timeline; missing/invalid renders the not-found state.
- [ ] Link present on the approval decision page (no secrets in the URL beyond the plan id).

**Verification:**
- [ ] Endpoint integration tests pass (authorized 200, unauthorized 401/403).
- [ ] `run-tests` gateway tier green.
- [ ] Manual: complete a demo plan, open its timeline from the approval page.

**Dependencies:** B1, B2, B3.

**Files likely touched:** `src/InfraGate.McpGateway/Approval/Service/GatewayApprovalEndpoints.cs`, gateway auth policy config, `src/InfraGate.ApprovalUi/Components/DecisionPage.razor`, gateway endpoint tests.

**Estimated scope:** Medium.

### Checkpoint B: Navigator works end-to-end

- [ ] From a demo run, a reviewer opens `/audit/timeline/{planId}` and sees the full correlated lifecycle.
- [ ] Unauthorized access blocked; read-only (no actions).
- [ ] All tiers green; project-reference constraints intact.
- [ ] Review with human.

### Phase C: Documentation

#### Task C1: Author `docs/observability-model.md`

**Description:** Write the observability-and-debugging model (hardening-plan Task 3). Document the current signals (Serilog logs, OTel traces/metrics, the three audit streams), the event taxonomy and the correlation path (request → anomaly → proposal → plan → challenge → grant → pre-execution gate → execution), the new dashboard and timeline navigator, common debugging flows (approval failure, digest mismatch, dry-run failure, policy denial, RBAC denial), and what remains future work (production telemetry backend, metrics SLOs).

**Acceptance criteria:**
- [ ] Covers current coverage AND gaps; marks future work explicitly.
- [ ] Documents both new surfaces (Aspire Dashboard, Audit Timeline) and how to reach them.
- [ ] Stays consistent with `src/InfraGate.Observability/README.md` and ADR-0020/0026.

**Verification:**
- [ ] `git diff --check -- docs/observability-model.md README.md`
- [ ] `verify-readme-docs` skill pass.

**Dependencies:** A, B (documents real behavior).

**Files likely touched:** `docs/observability-model.md`, `README.md` (link), `src/InfraGate.Observability/README.md` (dashboard note).

**Estimated scope:** Medium.

#### Task C2: ADR + README updates for the new surfaces

**Description:** Add an ADR (next number, ~0028) capturing the two decisions: dev-only Aspire Dashboard as the OTLP backend, and the read-only audit reader/timeline navigator (why a separate read path, why ID-correlation, access-control posture). Update README's project map to point at the observability doc and navigator.

**Acceptance criteria:**
- [ ] ADR records context, decision, consequences, and the production-vs-dev boundary.
- [ ] README links resolve; no overstatement of production readiness.

**Verification:**
- [ ] `git diff --check`
- [ ] Doc terminology scan clean.

**Dependencies:** C1.

**Files likely touched:** `docs/adr/0028-*.md`, `README.md`.

**Estimated scope:** Small.

### Checkpoint C: Senior-ready

- [ ] README points to observability model + both navigable surfaces.
- [ ] Docs distinguish shipped (dev dashboard, audit timeline) from future (production telemetry stack).
- [ ] All tests green; build clean.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Aspire Dashboard mistaken for a production telemetry stack | High | Keep it dev-compose only; document the production direction as future; never add to `production.yaml`. |
| Read path accidentally couples to or mutates the write/hash chain | High | Separate `IAuditStreamReader`; read-only SQL; assertion that no re-hash occurs; reuse existing column constants. |
| Timeline leaks secrets/credentials from payloads | High | Whitelist rendered payload fields; reuse existing sanitization conventions; render tests assert no secret fields. |
| Audit viewer authorization too broad | Medium | Gate behind an explicit policy (see Open Questions); deny-by-default; integration tests for unauthorized. |
| Observer/Planner gaining an Approvals reference via the reader | Medium | Reader lives in `AuditOutbox.Postgres`; existing project-reference assertion tests stay green. |
| Scope creep into live metrics/SLO dashboards | Medium | This plan stops at navigability; SLOs/alerting are explicitly future work in C1. |

## Open Questions

- **Audit-viewer authorization:** reuse the existing approval OAuth policy, or add a distinct `auditor`/`admin` scope? (Recommend a distinct read-only scope so viewing history is separable from approval authority.)
- **Blazor rendering mode for the timeline:** stay with the current static `ApprovalPageRenderer` HTML approach (consistent, simplest), or introduce interactive server rendering for filtering/expansion? (Recommend static first; interactivity is a later enhancement.)
- **`anomaly_id` entry point:** ship `plan_id` lookup first and add `anomaly_id` as a second entry point later, or build both in B2 from the start?
- **Dashboard auth:** the Aspire Dashboard supports token auth — enable it even for dev, or leave open on localhost-bound port only?
