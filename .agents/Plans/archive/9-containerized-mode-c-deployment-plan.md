# Containerized Mode C Deployment Plan

## Summary
Add a Docker Compose based **Mode C** path for Kubernetes MCP Guard: separate containers for `McpServer`, `McpGateway`, and `DevIssuer`, using Docker bridge networking. The root README quickstart should become:

```bash
./scripts/create-demo-kubeconfig.sh --compose
docker compose -f deploy/mode-c/compose.yaml up --build
```

Keep existing source-based stdio and gateway flows working.

## Key Changes
- Add three multi-stage Dockerfiles under `deploy/docker/`:
  - `mcp-server.Dockerfile` using .NET runtime.
  - `mcp-gateway.Dockerfile` using ASP.NET runtime.
  - `devissuer.Dockerfile` using ASP.NET runtime.
- Add `deploy/mode-c/compose.yaml` with services:
  - `mcp-server`: internal HTTP MCP server on `/mcp`, no host port.
  - `mcp-gateway`: public host endpoint `127.0.0.1:3001/mcp`.
  - `devissuer`: public host endpoint `127.0.0.1:3011`.
- Keep Compose OAuth-only for Mode C; do not enable static bearer auth in this deployment.
- Add `.mcp-guardrails/` to `.gitignore` because Compose will persist guardrail audit logs there.

## Runtime Interfaces
- Extend `InfraGate.McpServer` with transport selection:
  - `K8S_MCP_TRANSPORT=stdio|http`
  - Default: `stdio`, preserving current behavior.
  - HTTP mode maps MCP at `/mcp`; Compose binds it internally on port `3002`.
- Extend gateway downstream configuration:
  - `INFRA_GATE_DOWNSTREAM_TRANSPORT=stdio|http`
  - Default: `stdio`.
  - `INFRA_GATE_DOWNSTREAM_HTTP_ENDPOINT`, required when transport is `http`.
- Extend gateway OAuth configuration for bridge networking:
  - `INFRA_GATE_OAUTH_AUTHORITY` remains the public issuer, e.g. `http://127.0.0.1:3011`.
  - Add `INFRA_GATE_OAUTH_METADATA_ADDRESS` for container-internal discovery, e.g. `http://devissuer:3011/.well-known/openid-configuration`.
- Extend DevIssuer metadata for internal discovery:
  - Add `INFRA_GATE_DEV_ISSUER_INTERNAL_ENDPOINT_BASE`.
  - Metadata always reports the public `issuer`, but when requested through the internal host it emits internal endpoint URLs such as `http://devissuer:3011/jwks`.

## Kubeconfig Setup
- Keep existing `./scripts/create-demo-kubeconfig.sh` behavior unchanged.
- Add `--compose` mode:
  - Applies existing minikube RBAC.
  - Writes the normal `.kube/mcp-nginx-demo.config`.
  - Also writes `.kube/mcp-nginx-demo.compose.config`.
  - If the Kubernetes API server is loopback, rewrite only the compose kubeconfig server host to `host.docker.internal` and set `tls-server-name` to the original loopback host.
  - Ensure `.mcp-approvals/` and `.mcp-guardrails/` exist.
- Compose mounts only the compose kubeconfig into `mcp-server`; the gateway does not receive Kubernetes credentials.

## Docs
- Root `README.md`: add a short “Containerized Mode C” quickstart with the two commands above, plus the existing Codex MCP config pointing at `http://127.0.0.1:3001/mcp`.
- `docs/setup-guide.md`: make containerized Mode C the recommended OAuth path, while keeping the current two-terminal source mode as an alternate dev flow.
- `docs/devs-readme.md`: update architecture/runtime notes to mention the containerized Mode C topology.
- Runtime READMEs: add only the new env vars relevant to each project.

## Test Plan
- Run `docker compose -f deploy/mode-c/compose.yaml config`.
- Run `bash -n scripts/create-demo-kubeconfig.sh`.
- Add tests for:
  - McpServer transport option parsing/defaults.
  - Gateway downstream HTTP transport forwarding.
  - Gateway OAuth metadata override while validating the public issuer.
  - DevIssuer internal metadata endpoint URLs while preserving public issuer.
- Run `dotnet test InfraGate.slnx`.
- Live acceptance with minikube:
  - Run the two README commands.
  - Verify `http://127.0.0.1:3001/mcp` challenges through OAuth metadata.
  - Run `codex mcp login` against the gateway.
  - List tools and execute one read-only Kubernetes status call through gateway → server → Kubernetes.

## Assumptions
- Internal .NET project names and namespaces stay `InfraGate.*` for now.
- Docker image names are local/dev names, e.g. `kubernetes-mcp-guard-server`, `kubernetes-mcp-guard-gateway`, and `kubernetes-mcp-guard-devissuer`.
- This is a local developer/interview demo deployment, not a production hardening pass.
