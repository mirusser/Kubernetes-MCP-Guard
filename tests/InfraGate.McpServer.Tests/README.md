# InfraGate.McpServer.Tests

`InfraGate.McpServer.Tests` covers the stdio Kubernetes MCP server without requiring a live cluster by default. It focuses on plan creation, approval storage, manifest validation, option parsing, and the opt-in end-to-end Kubernetes path.

## What It Covers

- `ApprovalStoreTests.cs`: approval hash matching, denied unapproved plans, changed-plan protection, and server approval writes.
- `K8sManifestParserTests.cs`: supported Kubernetes manifests, namespace defaulting, unsupported kinds, missing names, and namespace mismatch rejection.
- `K8sManagerObservabilityTests.cs`: bounded Events, Pod logs, focused resource summaries, and sensitive-resource rejection.
- `K8sManagerDiagnosticsTests.cs`: bounded Deployment, Pod, and Service diagnostics without extra RBAC assumptions.
- `K8sManagerRequestTests.cs`: request-plan formatting, namespace validation, replica bounds, approval guidance, and unapproved apply refusal.
- `K8sManagerSetImageTests.cs`: Deployment image update planning, stale-plan refusal, and patch shape.
- `K8sMcpOptionsTests.cs`: allowed namespace parsing defaults and comma-separated values.
- `McpServerIntegrationTests.cs`: opt-in stdio MCP client flow that creates, approves, applies, scales, restarts, and deletes demo Kubernetes resources.

## Running Tests

- Default suite: `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- Integration path: `INFRA_GATE_RUN_INTEGRATION=1 dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`

The integration test expects a usable kubeconfig, defaulting to `.kube/mcp-nginx-demo.config` when `KUBECONFIG` is unset.
