# InfraGate.McpGateway

`InfraGate.McpGateway` is the local HTTP MCP endpoint that fronts the stdio Kubernetes MCP server. It adds authentication, prompt-injection guardrails, response sanitization, out-of-band plan approval, and guardrail audit logging while preserving the same Kubernetes tool names and arguments exposed by `InfraGate.McpServer`.

## Runtime Flow

- `Program.cs` configures the HTTP MCP server at `/mcp`, registers auth, approval endpoints, guardrails, the downstream client, and the gateway tool facade.
- `K8sGatewayTools.cs` exposes the same public MCP tools as before, injects requester metadata for downstream mutation-plan creation, and delegates to `GuardedToolRunner`.
- `GatewayApprovalService.cs` and `GatewayApprovalEndpoints.cs` create short-lived approval URLs and render Kubernetes review evidence decoded through `InfraGate.KubernetesAdapter`; `ApprovalChallengeStore` lives in `InfraGate.Approvals`.
- `GuardedToolRunner.cs` scans inbound arguments, calls the downstream stdio server, sanitizes risky model-visible output, and writes audit events.
- `DownstreamMcpClient.cs` starts and reuses the downstream `InfraGate.McpServer` process via the Model Context Protocol client.
- `PromptInjectionGuard*.cs` contains argument scanning, response redaction, operational-line allow-listing, and regex patterns.
- `GuardrailAuditStore.cs` appends JSONL audit entries under the configured guardrail audit root.
- MCP transport and OAuth compliance details for this gateway path are summarized in [MCP-compliance.md](../../docs/MCP-compliance.md).

## Important Contracts

- Gateway public tool names and argument names must stay stable for clients. Downstream stdio request tools additionally receive requester metadata injected by the gateway.
- Logs and Events are untrusted Kubernetes output; keep observability reads routed through `GuardedToolRunner` so response sanitization still applies.
- Suspicious input is warned and audited, but still forwarded to the downstream server.
- Suspicious response text and echoed manifest blocks are redacted before returning to the MCP client.
- Authentication behavior is provided by `InfraGate.McpGateway.Auth`; this project should not duplicate auth rules.
- OAuth access tokens are terminated at the gateway. The downstream stdio server receives tool calls, not bearer tokens.
- The gateway binds approval challenges to the requester stored in the generic plan envelope; a different authenticated subject must request a fresh plan.
- Approval is browser-based and out-of-band: MCP clients receive an approval URL but cannot submit approval content through MCP.
- Browser approval pages render the stored Kubernetes server-side dry-run status and refuse legacy pending plans without envelope payloads, dry-run data, or diff data.
- Approval challenges are bound to plan id, plan hash, requester subject, expiry, and single-use status.
- Guardrail audit entries must not include bearer tokens or raw credentials.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md). Auth settings are owned by `InfraGate.McpGateway.Auth` and are listed there too.

## Verification

- Main tests: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Opt-in live gateway integration: `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
