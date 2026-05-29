# InfraGate.Observer

`InfraGate.Observer` is an LLM-driven anomaly detection agent that periodically inspects Kubernetes through the MCP gateway's read-only tools and emits structured **Anomaly Reports**. It runs as an ASP.NET `WebApplication` alongside the gateway and authenticates via OAuth client_credentials with the `mcp:tools.readonly` scope.

**Owns:** read-only anomaly detection

## Runtime Flow

- `Program.cs` wires Serilog, `InfraGate.RuntimeSafety` mode detection, `InfraGate.ClientCredentials` OAuth token acquisition, and the `Microsoft.Extensions.AI` `IChatClient` for the chosen LLM provider.
- `ObservationCycleLoop` (an `IHostedService`) ticks on a configurable cadence (default 60s) and orchestrates one **Observation Cycle** per tick.
- Each cycle builds a per-namespace **workflow graph** via `ObservationCycleRunner.BuildWorkflow` using `Microsoft.Agents.AI.Workflows.WorkflowBuilder`. The graph fans out from a `CycleInputPassthroughExecutor` to N per-namespace chains, each running:
  1. `SnapshotExecutor` — calls `ISnapshotFetcher` to collect a deterministic baseline (`SnapshotDocument`) from the gateway's read-only tools.
  2. `ChatClientAgent` (via `ToolCallingAgentFactory`) — sends the snapshot to the LLM with the system prompt from `Prompts/ObserverSystemPrompt.md`; bounded follow-up tool calls (max 8 per cycle).
  3. `AnomalyParseExecutor` — parses the LLM output into `AnomalyReport[]`, applies `ISeverityClassifier` (rules-derived severity wins; LLM disagreements logged + counted).
- All namespace chains fan-in to `CycleAggregateExecutor`, which applies `IAnomalyDedupeStore` suppression and publishes the final `AnomalyReport[]` batch through `IAnomalyHandoffSink`.
- Cycle-level telemetry: wall-clock cap (20s default), tool-iteration cap (8), and metrics via `System.Diagnostics.Metrics` (`Meter("InfraGate.Observer", "1.0")`).

## Important Contracts

- **AnomalyKind** — four-bucket enum: `PodUnhealthy`, `DeploymentUnavailable`, `ServiceNoEndpoints`, `WarningEvent`. Sub-classification lives in `Annotations["PodCondition"]`.
- **Severity** — three-level rules-derived classification: `High` (service zero endpoints, deployment totally unavailable, all pods in critical condition), `Medium` (partial deployment unavailability, single pod in critical condition with healthy siblings, sustained warning events), `Low` (single pod restart, pending pod within grace, one-off warning events).
- **AnomalyStatus** — `Active | Resolved` (v1). No `Persistent` or `Flapping` statuses.
- **AnomalyId** — stable 12-char hex hash of `(Kind, ApiVersion, Kind, Namespace, Name)`. Stable across cycles for the same underlying anomaly.
- **Tool whitelist** — the Observer only calls the gateway's 8 read-only tools (`get_allowed_namespaces`, `get_k8s_status`, `get_k8s_events`, `get_k8s_pods`, `describe_k8s_resource`, `get_k8s_deployments`, `get_k8s_services`, `get_k8s_endpoints`). Any call to a mutation tool throws `InvalidOperationException` before HTTP.
- The Observer is a peer MCP client (not embedded in the gateway). It never calls mutation tools, never produces Plan Envelopes or Approval Grants, and never writes through `IApprovalAuditPublisher`.
- Optional HTTP handoff posts `AnomalyHandoffBatch` payloads to the Remediation Planner's `/handoff/anomalies` endpoint.
- LLM provider is configurable via env vars. Supported provider: `openrouter` (OpenAI-compatible). `INFRA_GATE_OBSERVER_LLM_API_KEY` is required. Configuring `ANTHROPIC` as the provider throws `InvalidOperationException` at startup — use OpenRouter instead.
- `POST /observe-now` triggers a synchronous on-demand cycle (30s HTTP timeout) without resetting the scheduled tick. Concurrent calls serialise via a shared semaphore.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md).

## Audit Stream

`ObserverAuditOutbox` writes a tamper-evident hash chain to `observer.audit_outbox` (ADR-0020). The Observer's Audit Stream is independent of the Approval Authority's Audit Spine — it does not produce Audit Spine events and does not reference `InfraGate.Approvals` (enforced by architecture tests in `tests/InfraGate.Observer.Tests/UnitTests/Architecture/`).

Five audit-worthy events are defined in `ObserverAuditEvents`:

| Event name | When emitted |
|---|---|
| `anomaly.detected` | Anomaly passes the suppression-window check and will be reported |
| `anomaly.suppressed` | Anomaly is observed but suppressed by the Suppression Window |
| `anomaly.resolved` | Anomaly is absent for the resolution threshold; `resolved` report emitted |
| `handoff.published` | Successful POST to the Planner's `/handoff/anomalies` endpoint |
| `handoff.failed` | Non-2xx or exception from the Planner handoff sink |

All emit uses the `AppendAsync(entry, ct)` convenience overload — Observer audit writes are not part of a larger state-mutation transaction.

The `observer` schema is created on startup by `PostgresAuditOutboxMigrationRunner` reading `Migrations/0001-initial-observer-audit.sql`. Connection string: `INFRA_GATE_OBSERVER_AUDIT_CONNECTION_STRING`.

See [InfraGate.AuditOutbox.Postgres README](../InfraGate.AuditOutbox.Postgres/README.md) for the chain-verification SQL recipe and cross-stream forensic query.

## Verification

- Unit tests: `dotnet test tests/InfraGate.Observer.Tests/InfraGate.Observer.Tests.csproj`
- Integration tests (in-process gateway + stubbed LLM): `dotnet test tests/InfraGate.Observer.IntegrationTests/InfraGate.Observer.IntegrationTests.csproj`
- Opt-in end-to-end tests (real gateway + Keycloak + K8s cluster): `INFRA_GATE_RUN_OBSERVER_E2E=1 dotnet test tests/InfraGate.Observer.E2E.Tests/InfraGate.Observer.E2E.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
