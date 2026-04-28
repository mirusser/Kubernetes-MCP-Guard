# InfraGate HTTP Guardrail Gateway

## Summary
Add a separate HTTP MCP gateway in front of the existing stdio Kubernetes MCP server. The gateway becomes the client-facing MCP endpoint, exposes only the known InfraGate Kubernetes tools, forwards valid calls to the existing server, and applies warn+redact prompt-injection guardrails to model-visible request/response text.

This is the right layer because prompt injection risk is mostly in text crossing the model boundary, while Kubernetes enforcement already lives in the downstream server through RBAC, allowed namespaces, supported kinds, exact plan hashing, and human approval.

Reference basis: MCP tool security guidance calls for input validation, output sanitization, access controls, confirmation, and audit logging; MCP trust guidance treats tool behavior/metadata as security-sensitive; OWASP MCP lists tool/context poisoning and audit gaps as MCP risks. Sources: [MCP Tools Security](https://modelcontextprotocol.io/specification/2025-06-18/server/tools), [MCP Security Principles](https://modelcontextprotocol.io/specification/draft), [OWASP MCP Top 10](https://owasp.org/www-project-mcp-top-10/), [OWASP MCP Tool Poisoning](https://owasp.org/www-community/attacks/MCP_Tool_Poisoning).

## Key Changes
- Add `src/InfraGate.McpGateway` as an ASP.NET Core MCP server using `ModelContextProtocol.AspNetCore`.
- Expose Streamable HTTP MCP at `/mcp`, default bind `http://127.0.0.1:3001`.
- Require `Authorization: Bearer <token>` for `/mcp`; token comes from `INFRA_GATE_GATEWAY_BEARER_TOKEN`, and startup fails if it is missing.
- Gateway starts/connects to the existing downstream stdio server with `StdioClientTransport`.
- Configure downstream with `INFRA_GATE_DOWNSTREAM_PROJECT`, defaulting to `src/InfraGate.McpServer/InfraGate.McpServer.csproj` relative to the gateway working directory.
- Gateway inherits existing downstream env vars such as `KUBECONFIG`, `K8S_MCP_APPROVAL_ROOT`, and `K8S_MCP_ALLOWED_NAMESPACES`.

## Guardrail Behavior
- Expose only the current six tools: `get_k8s_status`, `request_apply_manifest`, `request_delete_manifest`, `request_scale_deployment`, `request_restart_deployment`, and `apply_approved_plan`.
- Keep the same tool names, arguments, and annotations so existing clients need only switch from stdio server config to HTTP gateway config.
- Scan all string tool arguments and downstream text responses with a deterministic `PromptInjectionGuard`; no LLM classifier.
- Warning mode does not block calls. It records findings and adds a short guardrail warning to the MCP response.
- Redact model-visible high-risk text:
  - Always replace returned `Manifest: ```yaml ... ```` blocks with a note telling the user to inspect the pending plan file.
  - For JSON responses, recursively replace suspicious string values with `[redacted: prompt-injection-risk]`.
  - For non-JSON text, redact suspicious lines while preserving plan IDs, object refs, hashes, file paths, and approval commands.
- Use case-insensitive pattern categories for common prompt-injection instructions such as ignoring previous instructions, revealing system/developer prompts, calling tools, exfiltrating secrets, or treating embedded content as higher-priority instructions.
- Write JSONL guardrail audit events under `INFRA_GATE_GUARD_AUDIT_ROOT`, default `.mcp-guardrails/audit.jsonl`, including timestamp, tool name, direction, action, finding categories, and plan ID when extractable. Do not log raw flagged payload text.

## Test Plan
- Unit test `PromptInjectionGuard` with clean text, manifest-like injected ConfigMap data, risky labels/annotations, and ordinary Kubernetes strings that should pass.
- Unit test redaction preserves operational fields: `PlanId`, `Pending file`, `Approval file`, `Plan hash`, object refs, and approval command.
- Gateway tests with a fake downstream client verify: known tools forward calls, suspicious input adds warnings, manifest blocks are redacted, JSON output values are redacted, and clean outputs are unchanged.
- HTTP tests verify missing/wrong bearer token is rejected and valid token reaches MCP routing.
- Existing downstream tests remain unchanged.
- Verification commands:
  - `dotnet build InfraGate.slnx`
  - `dotnet test InfraGate.slnx`
  - Optional integration: run gateway, call `request_apply_manifest` through HTTP MCP, confirm the response is redacted while the pending plan file still contains the exact manifest for human approval.

## Assumptions
- Prompt injection is treated as a model-boundary risk, not as a Kubernetes authorization mechanism.
- The first version is warn+redact, not block, to avoid false positives breaking normal Kubernetes workflows.
- HTTP gateway is local-first: localhost binding, bearer token auth, no TLS. Remote deployment should sit behind TLS and stronger auth later.
- The downstream `InfraGate.McpServer` remains the source of truth for Kubernetes validation, RBAC, approval hashing, and API execution.
