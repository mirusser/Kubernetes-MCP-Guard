# InfraGate.McpServer

`InfraGate.McpServer` is the stdio MCP server that owns the Kubernetes governance behavior. It exposes MCP tools, validates requested namespaces and manifest kinds, creates approval plans, and applies approved plans through the Kubernetes .NET client.

## Runtime Flow

- `Program.cs` wires the generic host, stdio MCP transport, `K8sMcpOptions`, `ApprovalStore`, `IKubernetes`, and `K8sManager`.
- `K8sTools.cs` is the MCP-facing tool surface. Tool names are external contracts and must stay aligned with `K8sConventions.ToolNames`.
- `K8sManager.*.cs` contains the behavior behind those tools: status reads, request-plan creation, approval elicitation, plan application, rollout waits, and validation helpers.
- `K8sManifestParser.cs` accepts YAML/JSON manifests and allows only `apps/v1 Deployment`, `v1 Service`, and `v1 ConfigMap`.
- `ApprovalStore.cs` persists pending, approved, and applied plan files and writes approval audit events.

## Important Contracts

- Mutations are two-step: create a pending plan, then call `apply_approved_plan`.
- Approval is hash-bound. If a pending plan changes after approval, application is refused.
- Allowed namespaces come from `K8S_MCP_ALLOWED_NAMESPACES`; unsupported namespaces are rejected before Kubernetes API calls.
- Approval storage defaults to `.mcp-approvals` and uses `pending/`, `approved/`, `applied/`, and `audit.jsonl`.
- Do not rename MCP tool methods or tool-name constants without updating clients, tests, and README examples.

## Configuration

- `KUBECONFIG`: optional kubeconfig path. If unset, the Kubernetes client uses default config discovery.
- `K8S_MCP_APPROVAL_ROOT`: optional approval file root.
- `K8S_MCP_ALLOWED_NAMESPACES`: optional comma-separated namespace allow-list. Defaults to `mcp-nginx-demo`.

## Verification

- Main unit tests: `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
- Opt-in Kubernetes integration: `INFRA_GATE_RUN_INTEGRATION=1 dotnet test InfraGate.slnx`
