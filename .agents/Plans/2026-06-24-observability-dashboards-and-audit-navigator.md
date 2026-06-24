# Implementation Plan: Approachable Observability — Telemetry Dashboard + Audit Timeline Navigator

## Overview

InfraGate already emits good telemetry (Serilog + OpenTelemetry traces/metrics via `InfraGate.Observability`, ADR-0026) and keeps a strong tamper-evident audit trail (hash-chained Postgres outbox, `approvals`/`observer`/`planner` streams, ADR-0020). The problem is **approachability**: the telemetry has no backend to render it (no collector/dashboard exists; metrics and traces are emitted into the void unless an operator hand-wires `OTEL_EXPORTER_OTLP_ENDPOINT`), and the audit trail can only be read with raw SQL — there is no way for a human to *follow one incident* across streams.

This plan delivers two complementary, human-facing surfaces:

- **A. Turnkey telemetry dashboard** — a standalone .NET Aspire Dashboard container in the dev compose that receives OTLP and renders traces (including agent-framework spans: model, agent, token counts, tool calls), metrics, and structured logs with zero app code. Plus the wiring fixes that make *all* services actually export.
- **B. Audit Timeline navigator** — a read-only, OAuth-gated page in the existing `InfraGate.ApprovalUi` that takes a `plan_id` (or `anomaly_id`) and stitches the full Human-in-the-Loop lifecycle (anomaly → proposal → plan → challenge → grant → pre-execution gates → execution) into one ordered, correlated timeline. This is the differentiated, security-engineering signal a generic dashboard cannot express.

Plus health/readiness endpoints (basic operational hygiene) and the `docs/observability-model.md` the hardening plan calls for.

## Architecture Decisions

- **Aspire Dashboard, not Grafana LGTM** (user decision). One standalone container (`mcr.microsoft.com/dotnet/aspire-dashboard`), zero-config OTLP receiver, renders traces+metrics+logs and renders agent spans natively. Dev/demo backend only — explicitly **not** a production telemetry stack. Production direction (Tempo/Prometheus/Loki or a hosted OTLP endpoint) is documented as future, not built.
- **The audit trail needs a read side.** `IPostgresAuditOutboxCore` is append-only today. We add a separate, read-only `IAuditStreamReader` rather than overloading the writer — keeps the tamper-evident write path untouched and the read concern isolated. The reader does not, and must not, mutate or re-hash rows. The seam is typed (`ReadByPlanIdAsync`, `ReadByAnomalyIdAsync`) rather than generic `(stream, column, value)`: callers state intent, column-name knowledge stays inside the reader, and the SQL-identifier injection surface disappears.
- **Timeline navigator lives in `InfraGate.ApprovalUi`, hosted by the gateway.** ApprovalUi is a Razor component library rendered to HTML by `ApprovalPageRenderer` and served via `GatewayApprovalEndpoints`. The timeline reuses that exact hosting seam, so it inherits the gateway's OAuth pipeline and DB access. No new host process.
- **Read-only and access-controlled.** The timeline is observability, not a control surface — no actions, no mutations. It sits behind a dedicated read-only OAuth policy/scope (`auditor` or `audit:read`) so viewing history is separable from approval authority.
- **Correlation is by ID, matching ADR-0020.** The assembler joins streams on shared IDs (`plan_id`, `anomaly_id`); there is no shared hash chain across streams and we do not invent one.
- **No new magic strings; honor `code-standards`.** Stream names, event names, column names already exist as constants (`AuditOutboxConventions`, `*AuditEvents`); the reader/assembler reuse them.
- **Telemetry configuration is a first-class run-profile concern.** `OTEL_EXPORTER_OTLP_ENDPOINT` flows through a `telemetry.otlpEndpoint` key in `deploy/run-profiles.yaml` and is emitted into the generated env file, rather than being scattered across compose `environment:` blocks.

## Dependency Graph

```
Phase A (telemetry backend)            Phase B (audit navigator)
  A1 aspire-dashboard + OTLP env         B1 IAuditStreamReader (Postgres read side)
  A2 extend telemetry to GW + Executor        │
  A3a gateway health endpoints           B2 Timeline assembler (correlate streams)
  A3b agent health/readiness endpoints        │
  A3c compose healthcheck wiring         B3 Blazor Timeline page (ApprovalUi)
        │                                       │
        └── independent of B               B4 Gateway endpoint + auth + nav link

Phase C (docs) depends on A + B being built (documents real behavior)
```

Phases A and B are independent and can proceed in parallel. Within B, order is strict: B1 → B2 → B3 → B4.

## Task List

### Phase A: Make telemetry visible

#### Task A1: Add Aspire Dashboard to dev compose and route OTLP to it

**Description:** Add an `aspire-dashboard` service to `deploy/local-oauth/compose.yaml` (full local stack) and, optionally, `deploy/local-oauth/compose.release.yaml` (gateway-only release demo). Expose the dashboard UI and the OTLP gRPC ingress port (4317). Set `OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:4317` for all four runtime services once their telemetry providers are registered.

Make the OTLP endpoint a first-class run-profile concern: add `telemetry.otlpEndpoint` to `deploy/run-profiles.yaml` so `InfraGate.RunProfiles` emits `OTEL_EXPORTER_OTLP_ENDPOINT` into the generated `deploy/generated/local-compose.env`. This keeps configuration discovery in one place and avoids ad-hoc `environment:` blocks. Do not add Aspire to `deploy/compose/production.yaml`.

**Acceptance criteria:**
- [ ] `aspire-dashboard` container starts in the dev compose; UI reachable on a bound localhost port.
- [ ] Dashboard token auth is enabled; the token is generated into the run-profile env output (or another non-committed dev env file) and documented.
- [ ] `OTEL_EXPORTER_OTLP_ENDPOINT` is set for gateway, observer, planner, and executor via the generated run-profile output (not committed secrets).
- [ ] After a demo cycle, agent-framework spans (model, agent, token counts) and the service meters appear in the dashboard.

**Verification:**
- [ ] `docker compose -f deploy/local-oauth/compose.yaml config` validates.
- [ ] Manual: run the failing-deployment demo (`docs/demo-failing-deployment.md`), confirm traces + `infragate.observer.*` / `infragate.planner.*` metrics render.
- [ ] `git diff --check`

**Dependencies:** None.

**Files likely touched:** `deploy/local-oauth/compose.yaml`, `deploy/local-oauth/compose.release.yaml`, `deploy/run-profiles.yaml`, `deploy/generated/local-compose.env`, `deploy/local-oauth/release.env.example` (optional).

**Estimated scope:** Small.

#### Task A2: Register telemetry for the gateway and executor

**Description:** `McpGateway` and `Executor` already call `AddInfraGateObservability` (Serilog), but they never call `AddInfraGateTelemetry`, so no `MeterProvider`/`TracerProvider` is registered and their metrics/traces never export. Add `AddInfraGateTelemetry` to both hosts with the correct `ServiceName` and `MeterNames`, mirroring the Observer/Planner blocks.

**Acceptance criteria:**
- [ ] `McpGateway` Program registers telemetry with `ServiceName = "infragate-gateway"` and meter names `McpGatewayConventions.Telemetry.MeterName` and `AgentGuardrailConventions.MeterName`.
- [ ] `Executor` Program registers telemetry with `ServiceName = "infragate-executor"` and meter name `ExecutorMetrics.MeterName`.
- [ ] With A1's endpoint set, gateway metrics (`infragate.gateway.*`), executor metrics (`infragate.executor.execute.*`), and guardrail counters appear in the dashboard.

**Verification:**
- [ ] Build: `dotnet build`
- [ ] Existing tests pass: `run-tests` (gateway + executor tiers).
- [ ] Manual: trigger an execution and a blocked guardrail; confirm gateway (`infragate.gateway.*`), executor (`infragate.executor.execute.*`), and guardrail counters render.

**Dependencies:** None (but pairs with A1 for the visible check).

**Files likely touched:** `src/InfraGate.McpGateway/Program.cs` (or its configuration extension), `src/InfraGate.Executor/Program.cs`.

**Estimated scope:** Small.

#### Task A3a: Add gateway health and readiness endpoints

**Description:** The gateway currently has no health endpoint. Add a `GatewayHealthEndpoint` exposing `/healthz` (liveness) and `/readyz` (readiness, including the Npgsql data source check). Keep the existing MCP and approval routes unchanged.

**Acceptance criteria:**
- [ ] Gateway exposes `/healthz` and `/readyz`.
- [ ] `/readyz` fails when the Postgres data source is unreachable.
- [ ] Existing `/mcp` and `/approvals/*` routes remain unaffected.

**Verification:**
- [ ] Build: `dotnet build`
- [ ] New/existing gateway tests pass.
- [ ] Manual or test-host HTTP request: `/healthz` returns 200, `/readyz` returns 503 when Postgres is down.

**Dependencies:** None.

**Files likely touched:** `src/InfraGate.McpGateway/Endpoints/GatewayHealthEndpoint.cs`, `src/InfraGate.McpGateway/McpGatewayConventions.cs`, `src/InfraGate.McpGateway/Program.cs`, `tests/InfraGate.McpGateway.Tests`.

**Estimated scope:** Small.

#### Task A3b: Extend agent health endpoints to `/healthz` + `/readyz`

**Description:** Observer, Planner, and Executor already expose a single `/health` endpoint that probes token acquisition. Extend/rename this to `/healthz` (liveness) and `/readyz` (readiness). For Observer and Planner, readiness should also verify their audit Postgres data source; Executor readiness can remain token-acquisition based. Preserve `/health` (e.g., as an alias or redirect) so existing convention tests and E2E fixtures keep working, or update those tests explicitly.

**Acceptance criteria:**
- [ ] Each agent host exposes `/healthz` and `/readyz`.
- [ ] Readiness fails when Postgres (for Observer/Planner) or the token provider (IdP) is unreachable.
- [ ] `/health` still responds correctly, or all existing convention tests and E2E fixtures are updated to the new path.

**Verification:**
- [ ] Build: `dotnet build`
- [ ] `run-tests` for Observer, Planner, and Executor tiers green.
- [ ] Manual: `curl` `/healthz` and `/readyz` per service returns 200; stop Postgres and confirm `/readyz` flips to unhealthy for DB-backed services.

**Dependencies:** None.

**Files likely touched:** `src/InfraGate.Observer/Endpoints/HealthEndpoint.cs`, `src/InfraGate.Planner/Endpoints/HealthEndpoint.cs`, `src/InfraGate.Executor/Endpoints/HealthEndpoint.cs`, `src/InfraGate.Observer/ObserverConventions.cs`, `src/InfraGate.Planner/PlannerConventions.cs`, `src/InfraGate.Executor/ExecutorConventions.cs`, `tests/InfraGate.Observer.Tests/UnitTests/ObserverConventionsTests.cs`, `tests/InfraGate.Planner.Tests/UnitTests/PlannerConventionsTests.cs`, `tests/InfraGate.Executor.Tests/UnitTests/ExecutorConventionsTests.cs`, `tests/InfraGate.Observer.E2E.Tests/ObserverE2EFixture.cs`, `tests/InfraGate.Remediation.E2E.Tests/RemediationE2EFixture.cs`.

**Estimated scope:** Medium.

#### Task A3c: Wire compose healthchecks and startup ordering

**Description:** Add compose `healthcheck` blocks for gateway, observer, planner, and executor. Use `depends_on … condition: service_healthy` to make startup ordering meaningful. Note that the gateway runtime image uses `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, which has no shell, `curl`, or `wget`; the probe must be compatible (e.g., a small `dotnet` health-check command, a `HEALTHCHECK` instruction in the Dockerfile, or a host-side HTTP probe) rather than a shell-based curl.

**Acceptance criteria:**
- [ ] `docker compose -f deploy/local-oauth/compose.yaml config` validates.
- [ ] Each runtime service has a `healthcheck` referencing `/healthz` or `/readyz`.
- [ ] Startup ordering uses `service_healthy` where appropriate.

**Verification:**
- [ ] `docker compose -f deploy/local-oauth/compose.yaml config`
- [ ] Run the stack and inspect `docker compose ps`; services report `healthy` once dependencies are up.

**Dependencies:** A3a, A3b.

**Files likely touched:** `deploy/local-oauth/compose.yaml`, `deploy/local-oauth/compose.release.yaml`, `deploy/docker/mcp-gateway.Dockerfile` (if a Dockerfile `HEALTHCHECK` is chosen), possibly agent Dockerfiles.

**Estimated scope:** Small.

### Checkpoint A: Telemetry is visible

- [ ] Build clean, all existing tests green.
- [ ] One demo run produces visible traces (agent spans), metrics (all four services), and logs in the Aspire Dashboard.
- [ ] Health/readiness endpoints respond correctly and compose healthchecks are wired.
- [ ] Review with human before starting the navigator UI.

### Phase B: Audit Timeline navigator

#### Task B1: Add a read-only audit stream reader

**Description:** Add `IAuditStreamReader` and a Postgres implementation in `InfraGate.AuditOutbox.Postgres` that queries committed rows from a given stream filtered by a correlation ID (`plan_id` or `anomaly_id`), returning ordered `AuditOutboxRow` plus `sequence` and `occurred_at`. Strictly read-only; no writes, no re-hashing. Reuse `AuditOutboxConventions` for schema/column/stream names.

A typed interface is preferred over a generic `(stream, column, value)` query: it removes the SQL-identifier injection surface entirely, makes the seam deeper (callers state intent, not schema mechanics), and keeps column-name knowledge local to the reader implementation per `code-standards` and `improve-codebase-architecture`.

**Acceptance criteria:**
- [ ] `IAuditStreamReader.ReadByPlanIdAsync(stream, planId, ct)` returns rows ordered by sequence for the given stream and `plan_id`.
- [ ] `IAuditStreamReader.ReadByAnomalyIdAsync(stream, anomalyId, ct)` returns rows ordered by sequence for the given stream and `anomaly_id`.
- [ ] The `stream` argument is validated against `AuditOutboxConventions.Streams` before use; no caller-supplied column names are interpolated.
- [ ] Implementation touches no write path and does not recompute or verify hashes.
- [ ] Returns empty (not error) for an unknown id.

**Verification:**
- [ ] New unit/integration tests pass (`InfraGate.AuditOutbox.Postgres` integration tier, Postgres container).
- [ ] `run-tests` for the affected tier.
- [ ] Project-reference assertion tests still pass (Observer/Planner must not gain an Approvals reference).

**Dependencies:** None.

**Files likely touched:** `src/InfraGate.AuditOutbox.Postgres/IAuditStreamReader.cs`, `…/PostgresAuditStreamReader.cs`, `…/ServiceCollectionExtensions.cs`, `src/InfraGate.AuditOutbox.Postgres/AuditOutboxConventions.cs` (add `Streams` constant if missing), tests under `tests/InfraGate.AuditOutbox*`/integration.

**Estimated scope:** Medium.

#### Task B2: Build the cross-stream timeline assembler

**Description:** Add a small read-side service in the gateway (e.g., `src/InfraGate.McpGateway/Audit/`) that takes a `plan_id`, reads the `approvals`, `observer`, and `planner` streams via `IAuditStreamReader.ReadByPlanIdAsync`, and assembles an ordered, typed timeline. Resolve `anomaly_id`, `proposal_id`, and `cycle_id` from the returned correlation columns, not by parsing payloads; use payload JSON only for whitelisted display fields (namespace, operation, digest/gate results). Assumes the shared Postgres deployment from ADR-0020; the assembler uses the gateway's existing `NpgsqlDataSource` because all streams live in the same database instance. Output is a view model: ordered entries with `OccurredAtUtc`, stream, event name, outcome/reason, actor, and selected payload fields. Pure correlation-by-ID per ADR-0020.

This slice ships the `plan_id` entry point only; `anomaly_id` lookup is future work (see Decisions).

**Acceptance criteria:**
- [ ] Given a `plan_id`, returns a single ordered timeline spanning all three streams.
- [ ] Correlates the upstream anomaly/proposal even though they live in different streams by reading their stream rows via `ReadByAnomalyIdAsync` once the `anomaly_id` is discovered in the planner/approvals rows.
- [ ] Distinguishes outcomes (e.g. `execution.blocked`, `apply.denied`, dry-run failure) without leaking secrets/credentials.

**Verification:**
- [ ] Unit tests over a seeded multi-stream fixture assert ordering and correlation.
- [ ] `run-tests` affected tier green.

**Dependencies:** B1.

**Files likely touched:** `src/InfraGate.McpGateway/Audit/AuditTimelineAssembler.cs`, `src/InfraGate.ApprovalUi/AuditTimelineEntry.cs` (or similar view-model records), `src/InfraGate.ApprovalUi/AuditTimelinePageData.cs`, `src/InfraGate.McpGateway/InfraGate.McpGateway.csproj` (if a direct `InfraGate.AuditOutbox.Postgres` reference is needed), tests under `tests/InfraGate.McpGateway.Tests`.

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

**Files likely touched:** `src/InfraGate.ApprovalUi/Components/AuditTimeline*.razor`, `src/InfraGate.ApprovalUi/AuditTimelinePageData.cs`, `src/InfraGate.ApprovalUi/IApprovalPageRenderer.cs` (add `RenderAuditTimelinePageAsync`), `src/InfraGate.ApprovalUi/ApprovalPageRenderer.cs`, tests under `tests/InfraGate.ApprovalUi.Tests`.

**Estimated scope:** Medium.

#### Task B4: Gateway endpoint, auth, and navigation

**Description:** Expose the timeline at an authenticated gateway route (e.g. `GET /audit/timeline/{planId}`) in `GatewayApprovalEndpoints`, behind a dedicated read-only OAuth policy/scope (`auditor` or `audit:read`), wiring the reader → assembler → renderer. Add a discreet "View audit timeline" link from the approval challenge page (`ApprovalPageContent.razor`) so a reviewer can jump from a plan to its history before deciding.

**Acceptance criteria:**
- [ ] Route requires the `auditor`/`audit:read` policy; unauthorized or unauthenticated requests are rejected.
- [ ] A valid `planId` renders the timeline; missing/invalid renders the not-found state.
- [ ] Link present on the approval challenge page (no secrets in the URL beyond the plan id).

**Verification:**
- [ ] Endpoint integration tests pass (authorized 200, unauthorized 401/403).
- [ ] `run-tests` gateway tier green.
- [ ] Manual: complete a demo plan, open its timeline from the approval page.

**Dependencies:** B1, B2, B3.

**Files likely touched:** `src/InfraGate.McpGateway/Approval/Service/GatewayApprovalEndpoints.cs`, gateway auth policy config, `src/InfraGate.ApprovalUi/Components/ApprovalPageContent.razor`, gateway endpoint tests.

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

**Description:** Add an ADR (next number, 0032) capturing the two decisions: dev-only Aspire Dashboard as the OTLP backend, and the read-only audit reader/timeline navigator (why a separate read path, why ID-correlation, access-control posture). Update README's project map to point at the observability doc and navigator. Name the file `docs/adr/0032-observability-dashboards-and-audit-timeline.md` (or similar).

**Acceptance criteria:**
- [ ] ADR records context, decision, consequences, and the production-vs-dev boundary.
- [ ] README links resolve; no overstatement of production readiness.

**Verification:**
- [ ] `git diff --check`
- [ ] Doc terminology scan clean.

**Dependencies:** C1.

**Files likely touched:** `docs/adr/0032-observability-dashboards-and-audit-timeline.md`, `README.md`.

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
| Audit viewer authorization too broad | Low | Gate behind a dedicated read-only `auditor`/`audit:read` policy; deny-by-default; integration tests for unauthorized. |
| Observer/Planner gaining an Approvals reference via the reader | Medium | Reader lives in `AuditOutbox.Postgres`; existing project-reference assertion tests stay green. |
| Scope creep into live metrics/SLO dashboards | Medium | This plan stops at navigability; SLOs/alerting are explicitly future work in C1. |
| Compose healthchecks incompatible with chiseled gateway image | Medium | The gateway image is `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (no shell/curl/wget). Design a `dotnet`-based probe, a Dockerfile `HEALTHCHECK`, or a host-side HTTP probe rather than a shell-based curl. |
| Aspire Dashboard left open in shared dev environments | Low | Enable the dashboard's built-in token auth in dev; generate the token through the run-profile output/env file and document it in C1. |

## Decisions

These open questions are resolved for this plan, preferring the simpler, first-class, architecture-aligned option:

- **Audit-viewer authorization:** Add a distinct read-only OAuth policy/scope (`auditor` or `audit:read`) rather than reusing the approval policy. Viewing audit history is a separate concern from approving mutations; a dedicated policy makes that seam explicit and deny-by-default.
- **Blazor rendering mode:** Stay with the existing static `ApprovalPageRenderer` HTML seam. It is consistent with the approval pages, requires no new host configuration, and keeps the timeline read-only by construction. Interactive filtering/expansion is future work.
- **`anomaly_id` entry point:** Ship the `plan_id` lookup first (B2–B4). The reader interface supports `anomaly_id` correlation internally, but the gateway route and UI link expose `plan_id` only. Add a dedicated `/audit/timeline/anomaly/{anomalyId}` route later once the primary flow is proven.
- **Dashboard auth:** Enable the Aspire Dashboard's built-in token auth even in dev. Generate the token via run-profile output or an env file so the dashboard is not accidentally left open when the stack is shared.
- **Run-profile telemetry key:** Add a first-class `telemetry.otlpEndpoint` key to `deploy/run-profiles.yaml` so `OTEL_EXPORTER_OTLP_ENDPOINT` flows through the generated env file. This centralizes telemetry discovery and avoids ad-hoc compose `environment:` blocks.

## Open Questions

- None remaining for this plan. File new questions as risks if they surface during implementation.
