# InfraGate.Observer

`InfraGate.Observer` is an LLM-driven anomaly detection agent that periodically inspects Kubernetes through the MCP gateway's read-only tools and emits structured **Anomaly Reports**. It runs as an ASP.NET `WebApplication` alongside the gateway and authenticates via OAuth client_credentials with the `mcp:tools.readonly` scope.

## Runtime Flow

- `Program.cs` wires Serilog, `InfraGate.RuntimeSafety` mode detection, `InfraGate.ClientCredentials` OAuth token acquisition, and the `Microsoft.Extensions.AI` `IChatClient` for the chosen LLM provider.
- `ObservationCycleLoop` (an `IHostedService`) ticks on a configurable cadence (default 60s) and orchestrates one **Observation Cycle** per tick.
- Each cycle calls `ISnapshotFetcher` to collect a deterministic baseline (`SnapshotDocument`) from the gateway's read-only tools, then sends it to the LLM with the system prompt from `Prompts/ObserverSystemPrompt.md`.
- The LLM proposes anomalies with bounded follow-up tool calls (max 8 per cycle). Proposed anomalies pass through `ISeverityClassifier` — rules-derived severity is the source of truth; LLM disagreements are logged + counted but do not change emitted `Severity`.
- `IAnomalyDedupeStore` suppresses repeat reports within a suppression window (default 5 cycles) and emits `Resolved` reports after an anomaly is absent for a resolution threshold (default 2 cycles).
- Final `AnomalyReport[]` batches are published through `IAnomalyHandoffSink` (logging sink always on; JSON file sink opt-in).
- Cycle-level telemetry: wall-clock cap (20s default), tool-iteration cap (8), and metrics via `System.Diagnostics.Metrics` (`Meter("InfraGate.Observer", "1.0")`).

## Important Contracts

- **AnomalyKind** — four-bucket enum: `PodUnhealthy`, `DeploymentUnavailable`, `ServiceNoEndpoints`, `WarningEvent`. Sub-classification lives in `Annotations["PodCondition"]`.
- **Severity** — three-level rules-derived classification: `High` (service zero endpoints, deployment totally unavailable, all pods in critical condition), `Medium` (partial deployment unavailability, single pod in critical condition with healthy siblings, sustained warning events), `Low` (single pod restart, pending pod within grace, one-off warning events).
- **AnomalyStatus** — `Active | Resolved` (v1). No `Persistent` or `Flapping` statuses.
- **AnomalyId** — stable 12-char hex hash of `(Kind, ApiVersion, Kind, Namespace, Name)`. Stable across cycles for the same underlying anomaly.
- **Tool whitelist** — the Observer only calls the gateway's 8 read-only tools (`get_allowed_namespaces`, `get_k8s_status`, `get_k8s_events`, `get_k8s_pods`, `describe_k8s_resource`, `get_k8s_deployments`, `get_k8s_services`, `get_k8s_endpoints`). Any call to a mutation tool throws `InvalidOperationException` before HTTP.
- The Observer is a peer MCP client (not embedded in the gateway). It never calls mutation tools, never produces Plan Envelopes or Approval Grants, and never writes through `IApprovalAuditPublisher`.
- LLM provider is configurable via env vars; default is Anthropic (`claude-sonnet-4-6`). `INFRA_GATE_OBSERVER_LLM_API_KEY` is required when the LLM phase is active.
- `POST /observe-now` triggers a synchronous on-demand cycle (30s HTTP timeout) without resetting the scheduled tick. Concurrent calls serialise via a shared semaphore.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md).

## Verification

- Unit tests: `dotnet test tests/InfraGate.Observer.Tests/InfraGate.Observer.Tests.csproj`
- Integration tests (in-process gateway + stubbed LLM): `dotnet test tests/InfraGate.Observer.IntegrationTests/InfraGate.Observer.IntegrationTests.csproj`
- Opt-in end-to-end tests (real gateway + Keycloak + K8s cluster): `INFRA_GATE_RUN_OBSERVER_E2E=1 dotnet test tests/InfraGate.Observer.E2E.Tests/InfraGate.Observer.E2E.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
