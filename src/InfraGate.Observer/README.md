# InfraGate.Observer

`InfraGate.Observer` is an LLM-driven, deployable agent that periodically inspects Kubernetes through the MCP gateway's read-only tools and emits structured Anomaly Reports for a future executor to consume.

## Runtime Flow

- `Program.cs` starts an ASP.NET WebApplication on port 3003, registers the MCP HTTP client with OAuth bearer auth, the snapshot fetcher, the observation cycle loop (`IHostedService`), the `/health` endpoint, and the `/observe-now` on-demand trigger.
- `ObservationCycleLoop` ticks on a configurable cadence (default 60s), invoking the full `ObservationCycleRunner`: snapshot fetch, LLM-driven anomaly detection, rules-derived severity classification, deduplication against active anomaly state, and resolution emission for cleared anomalies.
- `ObserverMcpClient` wraps the MCP client SDK, injects OAuth bearer tokens via `ClientCredentialsBearerHandler`, and enforces a client-side read-only tool whitelist before each call.
- `SnapshotFetcher` fetches status, events, pods, deployments, services, and endpoints in parallel for each allowed namespace. Individual tool failures degrade gracefully (partial snapshot with structured warning).
- `HealthEndpoint` exposes `GET /health` returning 200 when the OAuth token is valid, 503 otherwise.
- `ObserveNowEndpoint` exposes `POST /observe-now` for on-demand manual triggering. Invokes one synchronous observation cycle (30s timeout, returns `AnomalyReport[]` on 200, empty array on truncated, 504 on timeout, 500 on errors). Serialises with the background scheduled cycle via a shared semaphore — at most one cycle runs at any time. The background schedule is unaffected by manual triggers.

## Observer Identity

The Observer authenticates with the MCP gateway using OAuth client_credentials flow. It uses the Keycloak client `infra-gate-observer` and the `mcp:tools.readonly` scope. The gateway resolves this to the audit identity `service:observer`.

Only read-only tools are permitted. The client-side whitelist enforces this before any HTTP call:
- `get_allowed_namespaces`
- `get_k8s_status`
- `get_k8s_events`
- `get_k8s_pods`
- `describe_k8s_resource`
- `get_k8s_deployments`
- `get_k8s_services`
- `get_k8s_endpoints`

Attempting any mutation tool (e.g. `request_scale_deployment`, `execute_approved_plan`, `apply_manifest`) throws `InvalidOperationException` before the HTTP request leaves the process.

## Handoff Contract

Anomaly Reports follow the types in `InfraGate.Observer.Contracts`: `AnomalyReport`, `AnomalyHandoffBatch`, `AnomalyKind`, `AnomalyStatus`, `Severity`, `ResourceRef`, `EvidenceItem`, `RemediationHint`, and `IAnomalyHandoffSink`. The Observer publishes to handoff sinks (v1: logging sink always on, JSON file sink opt-in). The executor is a separate service that references only the Contracts project.

## Deduplication and Resolution

The Observer maintains in-memory deduplication state (`ConcurrentDictionary<DedupKey, ActiveAnomalyState>`) behind the `IAnomalyDedupeStore` seam. A `DedupKey` is the tuple `(AnomalyKind, ResourceKind, Namespace, Name)`.

- **Suppression window** (`DedupeSuppressionWindow`, default 5 cycles): When the same anomaly is detected in consecutive cycles within the window, it is suppressed (not re-emitted). This prevents report noise from persistent conditions.
- **Resolution emission** (`DedupeResolutionThreshold`, default 2 cycles): When an active anomaly is absent for the configured number of consecutive cycles, one `AnomalyReport` with `Status = Resolved` and `Severity = Low` is emitted, and the dedupe entry is forgotten.
- **Truncated cycles** produce no reports and do not consume the suppression window or advance the absence counter.
- **Cold start / restart**: On the first cycle after launch, every detected anomaly emits a fresh report. The state is in-memory only — a restart re-emits all currently anomalous resources. Downstream sinks must be idempotent against re-emission (`AnomalyId` is a stable hash of `Kind + ResourceRef`). Persisted dedupe is a v2 candidate.

## Settings

Runtime environment variables, defaults, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md).

## Verification

- Main tests: `dotnet test tests/InfraGate.Observer.Tests/InfraGate.Observer.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
