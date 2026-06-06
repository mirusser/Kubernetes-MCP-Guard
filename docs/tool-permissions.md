# Tool Permissions Matrix

This document lists the 24 MCP tools exposed by Kubernetes MCP Guard and their associated Kubernetes RBAC permissions, required OAuth scope, and approval requirements. See [docs/security-model.md](security-model.md) for the broader threat model and boundary discussion.

## Common Properties

All 24 tools require at least one gateway OAuth scope. The gateway supports two scope tiers for human operators: `mcp:tools.read` (read-only tools and plan-status inspection) and `mcp:tools.write` (all tools including mutation plan creation). The legacy `mcp:tools` scope grants full access for backward compatibility. Agent service identities use role-specific scopes (`mcp:tools.readonly`, `mcp:tools.propose`, `mcp:tools.execute`). All K8s-facing operations are namespace-scoped by the MCP server namespace allow-list and by K8s RBAC. K8s RBAC is enforced independently by the K8s API server.

`ReadOnly` and `Destructive` are MCP tool annotations, not RBAC claims. They are useful client metadata, but they are not the enforcement mechanism.

## Read-Only Diagnostic Tools (8 tools)

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

## Evidence Tools (8 tools)

These read-only tools compute server-side dry-run results, diffs, and drift checks. They are used internally by plan creation and execution, and are also exposed directly to MCP clients for diagnostics.

| MCP Tool | MCP Annotation | K8s Verbs | K8s Resources | Approval Required | Bounds / Notes |
|---|---|---|---|---|---|
| `dry_run_apply_manifest` | `ReadOnly = true` | `create`, `update`, `patch` with `dryRun=All` | Deployment, Service, or ConfigMap | No | Same manifest allow-list as `request_apply_manifest` |
| `dry_run_delete_manifest` | `ReadOnly = true` | `delete` with `dryRun=All` | Deployment, Service, or ConfigMap | No | Same manifest allow-list |
| `dry_run_scale_deployment` | `ReadOnly = true` | `update`, `patch` with `dryRun=All` | Deployment `scale` subresource | No | Replicas bounded 0-5 |
| `dry_run_restart_deployment` | `ReadOnly = true` | `update`, `patch` with `dryRun=All` | Deployment | No | Server-side dry-run of rollout restart |
| `dry_run_set_deployment_image` | `ReadOnly = true` | `get`, `update`, `patch` with `dryRun=All` | Deployment | No | Server-side dry-run of container image update |
| `diff_manifest` | `ReadOnly = true` | `get` | Deployment, Service, or ConfigMap | No | Computes diff between live state and proposed manifest |
| `check_live_drift` | `ReadOnly = true` | `get` | Deployments, Services, ConfigMaps | No | Checks live state drift against recorded plan diffs |
| `diff_deployment` | `ReadOnly = true` | `get` | Deployment | No | Computes diff for a Deployment mutation (scale/restart/set-image) |

## Plan Mutation Tools (5 tools)

These tools create pending plans. They run Kubernetes `dryRun=All` first, but they do not persist Kubernetes writes. All require `mcp:tools` or `mcp:tools.write` and are namespace-scoped.

| MCP Tool | K8s Verbs at Request Time | K8s Resources at Request Time | Approval Required at Request Time | Bounds / Notes |
|---|---|---|---|---|
| `request_apply_manifest` | `create`, `update`, `patch` through server-side apply dry-run permissions | Deployment, Service, or ConfigMap | No | Manifest allow-list: `apps/v1 Deployment`, `v1 Service`, `v1 ConfigMap` only; stores dry-run result in the SHA-256-bound pending plan |
| `request_delete_manifest` | `delete` with `dryRun=All` | Deployment, Service, or ConfigMap | No | Same manifest allow-list; stores dry-run result in the SHA-256-bound pending plan |
| `request_scale_deployment` | `update`, `patch` with `dryRun=All` | Deployment `scale` subresource | No | Replicas bounded 0-5; stores dry-run result in the SHA-256-bound pending plan |
| `request_restart_deployment` | `update`, `patch` with `dryRun=All` | Deployment | No | Stores dry-run result in the SHA-256-bound pending plan for rollout restart |
| `request_set_deployment_image` | `get`, then `update`, `patch` with `dryRun=All` | Deployment | No | Reads the current Deployment image to bind the plan, then dry-runs and stores a container image patch |

## Mutation Execution Tool (1 tool)

| MCP Tool | MCP Annotation | K8s Verbs | K8s Resources | Approval Required | Bounds / Notes |
|---|---|---|---|---|---|
| `execute_approved_plan` | `Destructive = true` | Depends on approved plan | Depends on approved plan | Yes | Through the gateway, requires out-of-band browser approval through a Single-Execution challenge URL; the gateway validates the Approval Grant and generic gates, then the Kubernetes adapter repeats `dryRun=All` before any Kubernetes write |

## Approval Status Tools (2 tools)

| MCP Tool | MCP Annotation | K8s Verbs | K8s Resources | Approval Required | Bounds / Notes |
|---|---|---|---|---|---|
| `get_plan_status` | read-only gateway tool | none | none | No | Returns `planId` and status for pending, approved, applied, expired, or missing plans |
| `wait_for_plan_approval` | read-only gateway tool | none | none | No | Default timeout 55 seconds, min 1, max 300; returns `timedOut` and never applies the plan |

Local direct-stdio server experiments must use an Approval Grant for execution authorization. The legacy `scripts/approve-plan.sh` has been removed; the grant-based flow is the only supported path.

| Plan Operation | K8s Verbs | K8s Resources |
|---|---|---|
| apply, from `request_apply_manifest` | `create`, `update`, `patch` through server-side apply permissions | Deployment, Service, or ConfigMap |
| delete, from `request_delete_manifest` | `delete` | Deployment, Service, or ConfigMap |
| scale, from `request_scale_deployment` | `update`, `patch` | Deployment `scale` subresource |
| restart, from `request_restart_deployment` | `update`, `patch` | Deployment |
| set-image, from `request_set_deployment_image` | `update`, `patch` | Deployment |

## Notes

- **Scope tiers for human operators:** `mcp:tools.read` grants access to all 8 read-only diagnostic tools, 8 evidence tools, and `get_plan_status`. `mcp:tools.write` grants access to all 24 tools including the 5 `request_*` plan mutation tools, `propose_plan`, `execute_approved_plan`, `wait_for_plan_approval`, and all downstream destructive tools. The legacy `mcp:tools` scope remains available for backward compatibility. Agent service identities continue using `mcp:tools.readonly` (Observer), `mcp:tools.propose + mcp:tools.readonly` (Planner), and `mcp:tools.execute` (Executor). See [`src/InfraGate.McpGateway.Auth/README.md`](../src/InfraGate.McpGateway.Auth/README.md) for scope constants and authorization details.
- `get_allowed_namespaces` makes no Kubernetes API call. It reads in-process configuration, but it is still subject to gateway JWT and scope enforcement.
- For plan mutation tools, Kubernetes dry-run failures block plan creation and write an `execution.blocked` approval audit event with a dry-run failure payload. No Kubernetes write is persisted until `execute_approved_plan` is called and the user approves the plan.
