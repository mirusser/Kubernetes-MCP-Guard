# InfraGate.McpGateway

`InfraGate.McpGateway` is the local HTTP MCP endpoint and generic approval core that fronts a private stdio domain server. It adds authentication, prompt-injection guardrails, response sanitization, out-of-band plan approval, and guardrail audit logging. Kubernetes-specific plan building and execution gates are delegated to `InfraGate.KubernetesAdapter`.

## Runtime Flow

- `Program.cs` configures the HTTP MCP server at `/mcp`, registers auth, approval endpoints, guardrails, the downstream client, and the Kubernetes adapter implementation of the generic plan seams.
- `GatewayToolDispatcher.cs` dynamically forwards downstream ReadOnly tools, hides downstream Destructive tools, exposes `request_*` wrappers for plan creation, and owns `execute_approved_plan`.
- `GatewayApprovalService.cs` and `GatewayApprovalEndpoints.cs` create short-lived approval URLs and render Kubernetes review evidence decoded through `InfraGate.KubernetesAdapter`; `ApprovalChallengeStore` lives in `InfraGate.Approvals`.
- `GuardedToolRunner.cs` scans inbound arguments, calls the downstream stdio server, sanitizes risky model-visible output, and writes audit events.
- `DownstreamMcpClient.cs` starts and reuses the downstream `InfraGate.McpServer` process via the Model Context Protocol client.
- `PromptInjectionGuard*.cs` contains argument scanning, response redaction, operational-line allow-listing, and regex patterns.
- `GuardrailAuditStore.cs` appends JSONL audit entries under the configured guardrail audit root.
- MCP transport and OAuth compliance details for this gateway path are summarized in [MCP-compliance.md](../../docs/MCP-compliance.md).

## Important Contracts

- Gateway public read-only tool names and generated `request_*` wrappers must stay stable for clients. Raw downstream Destructive tools are not exposed through the gateway and must only be reached by the domain executor after approval gates pass.
- Logs and Events are untrusted Kubernetes output; keep observability reads routed through `GuardedToolRunner` so response sanitization still applies.
- Suspicious input is warned and audited, but still forwarded to the downstream server.
- Suspicious response text and echoed manifest blocks are redacted before returning to the MCP client.
- Authentication behavior is provided by `InfraGate.McpGateway.Auth`; this project should not duplicate auth rules.
- OAuth access tokens are terminated at the gateway. The downstream stdio server receives tool calls, not bearer tokens.
- The gateway binds approval challenges to the requester stored in the generic plan envelope; a different authenticated subject must request a fresh plan.
- Approval is browser-based and out-of-band: MCP clients receive an approval URL but cannot submit approval content through MCP.
- Browser approval pages render the stored Kubernetes server-side dry-run status, Intent Digest, Review Digest, and adapter review evidence. Manifest plans require diff evidence; narrow Deployment operations may be dry-run-only.
- Approval challenges are bound to plan id, intent/review digests, requester subject, expiry, and Single-Execution status. The gateway recomputes the plan file's intent and review digests at challenge creation and approval time to detect drift between the stored plan and the challenge bindings. Approved challenges issue Approval Grants consumed by execution.
- Guardrail audit entries must not include bearer tokens or raw credentials.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md). Auth settings are owned by `InfraGate.McpGateway.Auth` and are listed there too.

## Verification

- Main tests: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Opt-in live gateway integration: `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
