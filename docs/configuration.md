# Configuration Reference

This is the canonical configuration reference for Kubernetes MCP Guard. Keep runnable examples in setup docs, but keep defaults, descriptions, and production guidance here.

Defaults below come from the current source code and workflows. Paths shown as `<working directory>/...` are resolved from the process working directory.

## Runtime Mode

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `INFRA_GATE_ENVIRONMENT` | All runtime components | No | `Development` when no environment variable is set; Docker images set `Production` | `Production` | InfraGate runtime mode. Valid values are `Development` and `Production`. Overrides `DOTNET_ENVIRONMENT` and `ASPNETCORE_ENVIRONMENT`. | Set `Production` for shared or real deployments. Invalid values fail startup. |
| `DOTNET_ENVIRONMENT` | All runtime components | No | Used only when `INFRA_GATE_ENVIRONMENT` is unset | `Production` | Standard .NET environment fallback. `Development` means development; all other values are treated as production-like. | Prefer `INFRA_GATE_ENVIRONMENT` for explicit InfraGate behavior. |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET components, fallback for server mode parsing | No | Used only when the two variables above are unset | `Production` | ASP.NET Core environment fallback. `Development` means development; all other values are treated as production-like. | Prefer `INFRA_GATE_ENVIRONMENT` for explicit InfraGate behavior. |

## McpServer

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `KUBECONFIG` | `InfraGate.McpServer` | Required in Production unless `K8S_MCP_USE_IN_CLUSTER=true` | Development only: Kubernetes client default discovery | `.kube/mcp-nginx-demo.config` | Optional kubeconfig path used by the Kubernetes client. | Use a least-privilege kubeconfig backed by namespace-scoped RBAC, not an admin kubeconfig. Do not rely on default discovery in Production. |
| `K8S_MCP_USE_IN_CLUSTER` | `InfraGate.McpServer` | Required in Production unless `KUBECONFIG` is set | `false` | `true` | Uses the in-cluster Kubernetes ServiceAccount instead of a kubeconfig. Cannot be combined with `KUBECONFIG`. | Set to `true` only when running inside Kubernetes with a namespace-scoped ServiceAccount and RBAC. |
| `K8S_MCP_APPROVAL_ROOT` | `InfraGate.McpServer`, `InfraGate.McpGateway` | Required in Production | `<working directory>/.mcp-approvals` | `/data/approvals` | Retained for ASP.NET Core Data Protection key storage compatibility. Approval state and audit are now PostgreSQL-backed (see `InfraGate:Approval:Postgres:ConnectionString` in generated appsettings JSON). | Use a durable, protected absolute path. Production refuses temp paths, default dev paths, and group/other-writable existing directories. The ASP.NET Core Data Protection key ring persists at this path and must survive container restarts. |
| `K8S_MCP_ALLOWED_NAMESPACES` | `InfraGate.McpServer` | Required in Production | `mcp-nginx-demo` | `mcp-nginx-demo,staging` | Comma-separated namespace allow-list. Requests outside this set are rejected before Kubernetes API calls. | Keep this aligned with Kubernetes RBAC; do not use it as a substitute for RBAC. Production requires an explicit non-empty value. |
| `K8S_MCP_LOG_PATH` | `InfraGate.McpServer` | No | Unset | `/tmp/mcp-server.log` | Optional file path for MCP server debug logs (structured JSON via Serilog). When set, all log output is written to this file in JSON format in addition to the stderr transport. No file is created when this variable is unset. | Use for diagnosing connectivity issues in containerised deployments; disable in steady-state production to avoid unbounded log growth. |

## InfraGate.Observer

The Anomaly Observer listens on port `3003` by default and polls the MCP gateway on a configurable cadence. All variables use the `INFRA_GATE_OBSERVER_*` prefix.

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `ASPNETCORE_URLS` | `InfraGate.Observer` | No | `http://127.0.0.1:3003` | `http://0.0.0.0:3003` | ASP.NET Core bind URL for the Observer HTTP server (`/health` endpoint). | Bind intentionally; put behind TLS if exposing outside loopback. |
| `INFRA_GATE_OBSERVER_CYCLE_INTERVAL_SECONDS` | `InfraGate.Observer` | No | `60` | `60` | Observation cycle cadence in seconds. Bounds: 10–3600. | Below 10s hammers gateway + LLM; above 1h is not observation. |
| `INFRA_GATE_OBSERVER_WALL_CLOCK_CAP_SECONDS` | `InfraGate.Observer` | No | `20` | `20` | Per-cycle wall-clock cap. Truncated cycles emit no reports. | Keep below cadence so cycles never overlap; below `/observe-now` HTTP timeout (30s). |
| `INFRA_GATE_OBSERVER_MAX_TOOL_ITERATIONS` | `InfraGate.Observer` | No | `8` | `8` | Maximum LLM tool-call iterations per cycle. | Bounds agentic loops independent of clock time. |
| `INFRA_GATE_OBSERVER_GATEWAY_BASE_URL` | `InfraGate.Observer` | Yes | None | `http://127.0.0.1:3001/mcp` | Base URL of the MCP gateway HTTP endpoint. | Must be reachable from the Observer process. |
| `INFRA_GATE_OBSERVER_ALLOWED_NAMESPACES` | `InfraGate.Observer` | No | Unset | `mcp-nginx-demo` | Comma-separated namespace allow-list for snapshot fetching. | Align with gateway namespace allowlist. |
| `INFRA_GATE_OBSERVER_LLM_PROVIDER` | `InfraGate.Observer` | No | `anthropic` | `anthropic` | LLM provider for anomaly detection. Supported: `anthropic`. Others (`openai`, `google`, `azure`, `ollama`) are reserved for future implementation. | Use a provider supported by `Microsoft.Extensions.AI`. |
| `INFRA_GATE_OBSERVER_LLM_MODEL` | `InfraGate.Observer` | No | `claude-sonnet-4-6` | `claude-sonnet-4-6` | LLM model name. Applies when `INFRA_GATE_OBSERVER_LLM_PROVIDER` is set. | Model selection affects token cost and detection quality. |
| `INFRA_GATE_OBSERVER_LLM_API_KEY` | `InfraGate.Observer` | No | Unset | (secret) | LLM provider API key. Never logged. Required when a provider is configured. | Use a secret manager in production; env var is development-only. |
| `INFRA_GATE_OBSERVER_CLIENT_ID` | `InfraGate.Observer` | No | `infra-gate-observer` | `infra-gate-observer` | OAuth client ID for the Observer service account. | Register this client with the IdP and grant `mcp:tools.readonly` scope. |
| `INFRA_GATE_OBSERVER_CLIENT_SECRET` | `InfraGate.Observer` | Yes | None | (secret) | OAuth client secret for client_credentials flow. | Use a secret manager in production; env var is development-only. |
| `INFRA_GATE_OBSERVER_OAUTH_AUTHORITY` | `InfraGate.Observer` | Yes | None | `http://keycloak:8080/realms/infra-gate` | OAuth token endpoint authority. | Match the gateway's issuer. |
| `INFRA_GATE_OBSERVER_OAUTH_SCOPE` | `InfraGate.Observer` | No | `mcp:tools.readonly` | `mcp:tools.readonly` | OAuth scope requested by the Observer. | Must include `mcp:tools.readonly` for gateway access. |
| `INFRA_GATE_OBSERVER_DEDUPE_SUPPRESSION_WINDOW` | `InfraGate.Observer` | No | `5` | `5` | Number of cycles within which repeated detection of the same anomaly is suppressed (deduplication window). Bounds: 1–30. | Lower values increase report noise; higher values delay re-emission of persistent anomalies. |
| `INFRA_GATE_OBSERVER_DEDUPE_RESOLUTION_THRESHOLD` | `InfraGate.Observer` | No | `2` | `2` | Number of consecutive cycles an anomaly must be absent before emitting a `Resolved` report. Bounds: 1–10. | Lower values clear anomalies faster; higher values prevent transient flapping. |
| `INFRA_GATE_OBSERVER_FILE_SINK_ROOT` | `InfraGate.Observer` | No | Unset | `/data/observer/findings` | Directory for the opt-in JSON file handoff sink. When set and non-empty, each cycle writes a `{cycleId}.json` file atomically. | Use a durable bind-mount; operator owns cleanup and rotation. |
| `INFRA_GATE_OBSERVER_PLANNER_HANDOFF_URL` | `InfraGate.Observer` | No | Unset | `http://planner:3004/handoff/anomalies` | Optional HTTP handoff target for publishing `AnomalyHandoffBatch` payloads to the Remediation Planner. When unset, Observer output is limited to logging and the optional JSON file sink. | Required for the autonomous remediation loop. Use a service-local HTTPS route or trusted private network path outside local development. |

### On-Demand Observation Trigger

The Observer exposes `POST /observe-now` for manual on-demand cycle triggering:
- Returns `200` with `AnomalyReport[]` on success, `504` on 30-second timeout, `500` on errors.
- Serialises with the background scheduled cycle via a shared semaphore — at most one cycle runs at any time.
- Waits up to 2 seconds for an in-flight scheduled cycle to complete before starting.
- Background schedule is unaffected and continues on its normal cadence.
- The 30-second timeout and 2-second slack window are **hardcoded** and not configurable via environment variables.

## InfraGate.Planner

The Remediation Planner listens on port `3004` by default, accepts `POST /handoff/anomalies`, and proposes approval-pending plans through the gateway. Its v1 operation menu is `restart_deployment` and `scale_deployment`. Direct runtime configuration is under `InfraGate:Planner`; generated env files and local Compose use the `INFRA_GATE_PLANNER_*` prefix.

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `ASPNETCORE_URLS` / `INFRA_GATE_PLANNER_ASPNETCORE_URLS` | `InfraGate.Planner`, Compose interpolation | No | Runtime default `http://localhost:3004`; Compose default `http://0.0.0.0:3004` | `http://0.0.0.0:3004` | ASP.NET Core bind URL for the Planner HTTP server (`/health`, `/handoff/anomalies`). The prefixed variable is generated for Compose and mapped to `ASPNETCORE_URLS` inside the container. | Bind intentionally; put behind TLS or a private service network when exposed outside loopback. |
| `INFRA_GATE_PLANNER_GATEWAY_BASE_URL` | `InfraGate.Planner` | Yes | None | `http://gateway:3001/mcp` | MCP gateway HTTP endpoint used for read-only inspection and `propose_plan`. | Must be reachable only from the Planner service account path. Prefer HTTPS outside local Compose. |
| `INFRA_GATE_PLANNER_EXECUTOR_HANDOFF_URL` | `InfraGate.Planner` | No | Unset | `http://executor:3005/handoff/proposals` | Optional HTTP handoff target for publishing `RemediationProposalBatch` payloads to the Executor. When unset, proposals are logged and optionally written to the JSON file sink but are not pushed to an Executor. | Required for the autonomous remediation loop. Keep the route service-local and authenticated. |
| `INFRA_GATE_PLANNER_ANOMALY_WALL_CLOCK_CAP_SECONDS` | `InfraGate.Planner` | No | `30` | `30` | Per-anomaly decision cap covering LLM reasoning and plan proposal. Bounds: 5–120. | Keep bounded to control LLM cost and avoid tying up batch processing. |
| `INFRA_GATE_PLANNER_BATCH_WALL_CLOCK_CAP_SECONDS` | `InfraGate.Planner` | No | `300` | `300` | Per-batch processing cap. Proposals completed before the cap are still published. Bounds: 30–900. | Keep below operational alert windows; high values can delay later batches. |
| `INFRA_GATE_PLANNER_MAX_TOOL_ITERATIONS` | `InfraGate.Planner` | No | `4` | `4` | Maximum read-only inspection tool calls the LLM may request per anomaly. Bounds: 1–10. | Lower values reduce cost and blast radius; raise only with measured need. |
| `INFRA_GATE_PLANNER_LLM_PROVIDER` | `InfraGate.Planner` | No | `anthropic` | `anthropic` | LLM provider for remediation decisions. The current implemented provider is Anthropic; other parsed provider names are future wiring points. | Use a provider explicitly supported by the deployed binary. |
| `INFRA_GATE_PLANNER_LLM_MODEL` | `InfraGate.Planner` | No | `claude-sonnet-4-6` | `claude-sonnet-4-6` | LLM model name used by the Planner chat client. | Model choice affects cost and remediation quality. Validate prompt behavior before changing. |
| `INFRA_GATE_PLANNER_LLM_API_KEY` | `InfraGate.Planner` | Yes for Anthropic | None | (secret) | API key for the configured LLM provider. | Use a secret manager in production; env var is development-only. Never commit generated files containing this value. |
| `INFRA_GATE_PLANNER_CLIENT_ID` | `InfraGate.Planner` | No | `infra-gate-planner` | `infra-gate-planner` | OAuth client id for the Planner service account. | Register a dedicated confidential client and grant only `mcp:tools.propose` plus `mcp:tools.readonly`. |
| `INFRA_GATE_PLANNER_CLIENT_SECRET` | `InfraGate.Planner` | Yes | None | (secret) | OAuth client secret for Planner client_credentials flow. | Use a secret manager in production; rotate independently from Observer and Executor secrets. |
| `INFRA_GATE_PLANNER_OAUTH_AUTHORITY` | `InfraGate.Planner` | Yes | None | `http://keycloak:8080/realms/infra-gate` | OAuth/OIDC authority used for client_credentials token acquisition and inbound Observer JWT validation. | Use the same issuer trusted by the gateway; use HTTPS outside local development. |
| `INFRA_GATE_PLANNER_OAUTH_SCOPE` | `InfraGate.Planner` | No | `mcp:tools.propose mcp:tools.readonly` | `mcp:tools.propose mcp:tools.readonly` | OAuth scopes requested for Planner gateway calls. | Do not grant execution scopes to the Planner identity. |
| `INFRA_GATE_PLANNER_FILE_SINK_ROOT` | `InfraGate.Planner` | No | Unset | `/data/planner/proposals` | Directory for the opt-in JSON file proposal sink. | Use durable storage only when proposal capture is operationally required; define retention. |
| `INFRA_GATE_PLANNER_TOKEN_ENDPOINT` | Run Profiles / Compose | No | Profile value when emitted | `http://keycloak:8080/realms/infra-gate/protocol/openid-connect/token` | Generated profile value for local Compose parity. The current Planner runtime discovers tokens from `INFRA_GATE_PLANNER_OAUTH_AUTHORITY`. | Keep aligned with the authority and IdP realm; do not treat as a direct runtime override. |
| `INFRA_GATE_PLANNER_IMAGE` | Compose | No | `infragate-planner` | `ghcr.io/example/infragate-planner` | Planner container image used by local Compose. | Pin release tags for shared environments. |
| `INFRA_GATE_PLANNER_BIND_ADDRESS` | Compose | No | `127.0.0.1` | `127.0.0.1` | Host bind address for the Planner container port. | Keep loopback unless a reverse proxy or private network boundary is configured. |
| `INFRA_GATE_PLANNER_BIND_PORT` | Compose | No | `3004` | `3004` | Host port mapped to the Planner container. | Avoid exposing publicly; this is an internal handoff endpoint. |
| `INFRA_GATE_PLANNER_HOST_PATH` | Compose / Run Profiles | No | `./.mcp-remediation/proposals` in local Compose | `./.mcp-remediation/proposals` | Host path mounted to the Planner JSON proposal sink directory. | Use protected durable storage when enabled; otherwise leave the file sink unset. |

## InfraGate.Executor

The Remediation Executor listens on port `3005` by default, accepts `POST /handoff/proposals`, waits for approval with `wait_for_plan_approval`, and calls `execute_approved_plan` only after approval. Direct runtime configuration is under `InfraGate:Executor`; generated env files and local Compose use the `INFRA_GATE_EXECUTOR_*` prefix.

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `ASPNETCORE_URLS` / `INFRA_GATE_EXECUTOR_ASPNETCORE_URLS` | `InfraGate.Executor`, Compose interpolation | No | Runtime default `http://localhost:3005`; Compose default `http://0.0.0.0:3005` | `http://0.0.0.0:3005` | ASP.NET Core bind URL for the Executor HTTP server (`/health`, `/handoff/proposals`). The prefixed variable is generated for Compose and mapped to `ASPNETCORE_URLS` inside the container. | Bind intentionally; keep the handoff endpoint service-local. |
| `INFRA_GATE_EXECUTOR_GATEWAY_BASE_URL` | `InfraGate.Executor` | Yes | None | `http://gateway:3001/mcp` | MCP gateway HTTP endpoint used for `wait_for_plan_approval` and `execute_approved_plan`. | Must be reachable only from the Executor service account path. Prefer HTTPS outside local Compose. |
| `INFRA_GATE_EXECUTOR_CONCURRENCY_CAP` | `InfraGate.Executor` | No | `64` | `64` | Maximum in-flight watched plans. Batches exceeding available slots are rejected with `429`. Bounds: 1–256. | Size for gateway connection capacity and expected approval volume. |
| `INFRA_GATE_EXECUTOR_WATCH_TIMEOUT_SECONDS` | `InfraGate.Executor` | No | `900` | `900` | Wall-clock timeout for each plan watch. Bounds: 60–3600. | Keep aligned with challenge TTL and operator response expectations. |
| `INFRA_GATE_EXECUTOR_CLIENT_ID` | `InfraGate.Executor` | No | `infra-gate-executor` | `infra-gate-executor` | OAuth client id for the Executor service account. | Register a dedicated confidential client and grant only `mcp:tools.execute`. |
| `INFRA_GATE_EXECUTOR_CLIENT_SECRET` | `InfraGate.Executor` | Yes | None | (secret) | OAuth client secret for Executor client_credentials flow. | Use a secret manager in production; rotate independently from Planner secrets. |
| `INFRA_GATE_EXECUTOR_OAUTH_AUTHORITY` | `InfraGate.Executor` | Yes | None | `http://keycloak:8080/realms/infra-gate` | OAuth/OIDC authority used for client_credentials token acquisition and inbound Planner JWT validation. | Use the same issuer trusted by the gateway; use HTTPS outside local development. |
| `INFRA_GATE_EXECUTOR_OAUTH_SCOPE` | `InfraGate.Executor` | No | `mcp:tools.execute` | `mcp:tools.execute` | OAuth scope requested for Executor gateway calls. | Do not grant proposal or read-only scopes to the Executor identity. |
| `INFRA_GATE_EXECUTOR_TOKEN_ENDPOINT` | Run Profiles / Compose | No | Profile value when emitted | `http://keycloak:8080/realms/infra-gate/protocol/openid-connect/token` | Generated profile value for local Compose parity. The current Executor runtime discovers tokens from `INFRA_GATE_EXECUTOR_OAUTH_AUTHORITY`. | Keep aligned with the authority and IdP realm; do not treat as a direct runtime override. |
| `INFRA_GATE_EXECUTOR_IMAGE` | Compose | No | `infragate-executor` | `ghcr.io/example/infragate-executor` | Executor container image used by local Compose. | Pin release tags for shared environments. |
| `INFRA_GATE_EXECUTOR_BIND_ADDRESS` | Compose | No | `127.0.0.1` | `127.0.0.1` | Host bind address for the Executor container port. | Keep loopback unless a reverse proxy or private network boundary is configured. |
| `INFRA_GATE_EXECUTOR_BIND_PORT` | Compose | No | `3005` | `3005` | Host port mapped to the Executor container. | Avoid exposing publicly; this is an internal handoff endpoint. |
| `INFRA_GATE_EXECUTOR_HOST_PATH` | Run Profiles | No | Unset | `/var/lib/infra-gate/executor` | Optional generated host-path value reserved for Executor deployments. The current local Compose file does not mount an Executor data directory. | Leave unset unless a deployment profile introduces durable Executor-owned files. |

## McpGateway

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `ASPNETCORE_URLS` | `InfraGate.McpGateway` | No | `http://127.0.0.1:3001` when no URL config is set | `http://0.0.0.0:3001` | ASP.NET Core bind URL for the HTTP MCP gateway and browser approval endpoints. | Bind intentionally and put the gateway behind TLS in production. |
| `INFRA_GATE_DOWNSTREAM_PROJECT` | `InfraGate.McpGateway` | No | `<working directory>/src/InfraGate.McpServer/InfraGate.McpServer.csproj` | `/repo/src/InfraGate.McpServer/InfraGate.McpServer.csproj` | Downstream stdio MCP server project used when no published assembly is configured. | Prefer `INFRA_GATE_DOWNSTREAM_ASSEMBLY` for immutable container/runtime deployments. |
| `INFRA_GATE_DOWNSTREAM_ASSEMBLY` | `InfraGate.McpGateway` | No | Unset | `/app/server/InfraGate.McpServer.dll` | Published downstream server assembly. When set, the gateway starts `dotnet <assembly>`. | Use a known published assembly from the same release as the gateway image. |
| `INFRA_GATE_GUARD_AUDIT_ROOT` | `InfraGate.McpGateway` | Required in Production | `<working directory>/.mcp-guardrails` | `/data/guardrails` | Guardrail JSONL audit output root. | Store on protected durable absolute storage and monitor retention. Production refuses temp paths, default dev paths, and group/other-writable existing directories. |
| `K8S_MCP_APPROVAL_ROOT` | `InfraGate.McpGateway`, `InfraGate.McpServer` | Required in Production | `<working directory>/.mcp-approvals` | `/data/approvals` | Retained for ASP.NET Core Data Protection key storage only. Approval state and audit are PostgreSQL-backed. | Use a durable, protected absolute path for Data Protection keys. Production requires an explicit durable path. |
| `INFRA_GATE_APPROVAL_BASE_URL` | `InfraGate.McpGateway` | Required in Production | Request-derived URL, or `http://127.0.0.1:3001` when no request is available | `https://gateway.example.com` | Public base URL used when returning approval links to the MCP client. | Set explicitly to the external HTTPS URL users open in a browser. Production refuses missing, HTTP, or loopback values. |
| `INFRA_GATE_APPROVAL_CHALLENGE_TTL_SECONDS` | `InfraGate.McpGateway` | No | `900` | `900` | Approval URL lifetime in seconds. | Keep short enough to limit stale approvals while allowing human review. |
| `INFRA_GATE_OPERATOR_GROUP` | `InfraGate.McpGateway` | No | `kubernetes-operators` | `kubernetes-operators` | Group claim required for approving Operator Approval Policy plans. | Keep aligned with the IdP group path and grant membership only to real operators. |
| `INFRA_GATE_OPERATOR_EMAIL` | `InfraGate.McpGateway` | Required for approval email delivery | Unset; local Compose default `operators@example.local` | `operators@example.com` | Destination email address for Approval Access Code notifications created by `propose_plan`. Plans are still created if email delivery is not configured. | Use a monitored operator group mailbox or distribution list. |
| `INFRA_GATE_GATEWAY_SMTP_HOST` | `InfraGate.McpGateway` | Required for approval email delivery | Unset; local Compose default `mailpit` | `smtp.example.com` | SMTP host used by the gateway approval email sender. | Use an authenticated, monitored SMTP relay; local Mailpit is development-only. |
| `INFRA_GATE_GATEWAY_SMTP_PORT` | `InfraGate.McpGateway` | No | `25` when SMTP is configured; local Compose default `1025` | `587` | SMTP port. Bounds: 1–65535. | Prefer TLS-capable relay ports according to your mail infrastructure. |
| `INFRA_GATE_GATEWAY_SMTP_FROM` | `InfraGate.McpGateway` | Required for approval email delivery | Unset; local Compose default `infragate@example.local` | `infragate@example.com` | Sender address for Approval Access Code notifications. | Use a verified sender domain; monitor bounces. |
| `INFRA_GATE_GATEWAY_SMTP_USER` | `InfraGate.McpGateway` | No | Unset | `smtp-user` | Optional SMTP username. | Use secret storage and least-privilege SMTP credentials. |
| `INFRA_GATE_GATEWAY_SMTP_PASSWORD` | `InfraGate.McpGateway` | No | Unset | (secret) | Optional SMTP password. | Use secret storage; never commit generated env files containing this value. |
| `INFRA_GATE_GATEWAY_SMTP_ENABLE_SSL` | `InfraGate.McpGateway` | No | `true` when SMTP is configured; local Compose default `false` for Mailpit | `true` | Enables SMTP TLS/STARTTLS on the approval email client. | Keep enabled for production SMTP relays; disable only for local Mailpit or trusted development-only relays. |

## McpGateway.Auth

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `INFRA_GATE_OAUTH_AUTHORITY` | `InfraGate.McpGateway.Auth` | Yes | None | `http://127.0.0.1:3010/realms/infra-gate` | OAuth/OIDC issuer URL used for JWT validation and protected-resource metadata. | Use a real HTTPS issuer in production; local Keycloak is development-only. Production refuses HTTP or loopback values. |
| `INFRA_GATE_OAUTH_METADATA_ADDRESS` | `InfraGate.McpGateway.Auth` | No | Unset | `http://keycloak:8080/realms/infra-gate/.well-known/openid-configuration` | Optional internal OIDC discovery URL when the gateway reaches the issuer through a different network address than clients use. | Use only when network topology requires it; issuer claims must still match `INFRA_GATE_OAUTH_AUTHORITY`. Production refuses HTTP or loopback values when set. |
| `INFRA_GATE_OAUTH_RESOURCE` | `InfraGate.McpGateway.Auth` | No | `http://127.0.0.1:3001/mcp` | `https://gateway.example.com/mcp` | Expected JWT audience/resource and MCP protected resource value. | Set to the externally stable HTTPS MCP resource URI and configure the IdP to issue it as an audience. Production refuses the localhost default. |
| `INFRA_GATE_OAUTH_SCOPE` | `InfraGate.McpGateway.Auth` | No | `mcp:tools` | `mcp:tools` | Required scope checked on MCP requests and requested by the approval UI OAuth flow. | Keep scopes aligned with the IdP and client configuration. |
| `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA` | `InfraGate.McpGateway.Auth` | No | `true` | `false` | Controls the HTTPS requirement for OIDC discovery metadata. | `false` is acceptable only for local HTTP development issuers such as the Keycloak demo. Production refuses `false`. |
| `INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID` | `InfraGate.McpGateway.Auth` | No | `infra-gate-approval-ui` | `infra-gate-approval-ui` | Public OAuth client id used by the browser approval UI. | Register this as a public PKCE client with the production IdP. |
| `INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT` | `InfraGate.McpGateway.Auth` | No | `${INFRA_GATE_OAUTH_AUTHORITY}/authorize` | `https://issuer.example.com/realms/demo/protocol/openid-connect/auth` | Browser-visible authorization endpoint override for approval login. | Set when the provider does not expose `/authorize` under the authority root. Production requires the effective endpoint to be HTTPS and non-loopback. |
| `INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT` | `InfraGate.McpGateway.Auth` | No | `${INFRA_GATE_OAUTH_AUTHORITY}/token` | `https://issuer.example.com/realms/demo/protocol/openid-connect/token` | Gateway-visible token endpoint override for approval login. | Use an endpoint reachable by the gateway; do not point browser-only hosts here. Production requires the effective endpoint to be HTTPS and non-loopback. |
| `INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH` | `InfraGate.McpGateway.Auth` | No | `/approvals/oauth/callback` | `/approvals/oauth/callback` | Local callback path used by the gateway approval UI OAuth flow. | Register the full external redirect URI with the IdP. |

## Local Keycloak Demo

The local OAuth Compose path (`deploy/local-oauth`) and the development compose path import `deploy/keycloak/infra-gate-realm.json` into `quay.io/keycloak/keycloak:26.6.1`. The matching test realm lives at `tests/TestData/keycloak/infra-gate-realm.json`; clients, scopes, and DCR policy should remain aligned unless a test intentionally documents a difference.

| Realm item | Purpose |
| --- | --- |
| `mcp-client` | Public authorization-code + PKCE S256 client for MCP clients. Direct password grant is disabled. |
| `mcp-smoke-client` | Public direct-grant client used by CI/smoke tests to acquire non-browser tokens. |
| `mcp-client-limited` | Public direct-grant client with valid audience but no `mcp:tools`, used for 403 insufficient-scope coverage. |
| `infra-gate-approval-ui` | Public authorization-code + PKCE S256 client for browser approvals. |
| `infra-gate-planner` | Confidential client_credentials client for the Remediation Planner. |
| `infra-gate-executor` | Confidential client_credentials client for the Remediation Executor. |
| `mcp:tools` | Client scope with the audience mapper for `http://127.0.0.1:3001/mcp` and a subject mapper suitable for local tests. |
| `mcp:tools.propose` | Planner scope mapped to the `propose_plan` gateway tool. |
| `mcp:tools.execute` | Executor scope mapped to `wait_for_plan_approval` and `execute_approved_plan`. |
| `kubernetes-operators` | Demo operator group used by Operator Approval Policy checks. |

Anonymous OIDC Dynamic Client Registration is enabled only in the local/demo realm. Registration policies restrict redirect URIs to trusted loopback hosts, limit allowed client scopes, cap anonymous client count, and disable full-scope registration. Production deployments should use pre-registered or admin-managed clients instead.

Keycloak does not currently process RFC 8707 `resource` indicators for MCP as the gateway ultimately needs, so the local realm binds `aud` through the `mcp:tools` audience mapper. The gateway still validates issuer, signature, lifetime, audience, and scope. InfraGate should revisit issuer-side RFC 8707 resource-indicator coverage when Keycloak supports the needed MCP flow cleanly.

## Run Profiles

`deploy/run-profiles.yaml` is the canonical source of truth for all runnable environment configuration. It defines named profiles (tiers) that compile into `.env` files for Docker Compose interpolation and appsettings JSON for .NET runtime binding.

**CLI commands** (from repo root):

```bash
# List available profiles
dotnet run --project src/InfraGate.RunProfiles -- list

# Validate all profiles parse correctly (run in CI before tests)
dotnet run --project src/InfraGate.RunProfiles -- validate

# Generate an env file from a profile
dotnet run --project src/InfraGate.RunProfiles -- generate <profile-name> \
  [--format env|appsettings] \
  [--set section.field=value ...] \
  [--output path/to/output.env] \
  [--force]

# Example: generate for a local Compose run with absolute host paths
REPO_ROOT="$(pwd)"
dotnet run --project src/InfraGate.RunProfiles -- generate local-compose \
  --set "host.kubeconfigHostPath=${REPO_ROOT}/.kube/mcp-nginx-demo.compose.config" \
  --set "host.approvalHostPath=${REPO_ROOT}/.mcp-approvals" \
  --set "host.guardAuditHostPath=${REPO_ROOT}/.mcp-guardrails" \
  --set "host.dataProtectionHostPath=${REPO_ROOT}/.mcp-dataprotection-keys" \
  --set "host.configHostPath=${REPO_ROOT}/deploy/generated/local-compose.appsettings.json" \
  --output deploy/generated/local-compose.env
```

**Generated file transport**: `deploy/generated/*.env` and `deploy/generated/*.appsettings.json` files are gitignored. The committed no-SDK examples are `deploy/local-oauth/release.env.example` and `deploy/local-oauth/release.appsettings.json`, regenerated from the `smoke-release` profile.

**Section inheritance**: profiles only inherit `defaults:` values for sections they explicitly declare. A profile must include `gateway: {}` to receive gateway defaults; omitting the key produces no gateway vars. This keeps test profiles free of Compose-only configuration.

**`--set` overrides**: use `section.field=value` syntax. Section names match the YAML keys (`gateway`, `identityProvider`, `approvalAuthority`, `genericApprovalCore`, `host`, `observer`, `planner`, `executor`). Overrides are applied after merging defaults. Use them for host-path fields when a run needs paths different from the profile defaults; `scripts/generate-env.sh` uses them to emit absolute repository-root paths for local Compose runs.

**`--force`**: by default, `generate` refuses to overwrite an existing file. Pass `--force` when the generator must write to a system path (e.g., `/etc/infra-gate/development.env` from `scripts/setup-development-deploy.sh`).

See `src/InfraGate.RunProfiles/README.md` for the full schema reference and profile catalogue.

## CI, Release, And Scripts

| Variable | Component | Required | Default | Example | Description | Production guidance |
| --- | --- | :---: | --- | --- | --- | --- |
| `DOCKERHUB_USERNAME` | GitHub Actions variable | Required only when pushing images | None | `mirusser` | Docker Hub username used by `package-docker.yml`. | Store as `vars.DOCKERHUB_USERNAME`; do not hard-code in workflows. |
| `DOCKERHUB_NAMESPACE` | GitHub Actions variable | Required only when pushing images | `local` for non-push build metadata | `mirusser` | Docker Hub namespace used in published image tags. | Store as `vars.DOCKERHUB_NAMESPACE`; verify repositories are public if public pulls are intended. |
| `DOCKERHUB_TOKEN` | GitHub Actions secret | Required only when pushing images | None | `dckr_pat_...` | Docker Hub token used by `package-docker.yml`. | Store as `secrets.DOCKERHUB_TOKEN` and rotate if exposed. |
| `GITHUB_TOKEN` | GitHub Actions secret | Required for GHCR publishing | GitHub-provided token | `${{ secrets.GITHUB_TOKEN }}` | Token used by GitHub Actions to publish GHCR images. | Keep workflow permissions narrow; current package workflow grants `packages: write`. |
| `DEPLOY_PATH` | GitHub Actions environment variable | No | `/opt/infra-gate` for development | `/opt/infra-gate` | Directory where the development workflow copies `compose.yaml` before running Docker Compose on the self-hosted runner. | Use a path owned by the runner user and prepared by `scripts/setup-development-deploy.sh`. |
| `RUNNER_USER` | `scripts/setup-development-deploy.sh` | No | `SUDO_USER` | `actions-runner` | Local user that runs the development self-hosted GitHub Actions runner. The setup script uses it to make the deploy path and env file readable/writable by the runner and to generate the demo kubeconfig with the user's Kubernetes context. | Set explicitly when running the setup script from a different sudo user than the runner account. |
| `KEYCLOAK_PORT` | `scripts/setup-development-deploy.sh`, `deploy/compose/keycloak.yaml` | No | `3010` | `3010` | Host port for the local Keycloak development issuer. | Keep the default unless the Keycloak realm audience/redirects are updated and re-imported. |
| `KEYCLOAK_BIND_ADDRESS` | `scripts/setup-development-deploy.sh`, `deploy/compose/keycloak.yaml` | No | `0.0.0.0` from setup script; `127.0.0.1` in direct Compose usage | `0.0.0.0` | Host bind address for the local Keycloak development issuer. The setup script binds broadly so the gateway container can reach Keycloak through the Docker bridge while browser URLs stay on loopback. | Use only for local development; do not expose this demo issuer on shared networks. |
| `GATEWAY_PORT` | `scripts/setup-development-deploy.sh`, `deploy/compose/keycloak.yaml` | No | `3001` | `3001` | Host port used for the local gateway URLs and Keycloak audience/redirect alignment. | The bundled Keycloak realm expects `http://127.0.0.1:3001/mcp`; update and re-import the realm before changing it. |
| `SONAR_TOKEN` | GitHub Actions secret | Required for Sonar workflow | None | `sqp_...` | SonarCloud token used by `sonar.yml`. | Store as `secrets.SONAR_TOKEN`. |
| `SONAR_PROJECT_KEY` | GitHub Actions variable | Required for Sonar workflow | None | `mirusser_Kubernetes-MCP-Guard` | SonarCloud project key passed to the scanner. | Store as `vars.SONAR_PROJECT_KEY`. |
| `SONAR_ORGANIZATION` | GitHub Actions variable | Required for Sonar workflow | None | `mirusser` | SonarCloud organization passed to the scanner. | Store as `vars.SONAR_ORGANIZATION`. |

### SonarCloud Report Artifact

After every `sonar.yml` run (push, pull request, and `workflow_dispatch`), `sonar.yml` fetches the full analysis results from the SonarCloud Web API after the scan completes and uploads a `sonarcloud-report` artifact retained for 7 days. Download it from the **Artifacts** section of the GitHub Actions run page.

The artifact contains a single `sonarcloud-report.json` file with five top-level keys:

| Key | Contents |
| --- | --- |
| `metadata` | `generatedAt` (ISO-8601), `projectKey`, `sonarcloudUrl`, `branch` |
| `qualityGate` | Full `/api/qualitygates/project_status` response including per-condition results |
| `measures` | Full `/api/measures/component` response with values for bugs, vulnerabilities, code smells, coverage, duplication, ratings, complexity, and NCLOC |
| `issues` | All open/confirmed issues with `rule`, `severity`, `type`, `component` (file path), `line`, `message`, `effort`, and full `ruleDescription.descriptionSections` (root/compliant/remediation content) |
| `hotspots` | All security hotspots with `securityCategory`, `vulnerabilityProbability`, `component`, `line`, and `message` |

To process this report and produce a structured remediation plan, use the `.agents/skills/sonarcloud-remediation/SKILL.md` agent skill. It chains through `repo-onboarding`, `code-standards`, `planning-and-task-breakdown`, `writing-tests`, and `verify-readme-docs` to produce ordered, convention-compliant fix tasks.
| `push_images` | Docker workflow dispatch input | No | `false` | `true` | Manual workflow input that requests image publishing. | Use only for intentional publishing; release tags publish automatically. |
| `PUSH_IMAGES` | Docker workflow environment | Derived | `true` on `dev`, `v*` tag pushes, or manual `push_images=true`; otherwise `false` | `true` | Internal `package-docker.yml` flag controlling registry login and image push. | Do not set directly outside the workflow unless testing the workflow logic. |
| `INFRA_GATE_RUN_INTEGRATION` | Test environment | No | Unset, live server integration test returns early | `1` | Enables live Kubernetes integration coverage for `InfraGate.McpServer.Tests`. | Run only against a disposable/demo namespace with least-privilege kubeconfig. |
| `INFRA_GATE_RUN_GATEWAY_INTEGRATION` | Test environment | No | Unset, live gateway integration test returns early | `1` | Enables live Kubernetes integration coverage for `InfraGate.McpGateway.Tests`. | Run only against a disposable/demo namespace with least-privilege kubeconfig. |
| `Category=Keycloak` (xUnit trait) | Test environment | No | Keycloak tests excluded from default runs | `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"` | Enables real-OIDC integration tests against a Testcontainers Keycloak container. | Requires Docker. The shared test realm under `tests/TestData/keycloak/` is loaded as the realm config. Separate CI workflow at `keycloak-tests.yml`. |
| `KUBECONFIG` | Tests and local scripts | No | Tests fall back to `.kube/mcp-nginx-demo.config` when unset | `.kube/mcp-nginx-demo.config` | Kubeconfig used by live integration tests and runtime examples. | Use a generated service-account kubeconfig, not the admin kubeconfig. |
| `TAG` | Docker Compose and smoke tests | No for development/demo; required by production Compose | `dev` in development deploy, `latest` in demo release Compose | `v0.1.0` | Image tag used by `deploy/compose/*.yaml`, `deploy/local-oauth/compose.release.yaml`, and `scripts/smoke-test-release.sh`. | Use the raw release tag when running production Compose manually; avoid floating tags for production. |
| `INFRA_GATE_GATEWAY_IMAGE` | Docker Compose deploys | No | `ghcr.io/mirusser/kubernetes-mcp-guard-gateway` | `mirusser/kubernetes-mcp-guard-gateway` | Gateway image repository used by deployment Compose files. | Override only when deploying from Docker Hub or a private mirror. |
| `INFRA_GATE_CONFIG_PATH` | `InfraGate.McpGateway`, `InfraGate.McpServer` | No | Unset | `/app/config/appsettings.InfraGate.json` | Container path to the generated InfraGate appsettings JSON file. Both gateway and server load this file via `IConfiguration` when set, using JSON values as defaults before environment variable overrides. When unset, standard .NET configuration providers apply. | Set from a profile-generated appsettings file mounted at a known container path. |
| `INFRA_GATE_CONFIG_HOST_PATH` | Docker Compose deploys | No | `/etc/infra-gate/<environment>.appsettings.json` | `./release.appsettings.json` | Host path to the generated appsettings JSON file, mounted read-only into the gateway container at the path named by `INFRA_GATE_CONFIG_PATH`. | Use a profile-generated file kept alongside the env file. Readable by the Compose runtime user. |
| `INFRA_GATE_KUBECONFIG_HOST_PATH` | Docker Compose deploys | No | `/etc/infra-gate/<environment>.kubeconfig` | `/etc/infra-gate/production.kubeconfig` | Host kubeconfig path mounted read-only into the gateway container as `/run/kube/infra-gate.config`. | Use a least-privilege kubeconfig; keep permissions tight on the host. |
| `INFRA_GATE_APPROVAL_HOST_PATH` | Docker Compose deploys | No | `/var/lib/infra-gate/<environment>/approvals` | `/var/lib/infra-gate/production/approvals` | Host path mounted into the container as `/data/approvals`. | Use durable storage that is not group- or other-writable. |
| `INFRA_GATE_GUARD_AUDIT_HOST_PATH` | Docker Compose deploys | No | `/var/lib/infra-gate/<environment>/guardrails` | `/var/lib/infra-gate/production/guardrails` | Host path mounted into the container as `/data/guardrails`. | Use durable storage with retention/monitoring appropriate for audit logs. |
| `INFRA_GATE_DATA_PROTECTION_HOST_PATH` | Docker Compose deploys | No | `/var/lib/infra-gate/<environment>/dataprotection-keys` | `/var/lib/infra-gate/production/dataprotection-keys` | Host path mounted into the container as `/data/dataprotection-keys`. Persists the ASP.NET Core Data Protection key ring so antiforgery tokens and authentication cookies survive container restarts. | Must be persisted across restarts; key loss invalidates all in-flight approval form tokens and OAuth cookies. |
| `INFRA_GATE_BIND_ADDRESS` | Docker Compose deploys | No | `127.0.0.1` | `127.0.0.1` | Host bind address for the gateway container port. | Keep loopback when TLS terminates at a host reverse proxy. |
| `INFRA_GATE_BIND_PORT` | Docker Compose deploys | No | `3001` | `3001` | Host port mapped to the gateway container. The deploy workflow probes this port after `docker compose up`. | Match the local reverse proxy upstream. |
| `KUBECONFIG_PATH` | `scripts/smoke-test-release.sh` | No | `<repo>/.kube/mcp-nginx-demo.compose.config` | `.kube/mcp-nginx-demo.compose.config` | Kubeconfig path mounted by the published-image smoke test. | Use a disposable demo kubeconfig created with `./scripts/create-demo-kubeconfig.sh --compose`. |
