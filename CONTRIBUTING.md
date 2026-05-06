# Contributing

Thanks for taking a look at Kubernetes MCP Guard. This project is experimental and security-sensitive: it gives MCP clients a guarded path to Kubernetes, so small changes can have real safety impact.

Before opening a pull request, read the docs that own the area you are touching:

- Developer runbook: [docs/devs-readme.md](docs/devs-readme.md)
- Setup guide: [docs/setup-guide.md](docs/setup-guide.md)
- Security model: [docs/security-model.md](docs/security-model.md)
- Tool permissions matrix: [docs/tool-permissions.md](docs/tool-permissions.md)
- Configuration reference: [docs/configuration.md](docs/configuration.md)
- Security reporting: [SECURITY.md](SECURITY.md)

## Ground Rules

- Keep changes small and reviewable.
- Preserve the existing safety model unless the PR is explicitly about changing it.
- Keep the project visibly experimental; do not describe it as production-certified.
- Do not include live tokens, kubeconfigs, credentials, or cluster logs with sensitive data.
- Do not open public issues for security vulnerabilities. Use [GitHub Security Advisories](https://github.com/mirusser/Kubernetes-MCP-Guard/security/advisories/new).

## Local Setup

Install the .NET 10 SDK, Docker Compose v2, minikube, and kubectl. The full setup paths are in [docs/setup-guide.md](docs/setup-guide.md).

For the local demo namespace and kubeconfig:

```bash
./scripts/create-demo-kubeconfig.sh --compose
```

For the containerized OAuth demo:

```bash
docker compose -f deploy/mode-c/compose.yaml up --build
```

For Compose validation without starting services:

```bash
docker compose -f deploy/mode-c/compose.yaml config
```

## Verification

Run the narrowest useful checks for your change. For most code or contract changes, use:

```bash
dotnet build InfraGate.slnx
dotnet test InfraGate.slnx --no-build
```

For live Kubernetes coverage against the demo namespace:

```bash
INFRA_GATE_RUN_INTEGRATION=1 dotnet test InfraGate.slnx --no-build
INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --no-build
```

For coverage:

```bash
./scripts/coverage.sh
```

Docs-only changes normally need `git diff --check` and link/content checks rather than full test runs.

## MCP Tool Changes

MCP tools are external contracts. Before adding or changing one:

- Keep the tool surface narrow and typed.
- Do not add raw shell execution, `kubectl` passthrough, exec, attach, port-forward, namespace creation, RBAC manipulation, or Secret-value reads.
- Preserve OAuth JWT enforcement at the HTTP gateway.
- Preserve namespace allow-list checks and Kubernetes RBAC assumptions.
- Keep mutation behavior plan-first: `request_*` creates a pending plan, and `apply_approved_plan` applies only after approval.
- Preserve hash-bound approvals so a plan cannot change after approval.
- Keep observability bounded, and avoid raw manifests or ConfigMap values in read responses.
- Route model-visible reads through gateway guardrails and response sanitization.
- Update [docs/tool-permissions.md](docs/tool-permissions.md), [docs/security-model.md](docs/security-model.md), README tool tables, and tests when a tool contract changes.

## Documentation Changes

Use the existing ownership model:

- Setup and runnable commands belong in [docs/setup-guide.md](docs/setup-guide.md) or [docs/devs-readme.md](docs/devs-readme.md).
- Environment variables belong in [docs/configuration.md](docs/configuration.md).
- Security boundaries and threat model belong in [docs/security-model.md](docs/security-model.md).
- Tool RBAC and scope claims belong in [docs/tool-permissions.md](docs/tool-permissions.md).
- Protocol compliance belongs in [docs/MCP-compliance.md](docs/MCP-compliance.md).
- Release process belongs in [docs/releasing.md](docs/releasing.md) and release-visible changes belong in [CHANGELOG.md](CHANGELOG.md).

When a doc needs content owned elsewhere, link to the owner instead of duplicating it.
