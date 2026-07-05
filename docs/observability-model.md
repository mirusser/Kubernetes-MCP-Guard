# Observability and Debugging Model

This document describes how to observe and debug a running InfraGate stack: what signals exist today, how to correlate a single incident across the three autonomous agents (Observer, Planner, Executor) and the Gateway's approval flow, and what is intentionally out of scope for now.

It complements [ADR-0020](adr/0020-audit-outbox-architecture.md) (audit outbox architecture), [ADR-0026](adr/0026-opentelemetry-agent-observability-and-serilog-bridge.md) (OpenTelemetry + Serilog bridge), and [ADR-0032](adr/0032-observability-dashboards-and-audit-timeline.md) (the two surfaces this document describes).

## Signals

| Signal | Source | Where it goes by default | Where it can go |
| --- | --- | --- | --- |
| Structured logs | Serilog, all four services (`InfraGate.Observability`) | Console (and file sink for Observer, when configured) | Any Serilog sink |
| Traces | OpenTelemetry `TracerProvider` (agent-framework spans for Observer/Planner; ASP.NET Core + HttpClient spans for all four) | Bridged into Serilog as structured log events (`SerilogSpanProcessor`) | OTLP, when `OTEL_EXPORTER_OTLP_ENDPOINT` is set |
| Metrics | OpenTelemetry `MeterProvider` — `infragate.observer.*`, `infragate.planner.*`, `infragate.gateway.*`, `infragate.executor.execute.*`, guardrail/hallucination counters | Not visible without an OTLP backend | OTLP, when `OTEL_EXPORTER_OTLP_ENDPOINT` is set |
| Audit events | Three hash-chained Postgres outbox streams (`approvals`, `observer`, `planner`) per ADR-0020 | Postgres, queryable only with raw SQL | The Audit Timeline navigator (below) |

Both the trace/metric pipeline and the audit streams are independent: the audit streams are the tamper-evident, durable record of what happened; traces/metrics are for performance and liveness, not forensics. They are correlated by convention (both carry `plan_id`/`anomaly_id`), not by a shared identifier scheme.

See [`src/InfraGate.Observability/README.md`](../src/InfraGate.Observability/README.md) and [`docs/devs-readme.md`](devs-readme.md#telemetry-gateway-observer-planner-executor) for the environment variables that control export.

## Event taxonomy and correlation path

A single Human-in-the-Loop remediation incident moves through this sequence, each step durably recorded in one of the three audit streams:

```
observer stream    planner stream         approvals stream
───────────────    ──────────────         ────────────────
anomaly.detected → proposal.created
                    plan.proposed      →  challenge.created
                                          grant.issued | grant.denied
                                          pre_execution.gate.{passed|blocked}
                                          execution.{applied|blocked}
```

Correlation is **by ID, not by a shared hash chain** (ADR-0020's explicit rejection of a unified chain): each stream is independently tamper-evident, and rows carry `plan_id` and/or `anomaly_id` as plain correlation columns. The Audit Timeline navigator (below) is the read-side that joins the three streams on those IDs.

Common outcomes to look for, in the `approvals` stream:

- `challenge.created` / `grant.issued` / `grant.denied` — human-in-the-loop decision.
- `pre_execution.gate.blocked` — a gate 7/8 check (see `docs/mutation-approval-flow.md`) failed immediately before execution (e.g. resource-version drift).
- `execution.blocked` / `apply.denied` — the plan was not applied even though a grant existed (digest mismatch, expired grant, single-execution already consumed).

## Surfaces

### Aspire Dashboard (dev/demo telemetry backend)

`deploy/local-oauth/compose.yaml` runs a standalone `mcr.microsoft.com/dotnet/aspire-dashboard` container. It is a **zero-config OTLP receiver and viewer for traces, metrics, and logs — a development and short-term diagnostic tool, not a production monitoring stack** (it holds telemetry in memory and discards it on restart).

- UI: `http://127.0.0.1:18888` (bind address/port configurable via `ASPIRE_DASHBOARD_BIND_ADDRESS`/`ASPIRE_DASHBOARD_UI_PORT`).
- OTLP/gRPC ingress: host port `4317` (mapped to the container's actual internal OTLP port, `18889`); other containers on the compose network reach it at `http://aspire-dashboard:18889`, which is the value `InfraGate.RunProfiles` emits into `OTEL_EXPORTER_OTLP_ENDPOINT`.
- The dashboard UI is protected by browser-token auth. The token comes from `run-profiles.yaml`'s `telemetry.dashboardToken` (emitted as `ASPIRE_DASHBOARD_TOKEN`) — see [`docs/devs-readme.md`](devs-readme.md#telemetry-gateway-observer-planner-executor).
- All four services (Gateway, Observer, Planner, Executor) register `AddInfraGateTelemetry`; once `OTEL_EXPORTER_OTLP_ENDPOINT` is set, their traces (including agent-framework spans: model, agent, token counts, tool calls) and meters export here automatically.

Production direction (not built): point `OTEL_EXPORTER_OTLP_ENDPOINT` at Tempo/Prometheus/Loki or a hosted OTLP collector. No code change is required — only the endpoint and, if the backend requires it, OTLP auth headers.

### Audit Timeline navigator

A read-only page in `InfraGate.ApprovalUi`, served by the gateway at `GET /audit/timeline/{planId}`, behind the `audit:read` OAuth policy (distinct from the approval policy — viewing history does not require approval authority). Given a `plan_id`, it renders the full correlated lifecycle above as an ordered timeline (phase, timestamp, actor, outcome, digest/gate results), reusing the same static Razor-to-HTML seam as the approval challenge page. A "View audit timeline" link is present on the approval challenge page itself, so a reviewer can check history before approving.

It is implemented by:
- `IAuditStreamReader` (`InfraGate.AuditOutbox.Postgres`) — typed, read-only queries (`ReadByPlanIdAsync`, `ReadByAnomalyIdAsync`) against the existing hash-chained tables. No write path, no re-hashing.
- `AuditTimelineAssembler` (`InfraGate.McpGateway.Audit`) — joins the `approvals`, `planner`, and `observer` streams by `plan_id`/`anomaly_id` and whitelists which payload fields render (namespace, operation, status, gate result, digest fields) — never raw secrets or credentials.

Only the `plan_id` entry point ships today; `/audit/timeline/anomaly/{anomalyId}` is future work once the primary flow is proven (see ADR-0032).

## Common debugging flows

| Symptom | Where to look |
| --- | --- |
| Approval never resolves / plan stuck pending | Audit Timeline for the `plan_id` — check whether `challenge.created` exists but no `grant.issued`/`grant.denied` followed; then check the approval-page logs for delivery failures (email/notification). |
| "Digest mismatch" on apply | Audit Timeline — look for `execution.blocked`/`apply.denied` with a digest-related reason; compare `digest_value` in the `approvals` stream entry against the plan's current Intent/Review Digest (`docs/mutation-approval-flow.md`). |
| Dry-run failure surfaced to the client | Gateway structured logs around the `request_*` tool call (trace-correlated via `TraceId`); the audit stream only records the *plan proposal*, not the dry-run response body. |
| Policy denial (Operator Approval Policy) | Planner structured logs for the decision, plus `proposal.created`/`plan.proposed` events in the planner stream — the assembled timeline shows whether a plan was ever proposed for the anomaly. |
| RBAC / auth denial (401/403 from the gateway) | Gateway structured logs (JWT validation failure reason) — this occurs before any audit-stream write, so the Timeline will show nothing for a request that never authenticated. |
| Missing / no traces in the dashboard | Confirm `OTEL_EXPORTER_OTLP_ENDPOINT` is set for the service (via the generated run-profile env) and that the Aspire Dashboard container is healthy and reachable at that address — see the Aspire Dashboard section above. |

## Gaps and future work

- **Production telemetry backend.** The Aspire Dashboard is explicitly dev/demo-only (in-memory, no persistence, no clustering). A production deployment needs Tempo/Prometheus/Loki or a hosted OTLP endpoint — not built here.
- **Metrics SLOs / alerting.** No alerting rules or SLO definitions exist yet; this plan stops at making signals navigable, not at operating them.
- **`anomaly_id` timeline entry point.** The reader supports it; the gateway route and UI do not expose it yet.
- **Cross-signal correlation UI.** Traces/metrics (Aspire Dashboard) and the audit trail (Timeline navigator) are two separate surfaces today; there is no single view linking a `TraceId` to a `plan_id`.
