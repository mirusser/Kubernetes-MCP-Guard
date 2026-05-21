# GitHub Workflows for k8s-toolkit

## Summary
Add four GitHub Actions workflows under `.github/workflows`: .NET build, default cluster-free tests, live Kubernetes integration tests, and Docker image build/publish. The repo has no existing workflows, uses `InfraGate.slnx`, targets `net10.0`, and documents the same build/test commands in `docs/devs-readme.md`.

## Key Changes
- Add `.NET Build` workflow on PRs to `main`, pushes to `main`, and manual dispatch:
  - `runs-on: ubuntu-latest`
  - `actions/checkout@v6`
  - `actions/setup-dotnet@v5` with `dotnet-version: 10.0.x`
  - `dotnet restore InfraGate.slnx`
  - `dotnet build InfraGate.slnx --configuration Release --no-restore`
- Add `Unit Tests` workflow on PRs to `main`, pushes to `main`, and manual dispatch:
  - Restore and build Release
  - Run `dotnet test InfraGate.slnx --configuration Release --no-build`
  - Do not set live integration env vars, so Kubernetes-dependent tests remain inactive
- Add `Integration Tests` workflow on pushes to `main` and manual dispatch:
  - `runs-on: [self-hosted, linux]`
  - Add `concurrency` for integration runs to avoid namespace collisions
  - Run `./scripts/create-demo-kubeconfig.sh`
  - Set `KUBECONFIG=${{ github.workspace }}/.kube/mcp-nginx-demo.config`
  - Run `INFRA_GATE_RUN_INTEGRATION=1 dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --configuration Release --no-build`
  - Run `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --configuration Release --no-build`
- Add `Docker` workflow:
  - Build both Dockerfiles on PRs, pushes to `main`, tags `v*`, and manual dispatch without pushing by default
  - Matrix entries:
    - `deploy/docker/devissuer.Dockerfile` -> `kubernetes-mcp-guard-devissuer`
    - `deploy/docker/mcp-gateway.Dockerfile` -> `kubernetes-mcp-guard-gateway`
  - Use Docker official actions: `docker/setup-buildx-action@v4`, `docker/login-action@v4`, `docker/metadata-action@v6`, `docker/build-push-action@v7`
  - Push only for `v*` tags or manual dispatch with `push_images=true`
  - Require `vars.DOCKERHUB_USERNAME`, `vars.DOCKERHUB_NAMESPACE`, and `secrets.DOCKERHUB_TOKEN` for publishing

## Test Plan
- Validate workflow YAML syntax locally by inspection after creation.
- Verify existing repo commands still pass:
  - `dotnet build InfraGate.slnx`
  - `dotnet test InfraGate.slnx --no-build`
  - `docker compose -f deploy/mode-c/compose.yaml config`
- For integration workflow, verify on the self-hosted runner that `kubectl`, cluster credentials, and permissions are sufficient for `scripts/create-demo-kubeconfig.sh`.
- For Docker workflow, first run PR/main build-only mode, then test publishing with a disposable version tag before relying on `latest`/semver tags.

## Assumptions
- The integration runner is labeled `self-hosted` and `linux`.
- The self-hosted runner has Kubernetes access capable of applying `deploy/minikube/rbac.yaml` and creating a service-account token.
- The self-hosted runner is updated enough for Node 24 based GitHub Actions.
- Docker Hub publishing uses the existing compose image names as repository suffixes.
- No .NET source, test, Dockerfile, or project-file changes are needed.
- References checked: [actions/checkout releases](https://github.com/actions/checkout/releases), [actions/setup-dotnet](https://github.com/actions/setup-dotnet), [Docker GitHub Actions docs](https://docs.docker.com/build/ci/github-actions/), and [docker/metadata-action examples](https://github.com/docker/metadata-action).
