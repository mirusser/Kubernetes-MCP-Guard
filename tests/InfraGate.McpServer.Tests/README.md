# InfraGate.McpServer.Tests

`InfraGate.McpServer.Tests` covers the stdio Kubernetes MCP server without requiring a live cluster by default. It focuses on Kubernetes evidence tools, raw execution helpers, manifest validation, option parsing, adapter plan behavior, and the opt-in Kubernetes path.

## What It Covers

- `ApprovalStoreTests.cs`: grant-bound approval, denied unapproved plans, opaque plan identifiers, old-format refusal, and legacy approved-directory absence.
- `KubernetesManifestParserTests.cs`: supported Kubernetes manifests, namespace defaulting, unsupported kinds, missing names, and namespace mismatch rejection.
- `KubernetesManagerConfigTests.cs`: allowed namespace listing, sorting, empty configuration, and no K8s client dependency.
- `KubernetesManagerObservabilityTests.cs`: bounded Events, Pod logs, focused resource summaries, and sensitive-resource rejection.
- `KubernetesManagerDiagnosticsTests.cs`: bounded Deployment, Pod, and Service diagnostics without extra RBAC assumptions.
- `AuditPayloadsTests.cs`: serialisation shape for every approval-audit payload record — locks field names, ordering, and PlanId-vs-Id conventions.
- `KubernetesMcpOptionsTests.cs`: allowed namespace parsing defaults and comma-separated values.
- `KubernetesToolsTests.cs`: MCP tool delegation, argument forwarding to KubernetesManager, and plan-unaware live-drift evidence arguments.
- `KubernetesPlanBuilderTests.cs`: Kubernetes adapter plan creation, freshness policy declarations, dry-run evidence, diff evidence, policy refusal behavior, and adapter-owned reason codes.
- `KubernetesPlanExecutorTests.cs`: Kubernetes adapter pre-execution checks, drift blocking, pre-execution dry-run blocking, policy blocking, raw execution dispatch, and blocked-result reason codes.
- `KubernetesPlanReviewTests.cs`: review evidence requirements for manifest plans and dry-run-only Deployment operations.

## Running Tests

- Default suite: `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- Integration path: `INFRA_GATE_RUN_INTEGRATION=1 dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`

The integration test expects a usable kubeconfig, defaulting to `.kube/mcp-nginx-demo.config` when `KUBECONFIG` is unset.
