# Tool Permissions Matrix

This document lists the 14 MCP tools exposed by Kubernetes MCP Guard and their associated Kubernetes RBAC permissions, required OAuth scope, and approval requirements. See [docs/security-model.md](security-model.md) for the broader threat model and boundary discussion.

## Common Properties

All 14 tools require the `mcp:tools` OAuth scope at the gateway. All Kubernetes-facing operations are namespace-scoped by the MCP server namespace allow-list and by Kubernetes RBAC. Kubernetes RBAC is enforced independently by the Kubernetes API server.

`ReadOnly` and `Destructive` are MCP tool annotations, not RBAC claims. They are useful client metadata, but they are not the enforcement mechanism.

## Read-Only Tools (8 tools)

| MCP Tool | MCP Annotation | K8s Verbs | K8s Resources | Approval Required | Bounds / Notes |
|---|---|---|---|---|---|
| `get_allowed_namespaces` | `ReadOnly = true` | none | none | No | Returns configured namespace allow-list; no Kubernetes API call |
| `get_k8s_status` | `ReadOnly = true` | `get`, `list` | Deployments, Services, ConfigMaps, Pods, ReplicaSets | No | Supports optional label selector |
| `get_k8s_events` | `ReadOnly = true` | `list` | `events.k8s.io/v1` Events | No | Default 50, max 100 |
| `get_pod_logs` | `ReadOnly = true` | `get` | Pods `log` subresource | No | Default 200 lines, max 500 lines, 65536-byte hard cap |
| `get_k8s_resource` | `ReadOnly = true` | `get` | Deployment, ReplicaSet, Pod, Service, ConfigMap | No | Secret kind explicitly rejected; no raw manifests |
| `get_deployment_diagnostics` | `ReadOnly = true` | `get`, `list` | Deployment, ReplicaSet, Pod, Events | No | Related Pods and ReplicaSets capped at 50; events default 50, max 100 |
| `get_pod_diagnostics` | `ReadOnly = true` | `get`, `list` | Pod, Events | No | Events default 50, max 100 |
| `get_service_diagnostics` | `ReadOnly = true` | `get`, `list` | Service, Pod, Events | No | Related Pods capped at 50; events default 50, max 100 |

## Plan Mutation Tools (5 tools)

These tools create pending plans. They do not apply Kubernetes writes. All require `mcp:tools`, are namespace-scoped, and use `Destructive = false`.

| MCP Tool | K8s Verbs at Request Time | K8s Resources at Request Time | Approval Required at Request Time | Bounds / Notes |
|---|---|---|---|---|
| `request_apply_manifest` | none | none | No | Manifest allow-list: `apps/v1 Deployment`, `v1 Service`, `v1 ConfigMap` only; creates SHA-256-bound pending plan |
| `request_delete_manifest` | none | none | No | Same manifest allow-list; creates SHA-256-bound pending plan |
| `request_scale_deployment` | none | none | No | Replicas bounded 0-5; creates SHA-256-bound pending plan |
| `request_restart_deployment` | none | none | No | Creates SHA-256-bound pending plan for rollout restart |
| `request_set_deployment_image` | `get` | Deployment | No | Reads the current Deployment image to bind the plan, then creates a SHA-256-bound pending plan for a container image patch |

## Mutation Execution Tool (1 tool)

| MCP Tool | MCP Annotation | K8s Verbs | K8s Resources | Approval Required | Bounds / Notes |
|---|---|---|---|---|---|
| `apply_approved_plan` | `Destructive = true` | Depends on approved plan | Depends on approved plan | Yes | Through the gateway, requires out-of-band browser approval through a single-use challenge URL; the gateway checks the approved challenge record and the server validates the SHA-256 hash match before any Kubernetes write |

`scripts/approve-plan.sh` writes a direct approval hash for local direct-stdio server experiments. It does not create a Gateway approval challenge record and is not the normal gateway approval path.

| Plan Operation | K8s Verbs | K8s Resources |
|---|---|---|
| apply, from `request_apply_manifest` | `create`, `update`, `patch` through server-side apply permissions | Deployment, Service, or ConfigMap |
| delete, from `request_delete_manifest` | `delete` | Deployment, Service, or ConfigMap |
| scale, from `request_scale_deployment` | `update`, `patch` | Deployment `scale` subresource |
| restart, from `request_restart_deployment` | `update`, `patch` | Deployment |
| set-image, from `request_set_deployment_image` | `get`, `update`, `patch` | Deployment |

## Notes

- Scope is a single flat `mcp:tools`; there is no `mcp:read` or `mcp:write` split today. If finer-grained scopes are added, update this matrix and [`src/InfraGate.McpGateway.Auth/README.md`](../src/InfraGate.McpGateway.Auth/README.md) together.
- `get_allowed_namespaces` makes no Kubernetes API call. It reads in-process configuration, but it is still subject to gateway JWT and scope enforcement.
- For plan mutation tools, no Kubernetes write occurs until `apply_approved_plan` is called and the user approves the plan.
