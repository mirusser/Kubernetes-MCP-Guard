# InfraGate.McpServer

`InfraGate.McpServer` is the stdio MCP server that owns the Kubernetes governance behavior. It exposes MCP tools, validates requested namespaces and manifest kinds, creates approval plans, and applies approved plans through the Kubernetes .NET client.

## Runtime Flow

- `Program.cs` wires the generic host, stdio MCP transport, `K8sMcpOptions`, `ApprovalStore`, `IKubernetes`, and `K8sManager`.
- `K8sTools.cs` is the MCP-facing tool surface. Tool names are external contracts and must stay aligned with `K8sConventions.ToolNames`.
- `K8sManager.*.cs` contains the behavior behind those tools: status reads, bounded observability and diagnostics reads, request-plan creation, approved-plan application, rollout waits, and validation helpers.
- `K8sManifestParser.cs` accepts YAML/JSON manifests and allows only `apps/v1 Deployment`, `v1 Service`, and `v1 ConfigMap`.
- `ApprovalStore.cs` persists pending, approved, and applied plan files and writes approval audit events.

## Important Contracts

- Mutations are two-step: create a pending plan, then call `apply_approved_plan` after the Gateway writes out-of-band approval.
- Approval is hash-bound. If a pending plan changes after approval, application is refused.
- Observability tools are read-only and bounded. They expose Events, Pod logs, focused summaries, and diagnostics, but not Secret values, ConfigMap values, raw manifests, exec, attach, or port-forward.
- Allowed namespaces come from `K8S_MCP_ALLOWED_NAMESPACES`; unsupported namespaces are rejected before Kubernetes API calls.
- Approval storage defaults to `.mcp-approvals` and uses `pending/`, `approved/`, `applied/`, `challenges/`, and `audit.jsonl`.
- Do not rename MCP tool methods or tool-name constants without updating clients, tests, and README examples.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md).

## Verification

- Main unit tests: `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
- Opt-in Kubernetes integration: `INFRA_GATE_RUN_INTEGRATION=1 dotnet test InfraGate.slnx`
