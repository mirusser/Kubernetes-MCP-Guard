# InfraGate.McpServer

`InfraGate.McpServer` is the stdio MCP server that owns the Kubernetes governance behavior. It exposes MCP tools, validates requested namespaces and manifest kinds, dry-run-validates approval plans, and applies approved plans through the Kubernetes .NET client.

## Runtime Flow

- `Program.cs` wires the generic host, stdio MCP transport, `K8SMcpOptions`, `ApprovalStore`, `InfraGate.KubernetesAdapter`, `IKubernetes`, and `K8sManager`.
- `K8sTools.cs` is the MCP-facing tool surface. Tool names are external contracts and must stay aligned with `K8sConventions.ToolNames`.
- `K8sManager.*.cs` contains the behavior behind those tools: status reads, bounded observability and diagnostics reads, request-plan creation, approved-plan application, rollout waits, and validation helpers.
- `K8sManifestParser.cs` accepts YAML/JSON manifests and allows only `apps/v1 Deployment`, `v1 Service`, and `v1 ConfigMap`.
- The server uses `InfraGate.Approvals` for persistent generic approval envelope storage and audit writing.

## Important Contracts

- Mutations are two-step: create a dry-run-validated pending plan, then call `apply_approved_plan` after the Gateway writes out-of-band approval.
- Direct stdio mutation request tools require `requesterSubject`; the HTTP Gateway injects requester metadata automatically from the authenticated OAuth subject.
- The server repeats Kubernetes `dryRun=All` immediately before applying an approved plan; failure blocks the real write.
- Approval is digest-bound through an Approval Grant. If a pending plan changes after approval, the grant no longer matches and application is refused.
- Observability tools are read-only and bounded. They expose Events, Pod logs, focused summaries, and diagnostics, but not Secret values, ConfigMap values, raw manifests, exec, attach, or port-forward.
- Allowed namespaces come from `K8S_MCP_ALLOWED_NAMESPACES`; unsupported namespaces are rejected before Kubernetes API calls.
- Approval storage defaults to `.mcp-approvals` and uses `pending/`, `grants/`, `applied/`, `challenges/`, and `audit.jsonl`; legacy `approved/*.sha256` files are ignored and do not authorize execution.
- Do not rename MCP tool methods or tool-name constants without updating clients, tests, and README examples.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md).

## Verification

- Main unit tests: `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
- Opt-in Kubernetes integration: `INFRA_GATE_RUN_INTEGRATION=1 dotnet test InfraGate.slnx`
