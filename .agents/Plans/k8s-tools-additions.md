# Kubernetes MCP Tool Additions

## Summary
Add a balanced first batch of Kubernetes MCP tools without expanding RBAC: three read-only diagnostics tools plus one approval-gated Deployment image update. Keep the project’s current safety posture: namespace allow-listing, bounded output, no raw manifests, no Secret values, no exec/attach/port-forward.

## Public Tool Changes
Add these MCP tools to both `InfraGate.McpServer` and the mirrored HTTP gateway surface:

- `get_deployment_diagnostics(namespace, name, limit = 50)`
  - Reads one Deployment, matching ReplicaSets, matching Pods, and bounded related Events.
  - Uses the Deployment selector; caps related Pods/ReplicaSets to a fixed internal limit.

- `get_pod_diagnostics(namespace, podName, limit = 50)`
  - Reads one Pod summary plus bounded related Events.
  - Does not include logs; callers keep using `get_pod_logs` for bounded log reads.

- `get_service_diagnostics(namespace, name, limit = 50)`
  - Reads one Service and Pods matching its selector.
  - Does not inspect Endpoints or EndpointSlices in this batch because current RBAC does not allow them.

- `request_set_deployment_image(namespace, name, container, image)`
  - Creates a pending approval plan to update one container image in one Deployment.
  - Applied through existing `apply_approved_plan`.

## Implementation Changes
- Add tool-name/argument/plan-operation constants in `K8sConventions` and matching gateway constants.
- Add `K8sManager.Diagnostics.cs` for the three read-only diagnostics methods, reusing existing namespace validation, API exception formatting, JSON options, event bounds, and resource summary shaping.
- Add `RequestSetDeploymentImageAsync`:
  - Validate namespace, deployment name, container name, and target image.
  - Read the Deployment at plan-request time.
  - Refuse if the container does not exist.
  - Store `name`, `container`, `currentImage`, and `image` in the plan parameters.
- Add `set-image` handling in plan application:
  - Re-read the Deployment before patching.
  - Refuse if the container is missing or its current image differs from the planned `currentImage`.
  - Patch only `spec.template.spec.containers[name].image`.
  - Reuse existing Deployment rollout wait after apply.
- Update docs where tool contracts are listed: root README, `docs/devs-readme.md`, setup guide, and server README.
- No `.csproj` or RBAC changes are needed.

## Test Plan
- Extend server tests for namespace rejection, limit validation, JSON shape, bounded related resources, and no sensitive/raw fields in diagnostics output.
- Add set-image request/apply tests for missing container, plan creation, stale image refusal, and patch request shape.
- Add gateway forwarding tests for all four new tools and arguments.
- Run:
  - `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
  - `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
  - `dotnet test InfraGate.slnx --no-build`

## Assumptions
- First batch follows your selected direction: balanced tools, but no RBAC expansion.
- Endpoint/EndpointSlice diagnostics, Jobs/CronJobs, Ingress, nodes, namespaces, metrics, Secret inspection, exec, attach, and port-forward stay out of scope for this pass.
- Current server tests are green: 25 passed in `InfraGate.McpServer.Tests`.
