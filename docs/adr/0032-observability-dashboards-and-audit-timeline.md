# 32. Observability Dashboards and Audit Timeline Navigator

Date: 2026-06-24

## Status

Accepted

## Context

InfraGate already emits good telemetry (Serilog structured logs + OpenTelemetry traces/metrics via `InfraGate.Observability`, ADR-0026) and keeps a strong tamper-evident audit trail (hash-chained Postgres outbox, `approvals`/`observer`/`planner` streams, ADR-0020). Neither was *approachable*:

- Telemetry had no backend to render it. No collector/dashboard existed in the dev compose stack; metrics and traces were emitted into the void unless an operator hand-wired `OTEL_EXPORTER_OTLP_ENDPOINT`. The Gateway and Executor did not even call `AddInfraGateTelemetry`, so their meters/tracers were never registered at all.
- The audit trail could only be read with raw SQL. There was no way for a human reviewer to follow one incident — anomaly → proposal → plan → challenge → grant → pre-execution gate → execution — as a single ordered narrative across the three independent streams.

This ADR records the two decisions that closed those gaps, plus the access-control and production-boundary posture for each.

## Decision

### 1. Standalone Aspire Dashboard container, dev/demo only

Added an `aspire-dashboard` service (`mcr.microsoft.com/dotnet/aspire-dashboard`) to `deploy/local-oauth/compose.yaml`. It is a zero-config OTLP receiver that renders traces (including agent-framework spans — model, agent, token counts, tool calls), metrics, and logs with no application code.

Rejected: Grafana LGTM (Loki/Grafana/Tempo) stack. It renders agent-framework spans identically to any generic OTel exporter, but needs multiple containers and config files for a dev-loop payoff the Aspire Dashboard gets in one container with native span rendering.

The dashboard is explicitly a **development and short-term diagnostic tool** — it holds telemetry in memory, discards it on restart, and is never added to `deploy/compose/production.yaml`. The production direction (Tempo/Prometheus/Loki, or a hosted OTLP endpoint) is documented as future work in `docs/observability-model.md`, not built.

Two container-configuration details are load-bearing and easy to get wrong when reading the standalone-dashboard docs superficially: the container's internal OTLP/gRPC port is `18889` (the host port `4317` is only the published mapping — other containers must reach it at `aspire-dashboard:18889`, not `:4317`), and the browser-token auth keys are `Dashboard:Frontend:AuthMode`/`Dashboard:Frontend:BrowserToken` (as `DASHBOARD__FRONTEND__AUTHMODE`/`DASHBOARD__FRONTEND__BROWSERTOKEN`), not `Dashboard:Otlp:*` (which controls *inbound telemetry* auth, not the UI).

The dashboard's browser-token auth is enabled even in dev (not anonymous access) so a shared dev environment doesn't leave telemetry — which can include prompt/response content if `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` is later enabled — open to anyone who can reach the port. The token is a first-class run-profile concern: `deploy/run-profiles.yaml`'s `telemetry.dashboardToken` key flows through `InfraGate.RunProfiles` into the generated (non-committed) env file as `ASPIRE_DASHBOARD_TOKEN`, alongside `telemetry.otlpEndpoint` → `OTEL_EXPORTER_OTLP_ENDPOINT`. This keeps telemetry configuration in one place instead of scattered across compose `environment:` blocks, matching how every other per-profile setting in this repo is discovered.

The container image has no shell, `curl`, or `wget` (same chiseled-image constraint already true of `mcp-gateway.Dockerfile`), and its `/health`-shaped paths require an authenticated session, so no Docker `healthcheck` is attached to it — one was attempted and reliably reported `unhealthy` for both reasons. Nothing in the compose file depends on it via `condition: service_healthy`, so this doesn't affect startup ordering.

### 2. Read-only audit stream reader + cross-stream timeline assembler

`IPostgresAuditOutboxCore` is append-only by design (ADR-0020's tamper-evidence guarantee). Rather than overload the writer, a separate `IAuditStreamReader` (`InfraGate.AuditOutbox.Postgres`) was added with a **typed** interface — `ReadByPlanIdAsync(stream, id, ct)` / `ReadByAnomalyIdAsync(stream, id, ct)` — instead of a generic `(stream, column, value)` query. Callers state intent; column-name knowledge stays inside the reader; there is no SQL-identifier interpolation surface to begin with. The `stream` argument is validated against `AuditOutboxConventions.Streams` before use. The reader touches no write path and never recomputes or verifies hashes — it is intentionally a dumb, read-only projection.

`AuditTimelineAssembler` (`InfraGate.McpGateway.Audit`) is the correlation layer: given a `plan_id`, it reads the `approvals` and `planner` streams by plan, discovers the `anomaly_id` from whichever row carries it, and reads the `observer` stream by that `anomaly_id`. Correlation is **by ID, matching ADR-0020** — there is no shared hash chain across streams, and this ADR does not invent one. Display fields are pulled from an explicit allow-list (namespace, operation, status, gate result, digest fields) rather than passed through opaque payload JSON, so secrets/credentials cannot leak through a schema change elsewhere in the payload.

Only the `plan_id` entry point ships (`GET /audit/timeline/{planId}`). The reader also supports `anomaly_id` lookups internally, but no route exposes `/audit/timeline/anomaly/{anomalyId}` yet — that is deferred until the primary flow is proven in use.

### 3. Timeline lives in `InfraGate.ApprovalUi`, hosted by the gateway, behind a dedicated read-only policy

`InfraGate.ApprovalUi` is already a Razor component library rendered to static HTML by `ApprovalPageRenderer` and served via `GatewayApprovalEndpoints` — the approval challenge page uses exactly this seam. The Audit Timeline page reuses it rather than introducing a second hosting mode (e.g. interactive Blazor Server), so the gateway's existing OAuth pipeline and DB access apply unchanged and the page is read-only by construction — there is no mutation form to secure.

The route is gated by a distinct `audit:read` OAuth scope/policy (`GatewayAuthConventions.DefaultAuditReadOAuthScope`), not the approval policy. Viewing audit history and approving a mutation are different authorities; collapsing them would mean anyone who can view history could also approve, or vice versa. A discreet "View audit timeline" link is added to the approval challenge page so a reviewer can check an incident's history before deciding, without granting the two authorities together.

## Consequences

- All four runtime services (Gateway, Observer, Planner, Executor) now register `AddInfraGateTelemetry`; setting `OTEL_EXPORTER_OTLP_ENDPOINT` (wired by default in the `local-compose` run profile) makes every service's spans and meters visible in one place with zero additional app code.
- Gateway, Observer, Planner, and Executor all expose `/healthz` and `/readyz`, and the dev compose stack's `depends_on: condition: service_healthy` chains now mean something.
- A reviewer can answer "what happened to this plan, from anomaly to execution" from a single authenticated URL instead of raw SQL, without touching the tamper-evident write path.
- The Aspire Dashboard must not be mistaken for a production telemetry backend — it is compose-local, in-memory, and absent from `production.yaml` by design; `docs/observability-model.md` states the production direction as future work, not shipped.
- Audit-viewing authority (`audit:read`) is now separable from approval authority, narrowing the blast radius of over-broad access grants.

## References

- ADR-0020: Audit Outbox Architecture — Per-Component Audit Streams with Same-Transaction Hash Chain
- ADR-0026: OpenTelemetry Agent Observability and Serilog Bridge
- `docs/observability-model.md`: signals, correlation path, and debugging flows for the surfaces this ADR introduces
- Aspire standalone dashboard: https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone
- Aspire dashboard configuration reference: https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/configuration
