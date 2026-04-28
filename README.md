## Infra Gate: .NET 10 Kubernetes MCP server

This repo contains a narrow Kubernetes governance slice for the larger Open WebUI/LibreChat + remote MCP idea:

- `src/InfraGate.McpServer` is a .NET 10 stdio MCP server using the official C# MCP SDK.
- The MCP server uses the Kubernetes API through `KubernetesClient`, not runtime `kubectl` process execution.
- Mutating actions are two-step: request a plan through MCP, approve the exact plan file out of band, then apply it through MCP.
- The server allows only configured namespaces and only these manifest kinds for mutation: `apps/v1 Deployment`, `v1 Service`, and `v1 ConfigMap`.
- `deploy/minikube/rbac.yaml` creates a namespace-scoped ServiceAccount, Role, and RoleBinding.
- `scripts/create-demo-kubeconfig.sh` creates a short-lived service-account kubeconfig at `.kube/mcp-nginx-demo.config`.

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

### Run the MCP

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

### MCP client config

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

- `get_k8s_status(namespace, labelSelector = null)`
- `request_apply_manifest(namespace, manifest)`
- `request_delete_manifest(namespace, manifest)`
- `request_scale_deployment(namespace, name, replicas)`
- `request_restart_deployment(namespace, name)`
- `apply_approved_plan(planId)`

Approval flow:

```bash
# 1. Ask the MCP server for a plan with request_apply_manifest, request_scale_deployment, etc.
# 2. Approve the returned PlanId out of band:
./scripts/approve-plan.sh <plan-id>
# 3. Ask the MCP server to run apply_approved_plan with that same planId.
```

The approval file stores the SHA-256 hash of the pending plan. If the pending plan changes after approval, the MCP server refuses to apply it. Audit events are written under `.mcp-approvals/audit.jsonl`.

### Verification

```bash
dotnet build InfraGate.slnx
dotnet test InfraGate.slnx --no-build
INFRA_GATE_RUN_INTEGRATION=1 dotnet test InfraGate.slnx --no-build
kubectl --kubeconfig .kube/mcp-nginx-demo.config -n mcp-nginx-demo get deployment,service,configmap,pods,replicasets -o wide
```

The integration test drives the MCP server over stdio, requests supported manifest, scale, restart, and delete plans, approves each exact pending file, applies them through MCP, and verifies the Kubernetes API path works end to end.
