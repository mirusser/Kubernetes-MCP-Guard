# Kubernetes Observability Tools Plan

## Summary
- Add read-only observability tools first: bounded Events, bounded Pod logs, and focused resource details.
- Keep existing mutation tools, approval flow, RBAC posture, env vars, and Kubernetes contracts unchanged.
- Reference basis: Kubernetes API docs for [API concepts](https://kubernetes.io/docs/reference/using-api/api-concepts/), [Pod logs](https://kubernetes.io/docs/reference/kubernetes-api/workload-resources/pod-v1/), and [Events](https://kubernetes.io/docs/reference/kubernetes-api/cluster-resources/event-v1/).

## Public Tool Additions
- Add `get_k8s_events(namespace, labelSelector = null, fieldSelector = null, limit = 50)`.
  - Read-only, namespace-scoped, max `limit = 100`.
  - Use `events.k8s.io/v1` via `client.EventsV1.ListNamespacedEventAsync`.
  - Return compact JSON with selectors, effective limit, and event summaries.
- Add `get_pod_logs(namespace, podName, container = null, tailLines = 200, previous = false)`.
  - Read-only, namespace-scoped, max `tailLines = 500`, hard internal `limitBytes = 65536`.
  - Use `client.CoreV1.ReadNamespacedPodLogAsync` with `follow = false` and `insecureSkipTLSVerifyBackend = false`.
  - Return JSON with namespace, podName, container, previous, tailLines, truncated byte cap, and log text.
- Add `get_k8s_resource(namespace, kind, name)`.
  - Read-only focused summaries for `Deployment`, `ReplicaSet`, `Pod`, `Service`, and `ConfigMap`.
  - Reject `Secret` and unsupported kinds explicitly.
  - Do not return ConfigMap values, Secret values, env values, or raw manifests.

## Implementation Changes
- In `InfraGate.McpServer`, add tool-name constants, argument constants, validation bounds, and a new `K8sManager` observability partial for Events, logs, and resource summaries.
- Mirror the same tool names and arguments in `InfraGate.McpGateway` so HTTP MCP exposes the same surface as stdio MCP and still benefits from gateway guardrail sanitization.
- Update sample RBAC with read-only permissions only:
  - `events.k8s.io` `events`: `get`, `list`, `watch`
  - core `pods/log`: `get`
- Update README/setup docs to list the new tools and note that logs/events are untrusted application/cluster output; direct stdio use bypasses gateway prompt-injection sanitization.

## Test Plan
- Add server tests for namespace validation, unsupported resource kind rejection, Secret rejection, event limit bounds, and log tail bounds.
- Add HTTP-stubbed Kubernetes client tests for event, log, and resource JSON formatting without requiring a live cluster.
- Add gateway delegation tests proving the three new tools forward the exact tool names and argument keys.
- Extend opt-in integration coverage only when `INFRA_GATE_RUN_INTEGRATION=1` is already configured.
- Run:
  - `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
  - `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
  - `dotnet test InfraGate.slnx`

## Assumptions
- First pass is observability only; no new mutation tools.
- No Secret value reads, no exec/attach/port-forward, no arbitrary CRD access, and no cluster-scoped resource expansion.
- `events.k8s.io/v1` is the primary Events API; legacy core/v1 Events fallback is deferred unless older-cluster support becomes necessary.
