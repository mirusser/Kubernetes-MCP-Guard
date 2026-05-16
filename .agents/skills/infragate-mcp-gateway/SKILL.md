---
name: infragate-mcp-gateway
description: Use the repository-local InfraGate MCP gateway for Kubernetes inspection and guarded changes. Trigger when Codex needs to connect to or use the local MCP endpoint at http://127.0.0.1:3001/mcp, call InfraGate Kubernetes tools, inspect the mcp-nginx-demo namespace, request approval plans, scale or restart deployments, or explain the gateway's bearer/OAuth auth and session workflow.
---

# InfraGate MCP Gateway

## Overview

Use the InfraGate MCP gateway as the preferred interface to the demo Kubernetes namespace in this repo. Keep all changes inside the gateway's guardrails: allowlisted namespaces, supported resource kinds, and MCP server approval before apply.

## Defaults

- HTTP MCP endpoint: `http://127.0.0.1:3001/mcp`
- Auth: OAuth JWT Bearer (obtain a token from the local Keycloak realm; see Starting The Gateway below)
- Local OAuth issuer: `http://127.0.0.1:3010/realms/infra-gate`
- OAuth resource/scope: `http://127.0.0.1:3001/mcp`, `mcp:tools`
- Default allowed namespace: `mcp-nginx-demo`
- Approval root: `.mcp-approvals`
- Guardrail audit root: `.mcp-guardrails`

## Connection

Prefer configured MCP tools when available. In this environment they may appear as `mcp__infra_gate__.*` functions.

**Read-only tools (8):**

- `get_allowed_namespaces` — configured namespace allow-list
- `get_k8s_status` — Deployments, Services, ConfigMaps, Pods, ReplicaSets
- `get_k8s_events` — bounded events.k8s.io/v1 events
- `get_pod_logs` — bounded pod log reads
- `get_k8s_resource` — focused single-resource summary
- `get_deployment_diagnostics` — Deployment health, related Pods/ReplicaSets/Events
- `get_pod_diagnostics` — Pod status, conditions, container state, Events
- `get_service_diagnostics` — Service endpoints, backing Pods, Events

**Plan mutation tools (5):**

- `request_apply_manifest` — server-side apply plan for Deployment, Service, ConfigMap
- `request_delete_manifest` — delete plan for supported kinds
- `request_scale_deployment` — replica count plan (0–5)
- `request_restart_deployment` — rollout restart plan
- `request_set_deployment_image` — container image update plan

**Mutation execution tool (1):**

- `apply_approved_plan` — applies an out-of-band approved plan

If checking the raw HTTP endpoint, remember it is session-based MCP:

```bash
curl -i --max-time 5 \
  -H "Authorization: Bearer ${INFRA_GATE_JWT_TOKEN}" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  --data '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"codex-curl","version":"1.0"}}}' \
  http://127.0.0.1:3001/mcp
```

Expected signs:

- Missing or wrong bearer token returns `401 Unauthorized`.
- With OAuth enabled, missing auth returns `401 Unauthorized` with MCP OAuth discovery metadata.
- Valid bearer token plus plain `GET /mcp` may return a `Mcp-Session-Id` required error.
- Valid `initialize` returns `200 OK`, `Content-Type: text/event-stream`, and an `Mcp-Session-Id` header.

## Read-Only Workflow

For inspection, start with `get_k8s_status` and `get_allowed_namespaces` with an allowed namespace. Default to `mcp-nginx-demo` unless the user or gateway says another namespace is allowed.

For deeper investigation, use the diagnostic tools (`get_deployment_diagnostics`, `get_pod_diagnostics`, `get_service_diagnostics`) which aggregate related resources and events. For specific detail, use `get_k8s_resource`, `get_k8s_events`, or `get_pod_logs`.

Report the operational facts the user needs: desired/ready/available replicas, pod phases, service type and ports, and any namespace allowlist errors.

## Change Workflow

Use the gateway's plan-first flow for every Kubernetes mutation:

1. Call a `request_*` tool to create a pending plan.
2. Tell the user the `PlanId` and affected objects when returned.
3. Call `apply_approved_plan` with the exact `PlanId`.
4. The gateway returns an approval URL. Open it in a browser, authenticate with OAuth, and approve the plan there.
5. Call `apply_approved_plan` again with the same `PlanId` — the gateway forwards to the server and applies.
6. Verify with `get_k8s_status`.

Do not try to bypass the approval step. OAuth login authenticates access to the gateway, but Kubernetes mutation also requires browser-based out-of-band approval via the approval URL returned by `apply_approved_plan`.

Supported mutation operations:

- Apply or delete multi-document YAML/JSON containing only `apps/v1 Deployment`, `v1 Service`, or `v1 ConfigMap`.
- Scale a Deployment to `0..5` replicas.
- Restart a Deployment.
- Update a Deployment container image.

Unsupported examples include Secrets, Ingresses, CRDs, cluster-scoped resources, and manifests whose `metadata.namespace` conflicts with the tool namespace.

## Starting The Gateway

If the user asks to run the gateway locally, use the repo README workflow:

```bash
export REPO_ROOT="$(pwd)"
export INFRA_GATE_DOWNSTREAM_PROJECT="${REPO_ROOT}/src/InfraGate.McpServer/InfraGate.McpServer.csproj"
export KUBECONFIG="${REPO_ROOT}/.kube/mcp-nginx-demo.config"
export K8S_MCP_APPROVAL_ROOT="${REPO_ROOT}/.mcp-approvals"
export K8S_MCP_ALLOWED_NAMESPACES=mcp-nginx-demo

dotnet run --project src/InfraGate.McpGateway/InfraGate.McpGateway.csproj
```

Use a long-running terminal session for the server. If port `3001` is busy, inspect the running process before choosing a different setup, because the configured MCP URL and skill metadata assume that default port.

For source-run OAuth debugging, first run only the local Keycloak service:

```bash
./scripts/create-demo-kubeconfig.sh --compose
docker compose -f deploy/local-oauth/compose.yaml up keycloak
```

For source-run gateway debugging against that Keycloak instance, start the gateway with:

```bash
export REPO_ROOT="$(pwd)"
export INFRA_GATE_OAUTH_AUTHORITY="http://127.0.0.1:3010/realms/infra-gate"
export INFRA_GATE_OAUTH_METADATA_ADDRESS="http://127.0.0.1:3010/realms/infra-gate/.well-known/openid-configuration"
export INFRA_GATE_OAUTH_RESOURCE="http://127.0.0.1:3001/mcp"
export INFRA_GATE_OAUTH_SCOPE="mcp:tools"
export INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false
export INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID="infra-gate-approval-ui"
export INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT="http://127.0.0.1:3010/realms/infra-gate/protocol/openid-connect/auth"
export INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT="http://127.0.0.1:3010/realms/infra-gate/protocol/openid-connect/token"
export INFRA_GATE_APPROVAL_BASE_URL="http://127.0.0.1:3001"
export INFRA_GATE_DOWNSTREAM_PROJECT="${REPO_ROOT}/src/InfraGate.McpServer/InfraGate.McpServer.csproj"
export KUBECONFIG="${REPO_ROOT}/.kube/mcp-nginx-demo.config"
export K8S_MCP_APPROVAL_ROOT="${REPO_ROOT}/.mcp-approvals"
export K8S_MCP_ALLOWED_NAMESPACES=mcp-nginx-demo

dotnet run --project src/InfraGate.McpGateway/InfraGate.McpGateway.csproj
```

Use `codex mcp login infra-gate` for Codex CLI OAuth login after the server is configured with `url`, `oauth_resource`, and `scopes` as shown in `README.md`.
