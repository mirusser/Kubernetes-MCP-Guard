# Simplify Mode C To Gateway + Private Stdio Server

## Summary

Refactor the containerized Mode C deployment so `InfraGate.McpGateway` remains the only HTTP MCP surface and `InfraGate.McpServer` returns to being a private stdio subprocess. Compose should run only `mcp-gateway` and `devissuer`; the gateway image will contain the published server binary and launch it internally.

## Key Changes

- Remove the newly added `InfraGate.McpServer` HTTP transport mode:
  - Remove `K8S_MCP_TRANSPORT`.
  - Remove MCP server HTTP binding/default URL constants.
  - Remove ASP.NET dependency from `InfraGate.McpServer`.
- Remove gateway HTTP downstream support:
  - Remove `INFRA_GATE_DOWNSTREAM_TRANSPORT`.
  - Remove `INFRA_GATE_DOWNSTREAM_HTTP_ENDPOINT`.
  - Keep stdio downstream as the only runtime model.
- Add a container-friendly gateway subprocess option:
  - Keep existing `INFRA_GATE_DOWNSTREAM_PROJECT` for source/dev mode.
  - Add `INFRA_GATE_DOWNSTREAM_ASSEMBLY`.
  - If assembly is set, gateway starts `dotnet /path/to/InfraGate.McpServer.dll`.
  - Otherwise, gateway preserves current `dotnet run --project ...` behavior.
- Keep OAuth bridge-network support:
  - Retain `INFRA_GATE_OAUTH_METADATA_ADDRESS`.
  - Retain `INFRA_GATE_DEV_ISSUER_INTERNAL_ENDPOINT_BASE`.

## Deployment Changes

- Replace the three-service Compose setup with two services:
  - `devissuer`: public host endpoint `127.0.0.1:3011`.
  - `mcp-gateway`: public host endpoint `127.0.0.1:3001/mcp`; internally launches `InfraGate.McpServer` over stdio.
- Remove the standalone `mcp-server` image/Dockerfile.
- Update the gateway Dockerfile to publish both:
  - gateway app into `/app/gateway`
  - server app into `/app/server`
- Configure Compose gateway service with:
  - `INFRA_GATE_DOWNSTREAM_ASSEMBLY=/app/server/InfraGate.McpServer.dll`
  - `KUBECONFIG=/run/kube/mcp-nginx-demo.compose.config`
  - `K8S_MCP_APPROVAL_ROOT=/data/approvals`
  - `K8S_MCP_ALLOWED_NAMESPACES=mcp-nginx-demo`
  - OAuth env vars for DevIssuer.
- Keep `./scripts/create-demo-kubeconfig.sh --compose` because the gateway container still needs a container-reachable kubeconfig.

## Tradeoff Note

This approach is simpler and keeps a smaller HTTP attack surface because only the gateway is network-facing. The cons are that the gateway image bundles the server binary, the gateway and server share one container boundary, and the server cannot be independently scaled or restarted.

## Docs

- Keep the root README two-command quickstart:

  ```bash
  ./scripts/create-demo-kubeconfig.sh --compose
  docker compose -f deploy/mode-c/compose.yaml up --build
  ```

- Update architecture diagrams and wording:
  - Gateway is the only network-facing MCP server.
  - McpServer is a private stdio subprocess owned by the gateway.
- Update runtime READMEs and env var tables:
  - Remove HTTP server/downstream env vars.
  - Add `INFRA_GATE_DOWNSTREAM_ASSEMBLY`.
- Add a short note in docs explaining the tradeoff.

## Test Plan

- Remove tests for:
  - McpServer transport parsing.
  - Gateway HTTP downstream forwarding.
- Add tests for:
  - Gateway option parsing/defaults for `INFRA_GATE_DOWNSTREAM_ASSEMBLY`.
  - `DownstreamMcpClient` using assembly mode to build `dotnet /app/server/InfraGate.McpServer.dll` arguments.
  - Existing stdio downstream smoke test still passes.
  - OAuth metadata override and DevIssuer internal metadata tests remain.
- Run:
  - `bash -n scripts/create-demo-kubeconfig.sh`
  - `docker compose -f deploy/mode-c/compose.yaml config`
  - `dotnet build InfraGate.slnx`
  - `dotnet test InfraGate.slnx --no-build`
  - `docker compose -f deploy/mode-c/compose.yaml build`

## Assumptions

- Source/dev behavior remains backward compatible with `INFRA_GATE_DOWNSTREAM_PROJECT`.
- Container Mode C uses OAuth only; static bearer auth stays documented for source Mode B.
- Internal namespaces and project names remain `InfraGate.*`.
