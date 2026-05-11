# InfraGate.Safety.E2E.Tests

End-to-end tests that prove the seven safety properties listed in [`.agents/Plans/minimum-for-demo.md`](../../.agents/Plans/minimum-for-demo.md) §6 ("Tests proving the safety model"). Each test exercises the production code path: real OAuth (Keycloak in a container via Testcontainers), real gateway HTTP host (`Microsoft.AspNetCore.TestHost`), real `InfraGate.McpServer` subprocess spawned by the gateway's `DownstreamMcpClient`, and a real Kubernetes API via the developer-provided kubeconfig.

These tests do **not** run in the default repo test pass. They are gated behind an environment variable and require Docker plus a running Kubernetes cluster.

## What it covers

| File | Demo bullet | What it proves |
|---|---|---|
| [`Workflows/SmokeTests.cs`](Workflows/SmokeTests.cs) | (fixture sanity) | Gateway `/mcp` returns 401 without a bearer; not-401/403 with a valid Keycloak-issued JWT. |
| [`Workflows/PlanHashMismatchTests.cs`](Workflows/PlanHashMismatchTests.cs) | 1 | After approval, mutating the pending plan file is detected at apply time and refused. |
| [`Workflows/ExpiredApprovalTests.cs`](Workflows/ExpiredApprovalTests.cs) | 2 | A challenge whose `ExpiresAtUtc` is in the past is refused at approve time. |
| [`Workflows/AlreadyAppliedPlanTests.cs`](Workflows/AlreadyAppliedPlanTests.cs) | 3 | Applying a plan twice succeeds the first time and is refused the second. |
| [`Workflows/DangerousManifestTests.cs`](Workflows/DangerousManifestTests.cs) | 4 | A manifest with `securityContext.privileged: true` is rejected by the policy validator at request time and never produces a pending plan. |
| [`Workflows/ModifiedPendingPlanTests.cs`](Workflows/ModifiedPendingPlanTests.cs) | 5 | Mutating the pending plan after the request but before approval is detected at approve time. |
| [`Workflows/WrongUserApprovalTests.cs`](Workflows/WrongUserApprovalTests.cs) | 6 | A challenge created by user A cannot be approved by user B (same-subject enforcement). |
| [`Workflows/DryRunFailureTests.cs`](Workflows/DryRunFailureTests.cs) | 7 | A strict-validation dry-run failure at request time blocks plan creation; a pre-apply dry-run failure at apply time blocks the mutation. |

Each `Workflows/*Tests.cs` file is one workflow class with one or two `[Fact]`s, decorated with `[Trait("Category", "SafetyE2E")]` and `[Collection(SafetyE2ECollection.Name)]`. The shared fixture (`SafetyE2EFixture`) boots Keycloak once per assembly and lazily spawns the McpServer subprocess on the first tool call.

## Prerequisites

You need **all four** of the following before any test will run:

1. **.NET 10 SDK** — required to build and run the test project. Verify with `dotnet --version` (must report 10.x).
2. **Docker** — required because the fixture starts a Keycloak container via Testcontainers. The Docker daemon must be running and your user must have permission to reach it (on Linux, that usually means being in the `docker` group). Verify with `docker info` returning without error.
3. **A Kubernetes cluster reachable through a kubeconfig** — required because the McpServer subprocess uses `KubernetesClient` to perform real `dryRun=All` calls and (in some tests) real mutations. Minikube, kind, k3d, Docker Desktop Kubernetes, and Rancher Desktop all work; a remote cluster also works if your kubeconfig points to it.
4. **The `INFRA_GATE_RUN_SAFETY_E2E=1` environment variable** — without it, every test in this project early-returns (it is not a runtime error, the suite simply reports 0 executed tests because each method opens with `if (!fixture.IsEnabled) return;`).

> If any of the above is missing the suite will either skip silently (env var unset) or fail in the fixture's `InitializeAsync` (Docker unavailable, kubeconfig unreachable).

## Step-by-step setup

These steps assume a fresh checkout. Run every command from the **repository root** unless stated otherwise.

### Step 1 — Install and verify .NET 10

```bash
dotnet --version
# Expect: 10.x.x
```

If the version is below 10.0, install the .NET 10 SDK from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download).

### Step 2 — Install and verify Docker

```bash
docker info
```

If the command errors with "Cannot connect to the Docker daemon", start the daemon (`sudo systemctl start docker` on Linux, or open Docker Desktop / Rancher Desktop on macOS/Windows).

On Linux, if you see "permission denied", add yourself to the `docker` group and re-log:

```bash
sudo usermod -aG docker "$USER"
# log out and back in (or run: newgrp docker)
docker info
```

### Step 3 — Bring up a Kubernetes cluster

If you already have a cluster, skip to Step 4 — just make sure your kubeconfig is current and reachable.

Otherwise, pick a local distribution. The repo's existing scripts assume minikube but any working cluster is fine.

**Minikube**
```bash
minikube start
kubectl cluster-info
```

**kind**
```bash
kind create cluster --name infra-gate-safety
kubectl cluster-info
```

Confirm `kubectl get nodes` returns a `Ready` node before continuing.

### Step 4 — Generate the demo kubeconfig

The tests default to the repo-local `.kube/mcp-nginx-demo.config` kubeconfig (a 24-hour service-account token scoped to the `mcp-nginx-demo` namespace). Generate it with:

```bash
./scripts/create-demo-kubeconfig.sh
```

This creates `.kube/mcp-nginx-demo.config` and applies [`deploy/minikube/rbac.yaml`](../../deploy/minikube/rbac.yaml), which provisions:

- The namespace `mcp-nginx-demo`
- A `ServiceAccount` with namespace-scoped `Role` and `RoleBinding`
- The verbs the McpServer needs (get/list/patch/delete on Deployments, Services, ConfigMaps, Pods, ReplicaSets, Events, Pod logs)

Confirm the kubeconfig works. **Do not use `kubectl get all`** — it queries resource types (`replicationcontrollers`, `daemonsets`, `statefulsets`, `hpa`, `cronjobs`, `jobs`) the namespace-scoped ServiceAccount intentionally cannot list, and prints scary `Forbidden` errors that look like setup failures but are correct behaviour. Use this targeted form (matches [docs/devs-readme.md:257](../../docs/devs-readme.md#L257)):

```bash
kubectl --kubeconfig .kube/mcp-nginx-demo.config -n mcp-nginx-demo \
  get deployment,service,configmap,pods,replicasets -o wide
```

Expect `No resources found in mcp-nginx-demo namespace.` at this point — the namespace exists and is reachable, but no workloads are deployed yet (that's Step 5).

You can also sanity-check the RBAC envelope with `auth can-i`:

```bash
kubectl --kubeconfig .kube/mcp-nginx-demo.config auth can-i create deployments -n mcp-nginx-demo
# expect: yes
kubectl --kubeconfig .kube/mcp-nginx-demo.config auth can-i create namespaces
# expect: no  (the SA is intentionally namespace-scoped)
```

> **Token expiry:** the generated token is valid for 24 hours. If the suite starts failing with `401 Unauthorized` from the Kubernetes API, re-run `./scripts/create-demo-kubeconfig.sh`.

If you use a different kubeconfig, export it before running tests:

```bash
export KUBECONFIG=/absolute/path/to/your/kubeconfig
```

The fixture reads `KUBECONFIG` first and falls back to `.kube/mcp-nginx-demo.config`.

### Step 5 — Deploy the demo Deployment the tests target

Most workflow tests (`PlanHashMismatchTests`, `ExpiredApprovalTests`, `AlreadyAppliedPlanTests`, `ModifiedPendingPlanTests`, `WrongUserApprovalTests`, and one of the `DryRunFailureTests`) call `request_restart_deployment` with `name=nginx-demo`. That Deployment must exist in the namespace before tests run, otherwise the request-time `dryRun=All` will return 404 from the Kubernetes API and plan creation will fail before any safety property is exercised.

Apply the repo's existing demo manifest:

```bash
kubectl --kubeconfig .kube/mcp-nginx-demo.config -n mcp-nginx-demo apply -f examples/failing-deployment/deployment.yaml
```

This creates `Deployment/nginx-demo` (and `Service/nginx-demo`) in the `mcp-nginx-demo` namespace. The Deployment's container image is intentionally a failing tag — that is fine for these tests because we only need the resource to exist for `dryRun=All` to succeed; we do not assert pod readiness.

Verify:

```bash
kubectl --kubeconfig .kube/mcp-nginx-demo.config -n mcp-nginx-demo get deployment nginx-demo
```

You should see:
```
# NAME         READY   UP-TO-DATE   AVAILABLE   AGE
# nginx-demo   0/2     2            0           …
```

### Step 6 — Build the solution

```bash
dotnet build InfraGate.slnx
```

Must finish with `Build succeeded. 0 Warning(s) 0 Error(s)`. If it does not, fix the build before continuing — the next step assumes the test assembly is already produced.

### Step 7 — Run the safety E2E suite

Set the opt-in env var and run the project filtered to the `SafetyE2E` category:

```bash
INFRA_GATE_RUN_SAFETY_E2E=1 \
KUBECONFIG="$(pwd)/.kube/mcp-nginx-demo.config" \
  dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj \
    --no-build \
    --filter "Category=SafetyE2E"
```

The first run takes longer than subsequent runs because:

- Testcontainers pulls the Keycloak image (`quay.io/keycloak/keycloak:26.2`) on first use.
- The gateway's `DownstreamMcpClient` does a `dotnet run --project src/InfraGate.McpServer/InfraGate.McpServer.csproj` on the first tool call, which restores and compiles the McpServer if not already compiled.

Expected output ends with a green summary similar to:

```
Passed!  - Failed:     0, Passed:    N, Skipped:     0, Total:    N, Duration: …
```

### Step 8 (optional) — Confirm the default test pass is unaffected

```bash
dotnet test InfraGate.slnx --no-build --filter "Category!=Keycloak&Category!=SafetyE2E"
```

These tests should pass as they do without `INFRA_GATE_RUN_SAFETY_E2E` set.

## Configuration knobs

| Environment variable | Default | Purpose |
|---|---|---|
| `INFRA_GATE_RUN_SAFETY_E2E` | _unset_ | Must equal `1` to opt-in. Anything else (including absent) makes every test early-return. |
| `KUBECONFIG` | `.kube/mcp-nginx-demo.config` | Kubernetes API target. The fixture sets it on the McpServer subprocess so the spawned process talks to the same cluster as your `kubectl`. |
| `K8S_MCP_ALLOWED_NAMESPACES` | `mcp-nginx-demo` | Namespace the McpServer is allowed to operate in. Change this only if you also created the matching `Deployment/nginx-demo` in a different namespace. |
| `K8S_MCP_APPROVAL_ROOT` | _set per-run by the fixture_ | Approval store path. The fixture creates a unique temp directory per run; do **not** set this manually or the McpServer will write to a place the fixture doesn't read for audit assertions. |

## Troubleshooting

### "No tests matched the given filter" / 0 tests executed

You forgot `INFRA_GATE_RUN_SAFETY_E2E=1`. Set it and re-run.

### Fixture init fails with `DockerApiException` / cannot reach Docker daemon

Docker is not running, or your user lacks permission. See Step 2.

### Fixture init fails pulling the Keycloak image

No internet access, or the image is gone from `quay.io/keycloak/keycloak:26.2`. Pre-pull it manually:

```bash
docker pull quay.io/keycloak/keycloak:26.2
```

### Tests fail at request time with `404` from Kubernetes

The `Deployment/nginx-demo` does not exist in the namespace `K8S_MCP_ALLOWED_NAMESPACES` points to. Re-do Step 5.

### Tests fail with `401 Unauthorized` from Kubernetes

The kubeconfig token expired (24-hour lifetime). Re-run Step 4.

### Tests fail with "Approval Root path is denied" / production safety errors at McpServer startup

The McpServer subprocess inherited `INFRA_GATE_ENVIRONMENT=Production` from your shell or a parent process. Unset it or set it to `Development`:

```bash
unset INFRA_GATE_ENVIRONMENT
# or
export INFRA_GATE_ENVIRONMENT=Development
```

### The McpServer subprocess never starts / first call hangs

The gateway's `DownstreamMcpClient` runs `dotnet run --project src/InfraGate.McpServer/...` on the first tool call. If that command fails (e.g. the McpServer csproj has a compile error after a recent change), the call hangs. Run it directly to surface the error:

```bash
dotnet run --project src/InfraGate.McpServer/InfraGate.McpServer.csproj
# (Ctrl-C after it finishes startup logging; you only care that it starts.)
```

### Audit assertions fail unexpectedly

The audit log is shared across all tests in one run (single approval root per fixture instance). If you suspect interference, run a single workflow at a time:

```bash
INFRA_GATE_RUN_SAFETY_E2E=1 \
  dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj \
    --no-build \
    --filter "FullyQualifiedName~PlanHashMismatchTests"
```

## CI considerations

Don't run these in the default CI test job. They require Docker-in-Docker (for Testcontainers) and a Kubernetes cluster, both of which are nontrivial to provide in shared runners. If you do wire them up, the recommended shape is:

- A separate workflow gated on a label or on `workflow_dispatch`.
- A self-hosted runner with Docker available and a kind cluster pre-created in a setup step.
- Apply [`deploy/minikube/rbac.yaml`](../../deploy/minikube/rbac.yaml) and [`examples/failing-deployment/deployment.yaml`](../../examples/failing-deployment/deployment.yaml) before the test step.
- Set `INFRA_GATE_RUN_SAFETY_E2E=1` and `KUBECONFIG` on the test step.
- Cache the Keycloak image to keep first-run latency reasonable.
