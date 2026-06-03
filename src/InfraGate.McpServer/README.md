# InfraGate.McpServer

`InfraGate.McpServer` is the private stdio MCP server that owns direct Kubernetes reads, evidence collection, and raw execution tools. It validates requested namespaces and manifest kinds, runs Kubernetes dry-runs and diffs, and performs raw mutations through the Kubernetes .NET client when called by the gateway's Kubernetes domain adapter.

**Owns:** private Kubernetes MCP tool surface / raw Kubernetes interaction only

## Runtime Flow

- `Program.cs` wires the generic host, stdio MCP transport, `KubernetesMcpOptions`, `IKubernetes`, and `KubernetesManager`.
- `KubernetesTools.cs` is the MCP-facing tool surface. Tool names are external contracts and must stay aligned with `KubernetesConventions.ToolNames`.
- `KubernetesManager.*.cs` contains the behavior behind those tools: status reads, bounded observability and diagnostics reads, evidence dry-runs, manifest diffs, raw execution, and validation helpers.
- `KubernetesManifestParser.cs` accepts YAML/JSON manifests and allows only `apps/v1 Deployment`, `v1 Service`, and `v1 ConfigMap`.
- The server uses Kubernetes adapter evidence and policy records, but it does not create, approve, or apply approval plans.

## Important Contracts

- The server is plan-unaware. It exposes read-only evidence tools (`dry_run_*`, `diff_manifest`, `diff_deployment`, `check_live_drift`) and raw Destructive execution tools (`apply_manifest`, `delete_manifest`, `scale_deployment`, `restart_deployment`, `set_deployment_image`).
- Plan creation, digest binding, approval grants, pre-execution gates, and applied markers live in the gateway plus `InfraGate.Approvals` and `InfraGate.KubernetesAdapter`.
- Observability tools are read-only and bounded. They expose Events, Pod logs, focused summaries, and diagnostics, but not Secret values, ConfigMap values, raw manifests, exec, attach, or port-forward.
- Allowed namespaces come from `InfraGate__Kubernetes__AllowedNamespaces__0`; unsupported namespaces are rejected before Kubernetes API calls.
- Do not rename MCP tool methods or tool-name constants without updating clients, tests, and README examples.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md).

## Verification

- Main unit tests: `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
- Opt-in Kubernetes integration: `INFRA_GATE_RUN_INTEGRATION=1 dotnet test InfraGate.slnx`
