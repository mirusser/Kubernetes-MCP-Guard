# InfraGate.McpServer.Tests

`InfraGate.McpServer.Tests` covers the stdio Kubernetes MCP server without requiring a live cluster by default. It focuses on Kubernetes evidence tools, raw execution helpers, manifest validation, option parsing, adapter plan behavior, and the opt-in Kubernetes path.

## What It Covers

- `ApprovalStoreTests.cs`: grant-bound approval, denied unapproved plans, opaque plan identifiers, old-format refusal, and legacy approved-directory absence.
- `K8sManifestParserTests.cs`: supported Kubernetes manifests, namespace defaulting, unsupported kinds, missing names, and namespace mismatch rejection.
- `K8sManagerConfigTests.cs`: allowed namespace listing, sorting, empty configuration, and no K8s client dependency.
- `K8sManagerObservabilityTests.cs`: bounded Events, Pod logs, focused resource summaries, and sensitive-resource rejection.
- `K8sManagerDiagnosticsTests.cs`: bounded Deployment, Pod, and Service diagnostics without extra RBAC assumptions.
- `AuditPayloadsTests.cs`: serialisation shape for every approval-audit payload record — locks field names, ordering, and PlanId-vs-Id conventions.
- `K8SMcpOptionsTests.cs`: allowed namespace parsing defaults and comma-separated values.
- `K8sToolsTests.cs`: MCP tool delegation, argument forwarding to K8sManager, and plan-unaware live-drift evidence arguments.
- `KubernetesPlanBuilderTests.cs`: Kubernetes adapter plan creation, freshness policy declarations, dry-run evidence, diff evidence, and policy refusal behavior.
- `KubernetesPlanExecutorTests.cs`: Kubernetes adapter pre-execution checks, drift blocking, pre-execution dry-run blocking, policy blocking, and raw execution dispatch.
- `KubernetesPlanReviewTests.cs`: review evidence requirements for manifest plans and dry-run-only Deployment operations.

## Running Tests

- Default suite: `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- Integration path: `INFRA_GATE_RUN_INTEGRATION=1 dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`

The integration test expects a usable kubeconfig, defaulting to `.kube/mcp-nginx-demo.config` when `KUBECONFIG` is unset.
