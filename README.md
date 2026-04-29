## Infra Gate: .NET 10 Kubernetes MCP server

This repo contains a narrow Kubernetes governance slice for the larger Open WebUI/LibreChat + remote MCP idea:

- `src/InfraGate.McpServer` is a .NET 10 stdio MCP server using the official C# MCP SDK.
- `src/InfraGate.McpGateway` is a local HTTP MCP gateway that fronts the stdio server with OAuth/static bearer auth and warn+redact prompt-injection guardrails.
- `src/InfraGate.DevIssuer` is a dev-only localhost OAuth issuer with OIDC-style discovery metadata for testing Codex MCP OAuth login without an external provider.
- The MCP server uses the Kubernetes API through `KubernetesClient`, not runtime `kubectl` process execution.
- Mutating actions are two-step: request a plan through MCP, then call apply so the MCP server can request user approval before changing Kubernetes.
- The server allows only configured namespaces and only these manifest kinds for mutation: `apps/v1 Deployment`, `v1 Service`, and `v1 ConfigMap`.
- `deploy/minikube/rbac.yaml` creates a namespace-scoped ServiceAccount, Role, and RoleBinding.
- `scripts/create-demo-kubeconfig.sh` creates a short-lived service-account kubeconfig at `.kube/mcp-nginx-demo.config`.
- MCP transport and OAuth compliance notes for the HTTP gateway path are tracked in [MCP-COMPLIANCE.md](docs/MCP-COMPLIANCE.md).

General idea:

```text
Open WebUI/LibreChat
+ remote MCP
+ gateway/proxy
+ Docker/Kubernetes MCP
+ strict Kubernetes RBAC
+ auth
+ audit
+ multi-user isolation
+ approvals
```

### Bootstrap Minikube RBAC

```bash
./scripts/create-demo-kubeconfig.sh
```

The generated token is valid for 24 hours. Re-run the script when it expires.

Quick RBAC checks:

```bash
kubectl --kubeconfig .kube/mcp-nginx-demo.config auth can-i create deployments -n mcp-nginx-demo
kubectl --kubeconfig .kube/mcp-nginx-demo.config auth can-i patch configmaps -n mcp-nginx-demo
kubectl --kubeconfig .kube/mcp-nginx-demo.config auth can-i create namespaces
kubectl --kubeconfig .kube/mcp-nginx-demo.config auth can-i create deployments -n default
```

Expected: `yes`, `yes`, `no`, then `no`.

### Run the HTTP MCP gateway

The gateway is the recommended client-facing local MCP endpoint. It listens on `http://127.0.0.1:3001/mcp` by default, accepts either OAuth JWT access tokens or the local static bearer token, and starts the downstream stdio server itself.

For the local static bearer-token demo:

```bash
export REPO_ROOT="$(pwd)"
export INFRA_GATE_GATEWAY_BEARER_TOKEN="change-me"
export INFRA_GATE_DOWNSTREAM_PROJECT="${REPO_ROOT}/src/InfraGate.McpServer/InfraGate.McpServer.csproj"
export KUBECONFIG="${REPO_ROOT}/.kube/mcp-nginx-demo.config"
export K8S_MCP_APPROVAL_ROOT="${REPO_ROOT}/.mcp-approvals"
export K8S_MCP_ALLOWED_NAMESPACES=mcp-nginx-demo

dotnet run --project src/InfraGate.McpGateway/InfraGate.McpGateway.csproj
```

For local OAuth/Codex login without an external issuer, run the repo-local dev issuer in a separate terminal:

```bash
dotnet run --project src/InfraGate.DevIssuer/InfraGate.DevIssuer.csproj
```

The dev issuer listens on `http://127.0.0.1:3011` by default, exposes OAuth/OIDC discovery metadata, dynamic client registration, authorization-code + PKCE, and JWKS endpoints, and issues ephemeral JWT access tokens for `http://127.0.0.1:3001/mcp` with `mcp:tools`. It is for localhost development only; registrations, authorization codes, and signing keys are in memory and are reset on restart.

Then start the gateway with OAuth enabled:

```bash
export REPO_ROOT="$(pwd)"
export INFRA_GATE_OAUTH_AUTHORITY="http://127.0.0.1:3011"
export INFRA_GATE_OAUTH_RESOURCE="http://127.0.0.1:3001/mcp"
export INFRA_GATE_OAUTH_SCOPE="mcp:tools"
export INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false
export INFRA_GATE_DOWNSTREAM_PROJECT="${REPO_ROOT}/src/InfraGate.McpServer/InfraGate.McpServer.csproj"
export KUBECONFIG="${REPO_ROOT}/.kube/mcp-nginx-demo.config"
export K8S_MCP_APPROVAL_ROOT="${REPO_ROOT}/.mcp-approvals"
export K8S_MCP_ALLOWED_NAMESPACES=mcp-nginx-demo

dotnet run --project src/InfraGate.McpGateway/InfraGate.McpGateway.csproj
```

Set `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false` only for a localhost-only issuer during development. If `INFRA_GATE_GATEWAY_BEARER_TOKEN` is also set, the static token remains accepted for local demos while OAuth discovery is advertised to clients.

For an external OAuth/OIDC issuer, use its issuer URL for `INFRA_GATE_OAUTH_AUTHORITY`. The gateway remains a resource server only; external issuer setup, users, clients, login, consent, PKCE policy, and token issuance stay outside the gateway.

Optional dev issuer settings:

```bash
export INFRA_GATE_DEV_ISSUER_ISSUER="http://127.0.0.1:3011"
export INFRA_GATE_DEV_ISSUER_RESOURCE="http://127.0.0.1:3001/mcp"
export INFRA_GATE_DEV_ISSUER_SCOPE="mcp:tools"
export INFRA_GATE_DEV_ISSUER_SUBJECT="infra-gate-dev-user"
```

Use `ASPNETCORE_URLS` to bind the dev issuer to a different URL, and keep `INFRA_GATE_DEV_ISSUER_ISSUER` aligned with the URL clients use for discovery.

Codex CLI HTTP MCP config:

```toml
[mcp_servers.infra-gate]
url = "http://127.0.0.1:3001/mcp"
oauth_resource = "http://127.0.0.1:3001/mcp"
scopes = ["mcp:tools"]
```

Then run:

```bash
codex mcp login infra-gate
```

Guardrail audit events are written to `.mcp-guardrails/audit.jsonl` by default. Set `INFRA_GATE_GUARD_AUDIT_ROOT` to choose another directory.

### Run the stdio MCP server directly

From the repo directory run:

```bash
REPO_ROOT="$(pwd)"
codex mcp add infra-gate \
  --env KUBECONFIG="${REPO_ROOT}/.kube/mcp-nginx-demo.config" \
  --env K8S_MCP_APPROVAL_ROOT="${REPO_ROOT}/.mcp-approvals" \
  --env K8S_MCP_ALLOWED_NAMESPACES=mcp-nginx-demo \
  -- dotnet run --project "${REPO_ROOT}/src/InfraGate.McpServer/InfraGate.McpServer.csproj"
```

Next time, with the same environment, run:

```bash
dotnet run --project src/InfraGate.McpServer/InfraGate.McpServer.csproj
```

### Stdio MCP client config

Use this shape for a local stdio MCP client:

```json
{
  "mcpServers": {
    "infra-gate": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/infra-gate/src/InfraGate.McpServer/InfraGate.McpServer.csproj"
      ],
      "env": {
        "KUBECONFIG": "/absolute/path/to/infra-gate/.kube/mcp-nginx-demo.config",
        "K8S_MCP_APPROVAL_ROOT": "/absolute/path/to/infra-gate/.mcp-approvals",
        "K8S_MCP_ALLOWED_NAMESPACES": "mcp-nginx-demo"
      }
    }
  }
}
```

### Available MCP tools

The HTTP gateway exposes the same tool names and arguments as the stdio server:

- `get_k8s_status(namespace, labelSelector = null)`
- `request_apply_manifest(namespace, manifest)`
- `request_delete_manifest(namespace, manifest)`
- `request_scale_deployment(namespace, name, replicas)`
- `request_restart_deployment(namespace, name)`
- `apply_approved_plan(planId)`

Approval flow:

1. Ask the MCP server for a plan with `request_apply_manifest`, `request_scale_deployment`, etc.
2. Call `apply_approved_plan` with the returned `PlanId`.
3. The MCP server requests user approval through MCP elicitation before it writes the approval hash and applies anything.

The approval prompt requires an MCP client that supports elicitation. OAuth login authenticates the client to the gateway, but it does not replace `apply_approved_plan` approval. Codex CLI has been verified with this repo and routes elicitation prompts to its TUI for user input. Other clients vary; the community-maintained MCP client list can be filtered by `Elicitation`: <https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/docs/clients.mdx>.

The approval file stores the SHA-256 hash of the pending plan. If the pending plan changes after approval, the MCP server refuses to apply it. Audit events are written under `.mcp-approvals/audit.jsonl`.

### Verification

```bash
dotnet build InfraGate.slnx
dotnet test InfraGate.slnx --no-build
INFRA_GATE_RUN_INTEGRATION=1 dotnet test InfraGate.slnx --no-build
kubectl --kubeconfig .kube/mcp-nginx-demo.config -n mcp-nginx-demo get deployment,service,configmap,pods,replicasets -o wide
```

The integration test drives the MCP server over stdio, requests supported manifest, scale, restart, and delete plans, approves each exact pending file, applies them through MCP, and verifies the Kubernetes API path works end to end.
