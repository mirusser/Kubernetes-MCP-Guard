---
name: run-tests
description: Run all available tests in the repository, correctly accounting for prerequisites (Docker, Kubernetes, environment variables) and trait/category gating. Use when running, verifying, or troubleshooting any test tier.
---

# Run Tests

## Quick Reference

| Tier | What runs | Prerequisites | Command |
|------|-----------|---------------|---------|
| **1** | All unit tests | .NET 10 SDK | `dotnet test InfraGate.slnx --filter "Category!=Keycloak"` |
| **2** | Keycloak integration | .NET 10 + Docker | `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"` |
| **3** | McpServer integration | + K8s cluster + kubeconfig | `INFRA_GATE_RUN_INTEGRATION=1 dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj` |
| **4** | Gateway integration | + K8s cluster + kubeconfig | `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "Category!=Keycloak"` |
| **5** | Safety E2E | + Docker + K8s + nginx-demo | `INFRA_GATE_RUN_SAFETY_E2E=1 KUBECONFIG=... dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj --filter "Category=SafetyE2E"` |
| **6** | Coverage | .NET 10 (+ Docker for full) | `./scripts/coverage.sh` |
| **7** | Compose smoke (release) | Docker + K8s + kubeconfig + compose setup | `TAG=latest ./scripts/smoke-test-release.sh` |
| **8** | Compose smoke (local build) | Docker + K8s + kubeconfig + compose setup | `./scripts/smoke-test-local.sh` |

**One-liner to run everything your machine supports:**

```bash
./scripts/run-tests.sh
```

Auto-detects Docker and K8s availability. Skips tiers your machine can't run. Reports what ran and what was skipped.

---

## Test Gating Mechanisms

There are three ways tests are gated:

### 1. Trait / category filter (`--filter`)

| Trait | Used by | Runs via |
|-------|---------|----------|
| `Category=Keycloak` | `KeycloakIntegrationTests.cs` | Only when `--filter "Category=Keycloak"` is passed |
| `Category=SafetyE2E` | All Safety E2E workflow tests | Only when `--filter "Category=SafetyE2E"` is passed |

Tests with `Category=Keycloak` are **excluded** by the default filter `Category!=Keycloak`. Tests with `Category=SafetyE2E` have no `!=` exclusion — they are guarded by an env var (see below).

### 2. Environment variable gates

Tests in integration and E2E projects early-return `if (env != "1")` **without failing**:

| Env var | Set to `"1"` to enable | Guards |
|---------|------------------------|--------|
| `INFRA_GATE_RUN_INTEGRATION` | Yes | `McpServerIntegrationTests` (single test in `tests/InfraGate.McpServer.Tests`) |
| `INFRA_GATE_RUN_GATEWAY_INTEGRATION` | Yes | `GatewayHttpMcpIntegrationTests` (single test in `tests/InfraGate.McpGateway.Tests`) |
| `INFRA_GATE_RUN_SAFETY_E2E` | Yes | All Safety E2E workflow tests (11 files in `tests/InfraGate.Safety.E2E.Tests`) |

Without these set, the tests pass immediately with no-op — they do **not** fail or skip visibly.

### 3. Implicit Docker dependency

Two test projects use `Testcontainers.Keycloak` which starts a Keycloak container at test time:

| Project | Docker required for | Fails if Docker unavailable? |
|---------|---------------------|------------------------------|
| `InfraGate.McpGateway.KeycloakTests` | All tests (via `IAsyncLifetime.InitializeAsync`) | Yes — `InitializeAsync` throws |
| `InfraGate.Safety.E2E.Tests` | All tests (via `SafetyE2EFixture.InitializeAsync`) | Yes — but only if `INFRA_GATE_RUN_SAFETY_E2E=1` is set; otherwise fixture skips startup |

---

## Tier Details

### Tier 1: Unit Tests

**What it covers:** All unit tests that don't need Docker or K8s. Also includes RuntimeSafety and approval store tests.

```bash
dotnet test InfraGate.slnx --filter "Category!=Keycloak"
```

**Test projects exercised:**
- `InfraGate.McpServer.Tests` (253 unit tests)
- `InfraGate.McpGateway.Tests` (166 unit tests)
- `InfraGate.RuntimeSafety.Tests`

**Prerequisites:**
- .NET 10 SDK (`dotnet --version` must report 10.x)

**Expected output:**
```
Passed!  - Failed:     0, Passed:   XXX, Skipped:     0
```

**CI equivalent:** `unit-tests.yml` — runs on every PR/push to `main`/`dev`.

---

### Tier 2: Keycloak Integration Tests

**What it covers:** 11 tests exercising real OIDC discovery, JWT validation, PKCE flows, scope checks, audience checks, and the approval browser OAuth callback/cookie path against a Testcontainers Keycloak container.

```bash
dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"
```

**Prerequisites:**
1. .NET 10 SDK
2. Docker daemon running (`docker info` succeeds)
3. Pull access to `quay.io/keycloak/keycloak:26.6.1`

**Expected output:**
```
Passed!  - Failed:     0, Passed:    11, Skipped:     0
```

**CI equivalent:** `keycloak-tests.yml` — runs on every PR/push to `main`/`dev`. 10-minute timeout.

**To list tests without starting Docker:**
```bash
dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --list-tests --filter "Category=Keycloak"
```

---

### Tier 3: McpServer Integration Test

**What it covers:** One integration test (`McpServer_CanApplyApprovedK8sPlans_WhenIntegrationEnabled`) that spawns the McpServer as a subprocess, requests/approves/applies a Kubernetes manifest, scales, restarts, and deletes real resources, and verifies the full approval lifecycle.

```bash
INFRA_GATE_RUN_INTEGRATION=1 dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj
```

**Prerequisites:**
1. .NET 10 SDK
2. A K8s cluster reachable through a kubeconfig (default: `.kube/mcp-nginx-demo.config`)
3. The `mcp-nginx-demo` namespace with RBAC from `deploy/minikube/rbac.yaml`
4. The `nginx-demo` Deployment running in `mcp-nginx-demo` namespace

**Kubeconfig resolution:**
- Uses `$KUBECONFIG` if set
- Falls back to `.kube/mcp-nginx-demo.config` in repo root
- Writes approval files to a temp directory via `K8S_MCP_APPROVAL_ROOT`

**CI equivalent:** `integration-tests.yml` — runs on a self-hosted runner with Minikube.

---

### Tier 4: Gateway Integration Test

**What it covers:** One integration test (`GatewayHttpMcp_RestartDeployment_ThroughFullApprovalFlow`) that runs the full gateway HTTP path: MCP request through the TestServer gateway, approval challenge creation, browser approval, and deployment restart on a real K8s cluster.

```bash
INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "Category!=Keycloak"
```

**Prerequisites:**
1. .NET 10 SDK
2. A K8s cluster reachable through a kubeconfig (default: `.kube/mcp-nginx-demo.config`)
3. The `mcp-nginx-demo` namespace with RBAC
4. The `nginx-demo` Deployment running

**CI equivalent:** `integration-tests.yml` (second step) — self-hosted runner with Minikube.

---

### Tier 5: Safety E2E Tests

**What it covers:** 10+ workflow tests proving the seven safety properties plus RBAC boundary enforcement: full approval flow, review digest mismatch detection, expired challenge rejection, double-apply refusal, dangerous manifest blocking, modified pending plan detection, wrong-user approval rejection, and dry-run failure at request/apply time.

```bash
INFRA_GATE_RUN_SAFETY_E2E=1 \
  KUBECONFIG="$(pwd)/.kube/mcp-nginx-demo.config" \
  dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj \
    --filter "Category=SafetyE2E"
```

**Prerequisites:**
1. .NET 10 SDK
2. Docker daemon running (starts Keycloak container)
3. A K8s cluster reachable through the kubeconfig
4. The `mcp-nginx-demo` namespace with RBAC from `deploy/minikube/rbac.yaml`
5. The `nginx-demo` Deployment and `mcp-security-failing-demo` services from `examples/failing-deployment/deployment.yaml`

**Setup (first time):**
```bash
# Create demo kubeconfig
./scripts/create-demo-kubeconfig.sh

# Apply demo resources
kubectl --kubeconfig .kube/mcp-nginx-demo.config apply -f deploy/minikube/rbac.yaml
kubectl --kubeconfig .kube/mcp-nginx-demo.config apply -f examples/failing-deployment/deployment.yaml
```

**CI equivalent:** `safety-e2e.yml` — only runs on `workflow_dispatch` (manual trigger).

---

### Tier 6: Code Coverage

**What it covers:** Full solution test run with code coverage collection, HTML/Cobertura report generation, and threshold enforcement (80% line, 77% branch).

```bash
./scripts/coverage.sh
```

> **Note:** The coverage script runs `dotnet test InfraGate.slnx` without `Category!=Keycloak` filter. If Docker is available, Keycloak tests will execute; if not, they will fail. The CI workflow (`sonar.yml`) adds `--filter "Category!=Keycloak"` explicitly.

**Prerequisites:**
- .NET 10 SDK
- (Optional) Docker — if available, Keycloak tests run and contribute to coverage; if not, the script fails

**CI equivalent:** `sonar.yml` — runs on every PR/push to `main`/`dev` with `--filter "Category!=Keycloak"`.

---

### Tier 7: Compose Smoke Test (Released Image)

**What it covers:** Boots the full Keycloak + Gateway compose stack using the published gateway image (`compose.release.yaml`). Verifies:
- Keycloak OIDC discovery is reachable
- Gateway HTTP server responds to `/mcp`
- All host-side volume directories (`.mcp-approvals`, `.mcp-guardrails`, `.mcp-dataprotection-keys`) exist
- Gateway logs contain no filesystem permission errors (DataProtection key-ring, `UnauthorizedAccessException`, `Permission denied`)
- Unauthenticated `/mcp` returns 401 with `resource_metadata` in `WWW-Authenticate`
- A real Keycloak token is acquired and accepted by the gateway auth layer

```bash
TAG=latest ./scripts/smoke-test-release.sh
```

Override the tag to test a specific release:
```bash
TAG=vX.Y.Z ./scripts/smoke-test-release.sh
```

**Prerequisites:**
1. Docker daemon running (`docker compose version` succeeds)
2. `curl` and `jq` available
3. `./scripts/create-demo-kubeconfig.sh --compose` must have been run first
4. K8s cluster reachable through `.kube/mcp-nginx-demo.compose.config`
5. Pull access to `ghcr.io/mirusser/kubernetes-mcp-guard-gateway` and `quay.io/keycloak/keycloak:26.6.1`

**Expected output:**
```
OK: release smoke test passed for tag 'latest'.
```

**CI equivalent:** none currently (local-only smoke test). Typically runs for ~90s after image pull.

---

### Tier 8: Compose Smoke Test (Local Build)

**What it covers:** Same verifications as tier 7, but builds the gateway image from source via `compose.yaml` (`docker compose up --build`) instead of pulling the published image. Tests the full Docker build pipeline in addition to the runtime smoke checks. Includes `.mcp-logs` in the host volume directory check.

```bash
./scripts/smoke-test-local.sh
```

**Prerequisites:**
1. Docker daemon running with BuildKit/buildx support (`docker compose build` succeeds)
2. `curl` and `jq` available
3. `./scripts/create-demo-kubeconfig.sh --compose` must have been run first
4. K8s cluster reachable through `.kube/mcp-nginx-demo.compose.config`
5. Pull access to `quay.io/keycloak/keycloak:26.6.1`
6. Source tree must build (Docker build runs `dotnet restore` and `dotnet publish` inside the container)

**Expected output:**
```
OK: local-build smoke test passed.
```

**CI equivalent:** none currently (local-only smoke test). Typically runs for ~3-5 minutes including the Docker build.

---

## Prerequisites Verification

Run these checks before running tests:

```bash
# .NET 10 SDK
dotnet --version
# Expected: 10.0.x

# Docker (for tiers 2, 5, 6)
docker info
# Expected: prints daemon info, no error

# Kubernetes cluster (for tiers 3, 4, 5)
kubectl --kubeconfig .kube/mcp-nginx-demo.config cluster-info
# Expected: cluster info output

# Demo namespace (for tiers 3, 4, 5)
kubectl --kubeconfig .kube/mcp-nginx-demo.config -n mcp-nginx-demo get deployment nginx-demo
# Expected: deployment found
```

**Environment variable check:**
```bash
echo "INFRA_GATE_RUN_INTEGRATION=$INFRA_GATE_RUN_INTEGRATION"        # should be 1 for tier 3
echo "INFRA_GATE_RUN_GATEWAY_INTEGRATION=$INFRA_GATE_RUN_GATEWAY_INTEGRATION"  # should be 1 for tier 4
echo "INFRA_GATE_RUN_SAFETY_E2E=$INFRA_GATE_RUN_SAFETY_E2E"          # should be 1 for tier 5
```

**Compose smoke prerequisites (for tiers 7, 8):**

```bash
# Compose setup (populates kubeconfig and persistence dirs)
./scripts/create-demo-kubeconfig.sh --compose
# Expected: creates .kube/mcp-nginx-demo.compose.config and directories under .mcp-*

# Verify compose file is valid
docker compose -f deploy/local-oauth/compose.yaml config >/dev/null
docker compose -f deploy/local-oauth/compose.release.yaml config >/dev/null
# Expected: no errors
```

---

## CI Parity

| CI workflow | Runs on | Test tiers covered | Filter |
|-------------|---------|-------------------|--------|
| `unit-tests.yml` | Every PR/push `main`/`dev` | 1 | `Category!=Keycloak` |
| `keycloak-tests.yml` | Every PR/push `main`/`dev` | 2 | `Category=Keycloak` |
| `integration-tests.yml` | Every PR/push `main`/`dev` (self-hosted) | 3 + 4 | (env-var gated) |
| `safety-e2e.yml` | Manual `workflow_dispatch` only | 5 | `Category=SafetyE2E` |
| `sonar.yml` | Every PR/push `main`/`dev` | 6 | `Category!=Keycloak` |

Tiers 7 and 8 (compose smoke) are local-only smoke tests run against the full Keycloak + Gateway compose stack. They are not part of any CI workflow — run them before merging if your changes touch the Dockerfile, compose files, volume mounts, `create-demo-kubeconfig.sh`, or DataProtection setup.

Tiers 3, 4, and 5 require real infrastructure (K8s, Docker) and should be verified locally before merging if your changes touch the approval pipeline, Kubernetes adapter, or gateway auth.

---

## Auto-Detection Runner

`scripts/run-tests.sh` auto-detects available infrastructure and runs everything it can:

1. Always runs tier 1 (unit tests)
2. Detects Docker → runs tier 2 (Keycloak)
3. Detects K8s cluster + kubeconfig → runs tiers 3, 4, 5
4. Detects Docker + K8s + compose kubeconfig → runs tier 7 (release smoke)
5. Reports a summary of what ran, what passed, and what was skipped

Run it:
```bash
./scripts/run-tests.sh
```
