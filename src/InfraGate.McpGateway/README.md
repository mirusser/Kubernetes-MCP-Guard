# InfraGate.McpGateway

`InfraGate.McpGateway` is the local HTTP MCP endpoint that fronts the stdio Kubernetes MCP server. It adds authentication, prompt-injection guardrails, response sanitization, and guardrail audit logging while preserving the same Kubernetes tool names and arguments exposed by `InfraGate.McpServer`.

## Runtime Flow

- `Program.cs` configures the HTTP MCP server at `/mcp`, registers auth, guardrails, the downstream client, and the gateway tool facade.
- `K8sGatewayTools.cs` exposes the same MCP tools as the stdio server and delegates to `GuardedToolRunner`.
- `GuardedToolRunner.cs` scans inbound arguments, calls the downstream stdio server, sanitizes risky model-visible output, and writes audit events.
- `DownstreamMcpClient.cs` starts and reuses the downstream `InfraGate.McpServer` process via the Model Context Protocol client.
- `PromptInjectionGuard*.cs` contains argument scanning, response redaction, operational-line allow-listing, and regex patterns.
- `GuardrailAuditStore.cs` appends JSONL audit entries under the configured guardrail audit root.
- MCP transport and OAuth compliance details for this gateway path are summarized in [MCP-COMPLIANCE.md](../../docs/MCP-COMPLIANCE.md).

## Important Contracts

- Gateway tool names and argument names must stay compatible with `InfraGate.McpServer`.
- Suspicious input is warned and audited, but still forwarded to the downstream server.
- Suspicious response text and echoed manifest blocks are redacted before returning to the MCP client.
- Authentication behavior is provided by `InfraGate.McpGateway.Auth`; this project should not duplicate auth rules.
- OAuth access tokens are terminated at the gateway. The downstream stdio server receives tool calls, not bearer tokens.
- Guardrail audit entries must not include bearer tokens or raw credentials.

## Configuration

- `INFRA_GATE_DOWNSTREAM_PROJECT`: optional path to the downstream `InfraGate.McpServer.csproj`.
- `INFRA_GATE_GUARD_AUDIT_ROOT`: optional audit output root. Defaults to `.mcp-guardrails`.
- `ASPNETCORE_URLS`: optional HTTP binding. Defaults to `http://127.0.0.1:3001` when unset.
- Auth settings come from `InfraGate.McpGateway.Auth`.

## Verification

- Main tests: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
