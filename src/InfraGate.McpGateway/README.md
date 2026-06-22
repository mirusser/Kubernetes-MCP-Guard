# InfraGate.McpGateway

`InfraGate.McpGateway` is the local HTTP MCP boundary, auth/transport/composition layer, tool dispatch, approval orchestration, guardrails, and adapter orchestration that fronts a private stdio domain server. It adds authentication, prompt-injection guardrails, response sanitization, out-of-band plan approval orchestration, and guardrail audit logging. The generic approval lifecycle (challenges, grants, persistence) lives in `InfraGate.Approvals`. Kubernetes-specific plan building and execution gates are delegated to `InfraGate.KubernetesAdapter`.

**Owns:** public MCP boundary, auth, dispatch, approval HTTP endpoints, guardrails, composition

## Runtime Flow

- `Program.cs` bootstraps the pipeline, delegates service registration to `GatewayConfigurationExtensions`, configures the HTTP MCP server at `/mcp` with handler routing, and defines the request middleware chain.
- `McpTransport/Dispatch/IGatewayToolDispatcher.cs` / `McpTransport/GatewayToolDispatcher.cs` dynamically forward downstream ReadOnly tools, hide downstream Destructive tools, expose `request_*` wrappers for human-driven plan creation, own `execute_approved_plan`, `get_plan_status`, and `wait_for_plan_approval`, route `propose_plan`, and call the generic pre-execution gate before adapter execution.
- `Approval/ProposePlan/ProposePlanHandler.cs` creates Operator Approval Policy plans for the autonomous Planner operation menu, generates Approval Access Codes, and attempts the configured operator email notification.
- `McpTransport/Notifications/PlanStatusResourceHandler.cs` exposes the `plan://{planId}/status` MCP resource template, reads the same plan-status JSON contract as `get_plan_status`, and owns explicit `resources/subscribe` / `resources/unsubscribe` routing for approval notifications.
- `Approval/Service/IGatewayApprovalService.cs`, `Approval/Service/GatewayApprovalService.cs`, and `Approval/Service/GatewayApprovalEndpoints.cs` create or reuse short-lived approval URLs, expose the `/approvals/code` Approval Access Code route, and delegate HTML rendering to `InfraGate.ApprovalUi` Razor components; challenge workflows live in `InfraGate.Approvals` behind `IApprovalChallengeWorkflow`. `ApprovalGateResult` separates `Approved`, `ApprovalRequired`, and `Refused` states and carries stable reason codes while preserving the MCP text response.
- `Guardrails/GuardedToolRunner.cs` scans inbound arguments, calls the downstream stdio server, sanitizes risky model-visible output, and writes audit events.
- `McpTransport/Client/DownstreamMcpClient.cs` starts and reuses the downstream `InfraGate.McpServer` process via the Model Context Protocol client.
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

## Important Contracts

- Gateway public read-only tool names and generated `request_*` wrappers must stay stable for clients. Raw downstream Destructive tools are not exposed through the gateway and must only be reached by the domain executor after approval gates pass.
- `propose_plan` is the autonomous Planner entry point. It accepts only the v1 operation allowlist, stamps Operator Approval Policy, and does not change the existing human-driven `request_*` contracts.
- Logs and Events are untrusted Kubernetes output; keep observability reads routed through `GuardedToolRunner` so response sanitization still applies.
- Read-only downstream tool responses are returned as a `model_visible_tool_result` JSON envelope. The top-level fields are Gateway-owned metadata; Kubernetes-derived content is isolated under `untrusted.payload` and must be treated as observation data, not instructions.
- Suspicious input is warned and audited, but still forwarded to the downstream server.
- Suspicious response text and echoed manifest blocks are redacted before returning under the envelope's `untrusted.payload` field.
- Authentication behavior is provided by `InfraGate.McpGateway.Auth`; this project should not duplicate auth rules.
- OAuth access tokens are terminated at the gateway. The downstream stdio server receives tool calls, not bearer tokens.
- The gateway binds Same-Subject Approval challenges to the requester stored in the generic plan envelope. Operator Approval Policy challenges are decided by authenticated users in the configured operator group.
- Approval is browser-based and out-of-band: MCP clients receive an approval URL but cannot submit approval content through MCP.
- Approval Access Codes are one-time routing tokens for Planner-originated plans. They redirect an operator to the existing browser Review Surface and do not authenticate the approver.
- `get_plan_status` is read-only and returns the current approval plan status so MCP clients can poll after `execute_approved_plan` returns `ApprovalRequired`. `wait_for_plan_approval` is also read-only; it waits up to `timeoutSeconds` (default 55, max 300) and returns `timedOut` without applying the plan.
- MCP clients that support resources can subscribe to `plan://{planId}/status`; browser approval sends `notifications/resources/updated` for that URI. Notifications are best-effort and require a stateful client session that surfaces MCP resource notifications.
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
