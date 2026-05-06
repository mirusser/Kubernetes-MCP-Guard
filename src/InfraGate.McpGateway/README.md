# InfraGate.McpGateway

`InfraGate.McpGateway` is the local HTTP MCP endpoint that fronts the stdio Kubernetes MCP server. It adds authentication, prompt-injection guardrails, response sanitization, out-of-band plan approval, and guardrail audit logging while preserving the same Kubernetes tool names and arguments exposed by `InfraGate.McpServer`.

## Runtime Flow

- `Program.cs` configures the HTTP MCP server at `/mcp`, registers auth, approval endpoints, guardrails, the downstream client, and the gateway tool facade.
- `K8sGatewayTools.cs` exposes the same MCP tools as the stdio server and delegates to `GuardedToolRunner`.
- `GatewayApprovalService.cs` and `GatewayApprovalEndpoints.cs` create short-lived approval URLs and render pending plans from the shared approval store.
- `GuardedToolRunner.cs` scans inbound arguments, calls the downstream stdio server, sanitizes risky model-visible output, and writes audit events.
- `DownstreamMcpClient.cs` starts and reuses the downstream `InfraGate.McpServer` process via the Model Context Protocol client.
- `PromptInjectionGuard*.cs` contains argument scanning, response redaction, operational-line allow-listing, and regex patterns.
- `GuardrailAuditStore.cs` appends JSONL audit entries under the configured guardrail audit root.
- MCP transport and OAuth compliance details for this gateway path are summarized in [MCP-compliance.md](../../docs/MCP-compliance.md).

## Important Contracts

- Gateway tool names and argument names must stay compatible with `InfraGate.McpServer`.
- Logs and Events are untrusted Kubernetes output; keep observability reads routed through `GuardedToolRunner` so response sanitization still applies.
- Suspicious input is warned and audited, but still forwarded to the downstream server.
- Suspicious response text and echoed manifest blocks are redacted before returning to the MCP client.
- Authentication behavior is provided by `InfraGate.McpGateway.Auth`; this project should not duplicate auth rules.
- OAuth access tokens are terminated at the gateway. The downstream stdio server receives tool calls, not bearer tokens.
- Approval is browser-based and out-of-band: MCP clients receive an approval URL but cannot submit approval content through MCP.
- Approval challenges are bound to plan id, plan hash, requester subject, expiry, and single-use status.
- Guardrail audit entries must not include bearer tokens or raw credentials.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md). Auth settings are owned by `InfraGate.McpGateway.Auth` and are listed there too.

## Verification

- Main tests: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Opt-in live gateway integration: `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
