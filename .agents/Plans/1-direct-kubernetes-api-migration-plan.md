# Direct Kubernetes API Migration Plan

## Summary
Move from nginx-specific `kubectl` process execution to direct Kubernetes API calls using `KubernetesClient` `19.0.2`. This is a clean-break v2: replace nginx MCP tools with generic manifest-based tools, keep the approval/hash/audit flow, and enforce a namespace allowlist.

Success criteria: no runtime dependency on `KubectlRunner`; MCP can apply, scale, restart, inspect, and delete supported resources through Kubernetes API calls only.

## Public MCP Contract
Replace nginx tools with:

- `get_k8s_status(namespace, labelSelector = null)`: read-only JSON summary of Deployments, Services, ConfigMaps, Pods, and ReplicaSets.
- `request_apply_manifest(namespace, manifest)`: creates an approval plan for multi-document YAML/JSON manifests.
- `request_delete_manifest(namespace, manifest)`: creates an approval plan to delete each object named in the manifest; missing objects count as success.
- `request_scale_deployment(namespace, name, replicas)`: creates an approval plan; keep replica bounds `0..5`.
- `request_restart_deployment(namespace, name)`: creates an approval plan by patching the Deployment pod-template restart annotation.
- `apply_approved_plan(planId)`: stays as the single mutating executor.

Supported manifest mutation kinds for v2: `apps/v1 Deployment`, `v1 Service`, `v1 ConfigMap`. Reject Secrets, Ingress, CRDs, cluster-scoped resources, unsupported kinds, missing names, and disallowed namespaces.

## Implementation Changes
- Add `KubernetesClient` `19.0.2`; remove `KubectlRunner` and `K8S_MCP_KUBECTL`.
- Replace `NginxTools`/`NginxManager`/`NginxPlan` with generic `K8sTools`/manager/plan types while preserving `ApprovalStore`.
- Add `K8S_MCP_ALLOWED_NAMESPACES`, comma-separated; default to `mcp-nginx-demo` when unset.
- Load Kubernetes config from `KUBECONFIG` using the client library.
- Parse manifests with `KubernetesYaml.LoadAllFromString`, mapped only to `V1Deployment`, `V1Service`, and `V1ConfigMap`.
- Namespace rule: tool `namespace` must be allowlisted; manifest `metadata.namespace` may be omitted or equal to that namespace; omitted namespace is set before planning/applying.
- Store plans as API actions/object refs, not kubectl command strings; the approval hash remains over the exact pending plan file.
- Apply manifests with server-side apply patches using field manager `infra-gate-mcp`.
- Scale with the Deployment scale subresource; restart with a Deployment patch.
- Replace `kubectl rollout status` with API polling for affected Deployments for up to 60 seconds.
- Update RBAC to include ConfigMap mutation verbs; keep Deployment, Deployment scale, Service, Pod, and ReplicaSet permissions.
- Update README to document the new tools, namespace allowlist, supported kinds, approval flow, and verification commands.

## Test Plan
1. Add unit tests for namespace allowlist parsing, manifest validation, namespace defaulting, unsupported-kind rejection, replica bounds, and approval hash mismatch -> verify with `dotnet test InfraGate.slnx`.
2. Update the gated MCP integration test to call the new tools: apply Deployment/Service/ConfigMap, approve/apply, inspect status, scale Deployment, restart Deployment, delete by manifest -> verify with `INFRA_GATE_RUN_INTEGRATION=1 dotnet test InfraGate.slnx --no-build`.
3. Run `dotnet build InfraGate.slnx` and normal `dotnet test InfraGate.slnx --no-build`.

## Assumptions
- Clean break means nginx-specific MCP tools are removed, not kept as aliases.
- v2 stays namespaced only; no cluster-scoped resources.
- Secrets are intentionally excluded from the first broader API.
- `kubectl` may still appear in bootstrap scripts and manual verification docs, but the MCP server runtime does not shell out to it.
