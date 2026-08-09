# InfraGate.McpGateway

`InfraGate.McpGateway` is the local HTTP MCP boundary, auth/transport/composition layer, tool dispatch, approval orchestration, guardrails, and adapter orchestration that fronts a private stdio domain server. It adds authentication, prompt-injection guardrails, response sanitization, out-of-band plan approval orchestration, and guardrail audit logging. The generic approval lifecycle (challenges, grants, persistence) lives in `InfraGate.Approvals`. Kubernetes-specific plan building and execution gates are delegated to `InfraGate.KubernetesAdapter`.

**Owns:** public MCP boundary, auth, dispatch, approval HTTP endpoints, guardrails, composition

## Runtime Flow

- `Program.cs` bootstraps the pipeline, delegates service registration to `GatewayConfigurationExtensions`, configures the HTTP MCP server at `/mcp` with handler routing, and defines the request middleware chain.
- `McpTransport/Dispatch/IGatewayToolDispatcher.cs` / `McpTransport/GatewayToolDispatcher.cs` dynamically forward downstream ReadOnly tools, hide downstream Destructive tools, expose `request_*` wrappers for human-driven plan creation, own `execute_approved_plan`, `get_plan_status`, and `wait_for_plan_approval`, route `propose_plan`, and call the generic pre-execution gate before adapter execution.
- `Approval/ProposePlan/ProposePlanHandler.cs` creates Operator Approval Policy plans for the autonomous Planner operation menu, generates Approval Access Codes, and attempts the configured operator email notification.
- `McpTransport/Notifications/PlanStatusResourceHandler.cs` exposes the `plan://{planId}/status` MCP resource template and reads the same plan-status JSON contract as `get_plan_status`; `PlanStatusSubscriptionsListenHandler.cs` owns MCP 2026-07-28 `subscriptions/listen` routing for approval notifications.
- `Approval/Service/IGatewayApprovalService.cs`, `Approval/Service/GatewayApprovalService.cs`, and `Approval/Service/GatewayApprovalEndpoints.cs` create or reuse short-lived approval URLs, expose the `/approvals/code` Approval Access Code route, and delegate HTML rendering to `InfraGate.ApprovalUi` Razor components; challenge workflows live in `InfraGate.Approvals` behind `IApprovalChallengeWorkflow`. `ApprovalGateResult` separates `Approved`, `ApprovalRequired`, and `Refused` states and carries stable reason codes while preserving the MCP text response.
- `Guardrails/GuardedToolRunner.cs` scans inbound arguments, calls the downstream stdio server, sanitizes risky model-visible output, and writes audit events.
- `McpTransport/Client/DownstreamMcpClient.cs` starts and reuses the downstream `InfraGate.McpServer` process via the Model Context Protocol client. It is generalized over a `DownstreamProcessDescriptor` so the same client class also drives a second, optional, read-only-only downstream: the upstream `containers/kubernetes-mcp-server` Go binary, offering a broader (arbitrary resource kind) inspection surface than the primary's narrow Deployment/Service/ConfigMap tools. `GatewayToolDispatcher` merges `tools/list` and read-only-tool routing across both sources (via a small internal `readOnlySources` collection), but only ever calls `GetDestructiveAsync()` and generates `request_*` mutation wrappers for the primary — the secondary is architecturally incapable of reaching the approval/mutation path. It is off by default; see the [developer runbook](../../docs/devs-readme.md#optional-secondary-read-only-kubernetes-mcp-downstream) for local enablement, the [configuration reference](../../docs/configuration.md#mcpgateway) for settings, and [ADR-0033](../../docs/adr/0033-kubernetes-mcp-server-readonly-secondary-downstream.md) for the decision.
- `Guardrails/Scanning/PromptInjectionGuard*.cs` contains argument scanning, response redaction, operational-line allow-listing, and regex patterns.
- `Guardrails/Audit/GuardrailAuditStore.cs` appends JSONL audit entries under the configured guardrail audit root.
- MCP transport and OAuth compliance details for this gateway path are summarized in [MCP-compliance.md](../../docs/MCP-compliance.md).

## Security Controls (Priority Order)

The controls below are ordered by importance. The downstream service token (in-progress) is intentionally last; a stolen bearer token must not bypass the controls above it.

1. **Trusted launch** — production starts the downstream server from a configured built artifact (`InfraGate__Gateway__DownstreamAssembly=/app/server/InfraGate.McpServer.dll`), not `dotnet run --project`. The `dotnet run --project` fallback is development-only and must not be used in production images. In production the gateway also verifies the SHA-256 hash of that assembly against `InfraGate__Gateway__DownstreamAssemblyHash` at startup and refuses to start if the binary has been tampered with. See [docs/configuration.md](../../docs/configuration.md) for the environment variable reference and hash-computation examples.
2. **Containment** — the downstream subprocess receives only an explicit allowlist of environment variables (`McpGatewayConventions.DownstreamProcess.AllowedEnvironmentVariables`), not the full gateway environment. Gateway-only secrets such as `InfraGate__DownstreamAuth__GatewayClientSecret` are intentionally excluded.
3. **Human approval** — destructive downstream tools are reachable only through the gateway's approval-bound execution path; the MCP client cannot trigger them directly.
4. **Per-action authorization** — request and execution checks use trusted requester identity from the gateway JWT; downstream service-token validation is not a substitute for these checks.
5. **Downstream service token** (defense-in-depth) — proves gateway service identity for audit and forward-compatibility. Not the primary boundary.

### Secondary downstream (kubernetes-mcp-server) trust boundary

The optional secondary downstream (see Runtime Flow above) is an untrusted diagnostic-read adapter. It runs as the dedicated, namespace-scoped `infra-gate-mcp-view` identity through a kubeconfig that is never shared with the mutation-capable primary. Its Role grants only the reads required by the approved tools, including `get` on `pods/log`; it grants no Secret access or mutation verbs.

The stock Go binary speaks plain MCP stdio and does not participate in InfraGate's service-token `_meta` convention, so `KubernetesMcpServerProcessOptions.AuthRequired` is a `const false` with no configurable override. It instead relies on trusted launch and explicit environment isolation: inherited variables are disabled, then only `PATH`/`HOME`/`TMPDIR`/`TMP`/`TEMP` plus the dedicated `KUBECONFIG` are supplied. Defense in depth is provided by viewer RBAC, a fixed generated TOML profile, a validated single-context viewer kubeconfig, an exact Gateway namespace/source policy, output sanitization/bounds, and dispatcher routing that never creates secondary mutation wrappers.

The exact approved tool set is `pods_list_in_namespace`, `pods_get`, and `pods_log`. `events_list` remains disabled because v0.0.66 cannot bound it server-side. `resources_get`, `resources_list`, cluster-wide reads, implicit or wildcard namespaces, multi-cluster operation, and all mutations fail closed. The Gateway authorizes by immutable source identity and exact tool name; downstream `ReadOnlyHint` annotations are metadata, not authority.

Every approved call must name one of the configured namespaces and may use only the reviewed upstream arguments. `pods_log` additionally requires `tail` from 0 through 200. The complete serialized model-visible envelope is rejected above 256 KiB (measured as UTF-8 bytes), and the rejection is audited without retaining response content.

Checkpoint B currently filters the secondary catalog by the exact approved names and applies request/response policy on every call. Later plan checkpoints add schema/collision validation, atomic per-generation snapshots, restart supervision, and degraded health reporting. Any future upstream mutation use requires real-cluster evidence parity, explicit human approval, and a separate ADR and implementation plan; [`InfraGate.McpServer` remains the only mutation path](../../docs/adr/0021-mcpserver-local-dto-copies-over-shared-contracts.md).

## Important Contracts

- Gateway public read-only tool names and generated `request_*` wrappers must stay stable for clients. Raw downstream Destructive tools are not exposed through the gateway and must only be reached by the domain executor after approval gates pass.
- The optional secondary (kubernetes-mcp-server) contributes only approved entries from its three-tool catalog to `tools/list`; it never contributes to the Destructive tool set or `request_*` wrapper generation. Catalog isolation and degraded failure handling remain later hardening checkpoints.
- `propose_plan` is the autonomous Planner entry point. It accepts only the v1 operation allowlist, stamps Operator Approval Policy, and does not change the existing human-driven `request_*` contracts.
- Logs and all other Kubernetes-derived text are untrusted output; keep observability reads routed through `GuardedToolRunner` so response sanitization still applies.
- Read-only downstream tool responses are returned as a `model_visible_tool_result` JSON envelope. The top-level fields are Gateway-owned metadata; Kubernetes-derived content is isolated under `untrusted.payload` and must be treated as observation data, not instructions.
- Suspicious input is warned and audited, but still forwarded to the downstream server.
- Suspicious response text, echoed manifest blocks, and common secret patterns (bearer tokens, API keys, passwords, private keys, etc.) are redacted before returning under the envelope's `untrusted.payload` field.
- Authentication behavior is provided by `InfraGate.McpGateway.Auth`; this project should not duplicate auth rules.
- OAuth access tokens are terminated at the gateway. The downstream stdio server receives tool calls, not bearer tokens.
- The gateway binds Same-Subject Approval challenges to the requester stored in the generic plan envelope. Operator Approval Policy challenges are decided by authenticated users in the configured operator group.
- Approval is browser-based and out-of-band: MCP clients receive an approval URL but cannot submit approval content through MCP.
- Approval Access Codes are one-time routing tokens for Planner-originated plans. They redirect an operator to the existing browser Review Surface and do not authenticate the approver.
- `get_plan_status` is read-only and returns the current approval plan status so MCP clients can poll after `execute_approved_plan` returns `ApprovalRequired`. `wait_for_plan_approval` is also read-only; it waits up to `timeoutSeconds` (default 55, max 300) and returns `timedOut` without applying the plan.
- MCP 2026-07-28 clients can include `plan://{planId}/status` in a held-open `subscriptions/listen` request; browser approval sends a subscription-tagged `notifications/resources/updated` for that URI. Notifications are best-effort, and clients can use `get_plan_status` or `wait_for_plan_approval` when they do not maintain a subscription stream.
- Browser approval pages render the stored Kubernetes server-side dry-run status, Intent Digest, Review Digest, and adapter review evidence. Manifest plans require diff evidence; narrow Deployment operations may be dry-run-only.
- Browser approval pages expose semantic `data-section`, `data-field`, and `data-action` attributes for tests and tooling; visible copy remains presentation text.
- Approval challenges are bound to plan id, intent/review digests, requester subject, expiry, and Single-Execution status. The gateway recomputes the plan file hash and digest bindings at challenge creation and approval time to detect drift between the stored plan and the challenge bindings. Repeated execution requests reuse a matching still-pending challenge URL. Approved challenges issue Approval Grants consumed by the generic pre-execution gate.
- Approval state and audit are persisted in PostgreSQL. The connection string is configured via `InfraGate:Approval:Postgres:ConnectionString`, emitted by run profiles as `InfraGate__Approval__Postgres__ConnectionString` in the generated env file. The gateway validates schema compatibility and applies pending migrations on startup when `InfraGate__Approval__Postgres__RunMigrationsOnStartup=true`.
- Guardrail audit entries must not include bearer tokens or raw credentials.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md). Auth settings are owned by `InfraGate.McpGateway.Auth` and are listed there too.

## Verification

- Main tests: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Opt-in live gateway integration: `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
