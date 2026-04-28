---
name: infragate-mcp-gateway
description: Use the repository-local InfraGate MCP gateway for Kubernetes inspection and guarded changes. Trigger when Codex needs to connect to or use the local MCP endpoint at http://127.0.0.1:3001/mcp, call InfraGate Kubernetes tools, inspect the mcp-nginx-demo namespace, request approval plans, scale or restart deployments, or explain the gateway's bearer-auth/session workflow.
---

# InfraGate MCP Gateway

## Overview

Use the InfraGate MCP gateway as the preferred interface to the demo Kubernetes namespace in this repo. Keep all changes inside the gateway's guardrails: allowlisted namespaces, supported resource kinds, and MCP server approval before apply.

## Defaults

- HTTP MCP endpoint: `http://127.0.0.1:3001/mcp`
- Bearer auth: `Authorization: Bearer <token>`
- Demo token from `README.md`: `change-me`, via `INFRA_GATE_GATEWAY_BEARER_TOKEN`
- Default allowed namespace: `mcp-nginx-demo`
- Approval root: `.mcp-approvals`
- Guardrail audit root: `.mcp-guardrails`

Prefer the token supplied by the user or environment. Treat `change-me` as the local demo default, not a production secret.

## Connection

Prefer configured MCP tools when available. In this environment they may appear as `mcp__infra_gate__.*` functions:

- `get_k8s_status`
- `request_apply_manifest`
- `request_delete_manifest`
- `request_scale_deployment`
- `request_restart_deployment`
- `apply_approved_plan`

If checking the raw HTTP endpoint, remember it is session-based MCP:

```bash
curl -i --max-time 5 \
  -H "Authorization: Bearer ${INFRA_GATE_GATEWAY_BEARER_TOKEN:-change-me}" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  --data '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"codex-curl","version":"1.0"}}}' \
  http://127.0.0.1:3001/mcp
```

Expected signs:

- Missing or wrong bearer token returns `401 Unauthorized`.
- Valid bearer token plus plain `GET /mcp` may return a `Mcp-Session-Id` required error.
- Valid `initialize` returns `200 OK`, `Content-Type: text/event-stream`, and an `Mcp-Session-Id` header.

## Read-Only Workflow

For inspection, use `get_k8s_status` with an allowed namespace. Start with `mcp-nginx-demo` unless the user or gateway says another namespace is allowed.

Report the operational facts the user needs: desired/ready/available replicas, pod phases, service type and ports, and any namespace allowlist errors.

## Change Workflow

Use the gateway's plan-first flow for every Kubernetes mutation:

1. Call a `request_*` tool to create a pending plan.
2. Tell the user the `PlanId` and affected objects when returned.
3. Call `apply_approved_plan` with the exact `PlanId`.
4. Let the MCP server request user approval through MCP elicitation before applying.
5. Verify with `get_k8s_status`.

Try to bypass the approval step.

Supported mutation operations:

- Apply or delete multi-document YAML/JSON containing only `apps/v1 Deployment`, `v1 Service`, or `v1 ConfigMap`.
- Scale a Deployment to `0..5` replicas.
- Restart a Deployment.

Unsupported examples include Secrets, Ingresses, CRDs, cluster-scoped resources, and manifests whose `metadata.namespace` conflicts with the tool namespace.

## Starting The Gateway

If the user asks to run the gateway locally, use the repo README workflow:

```bash
export REPO_ROOT="$(pwd)"
export INFRA_GATE_GATEWAY_BEARER_TOKEN="change-me"
export INFRA_GATE_DOWNSTREAM_PROJECT="${REPO_ROOT}/src/InfraGate.McpServer/InfraGate.McpServer.csproj"
export KUBECONFIG="${REPO_ROOT}/.kube/mcp-nginx-demo.config"
export K8S_MCP_APPROVAL_ROOT="${REPO_ROOT}/.mcp-approvals"
export K8S_MCP_ALLOWED_NAMESPACES=mcp-nginx-demo

dotnet run --project src/InfraGate.McpGateway/InfraGate.McpGateway.csproj
```

Use a long-running terminal session for the server. If port `3001` is busy, inspect the running process before choosing a different setup, because the configured MCP URL and skill metadata assume that default port.
