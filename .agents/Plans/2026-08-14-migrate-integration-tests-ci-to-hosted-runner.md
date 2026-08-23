# Migrate Integration Tests CI to a GitHub-Hosted Runner

**Date:** 2026-08-14
**Status:** Draft — awaiting review
**Target branch:** `feature/mcp-server` (or wherever it lands after that branch merges)

## Goal

Restore hosted-CI proof for `.github/workflows/integration-tests.yml` (the outstanding Task 18 / Checkpoint H gap from `.agents/Plans/2026-08-09-kubernetes-mcp-server-integration-hardening.md`) by dropping the self-hosted-runner dependency entirely and running the job on standard `ubuntu-latest` GitHub-hosted runners instead.

## Context

`integration-tests.yml` currently targets `runs-on: [self-hosted, linux]`, with `pull_request`/`push` triggers commented out because no runner is registered. Checked directly against the GitHub API this session:

```
gh api repos/:owner/:repo/actions/runners  →  {"total_count":0,"runners":[]}
```

This is a stronger finding than the workflow's own comment ("the only runner, vaporwave, as offline") — there is no runner registered at all, not merely an offline one. Nothing in the repo documents how "vaporwave" was provisioned (no setup script, no IaC), so there is no known recreation path for it even if we wanted one. The user has decided (2026-08-14) to migrate to hosted runners rather than investigate/restore it.

This repo already proves the core techniques work on `ubuntu-latest`:
- `.github/workflows/safety-e2e.yml` runs a live Kubernetes cluster (via `helm/kind-action`), applies RBAC, generates a service-account kubeconfig, and runs real `.NET` integration tests — all on `ubuntu-latest`.
- `docs/setup-guide.md:283` already documents `minikube start --driver=docker` as this project's standard local pattern, so using the same driver in CI is consistent with an existing convention, not a new one.

**Why minikube, not KinD (safety-e2e.yml's approach):** `scripts/create-demo-kubeconfig.sh:210` hard-fails unless the current kubectl context is literally named `minikube` (KinD's context is `kind-<cluster-name>`), and the workflow's own failure-diagnostics step calls `minikube logs` (`integration-tests.yml:115`). Both are used by other consumers of this script (local dev, `tests/InfraGate.Safety.E2E.Tests/README.md`), so reworking them to be cluster-tool-agnostic is out of scope here. Keeping minikube is the smaller, lower-risk diff.

**The real gap this plan closes:** `integration-tests.yml` never applies `deploy/minikube/rbac.yaml` directly (that happens inside `create-demo-kubeconfig.sh:224`) and — critically — **never applies an nginx-demo Deployment at all**. `GatewayKubernetesMcpServerIntegrationTests.cs:33` hard-asserts a pod named with prefix `nginx-demo-` exists and is running (it calls `pods_get`/`pods_log` against it, not just `pods_list`). The only reason the current self-hosted job passes is that "vaporwave" was a **persistent** host with a long-lived minikube profile someone set up by hand at some point — state an ephemeral GitHub-hosted runner will never have. `examples/failing-deployment/deployment.yaml` (used by `safety-e2e.yml`) is intentionally a failing image tag and won't produce a Ready pod with logs, so it cannot be reused as-is.

**Explicitly out of scope:** `publish.yml:180` also runs on `[self-hosted, linux]`, but that job deploys to the real "development" environment host (`docs/devs-readme.md:102`), not a disposable test cluster. It is a different runner for a different purpose and this plan does not touch it.

## Task List

### Phase 1: Reproducible ephemeral cluster state

#### Task 1: Commit a healthy nginx-demo Deployment manifest for CI

**Description:** Add a minimal, real (not intentionally-failing) `Deployment`/`Service` manifest for `nginx-demo` in the `mcp-nginx-demo` namespace, distinct from `examples/failing-deployment/deployment.yaml`. Use a real `nginx` image so the pod actually reaches `Running` and produces logs, matching what `GatewayKubernetesMcpServerIntegrationTests.cs` expects (`pods_get`, `pods_log` against a live pod, not just an existing-but-unready one).

**Acceptance criteria:**
- [ ] New manifest (e.g. `deploy/minikube/nginx-demo-workload.yaml`) defines `Deployment/nginx-demo` (real `nginx` image) and matching `Service/nginx-demo` in namespace `mcp-nginx-demo`.
- [ ] Applying it and waiting on `kubectl wait --for=condition=Ready pod -l app=nginx-demo -n mcp-nginx-demo` succeeds within a bounded timeout.

**Verification:**
- [ ] Manual: `kubectl apply -f deploy/minikube/nginx-demo-workload.yaml && kubectl wait --for=condition=Ready pod -l app=nginx-demo -n mcp-nginx-demo --timeout=120s` against a local minikube cluster.

**Dependencies:** None

**Files likely touched:**
- `deploy/minikube/nginx-demo-workload.yaml` (new)

**Estimated scope:** XS (1 file)

---

#### Task 2: Wire the demo workload into `integration-tests.yml`

**Description:** Add a step after "Ensure Minikube is running" (and before "Create demo kubeconfig", so RBAC from `create-demo-kubeconfig.sh` and the workload both exist before tests run) that applies Task 1's manifest and waits for the pod to be Ready.

**Acceptance criteria:**
- [ ] New step `kubectl apply -f deploy/minikube/nginx-demo-workload.yaml` + `kubectl wait --for=condition=Ready pod -l app=nginx-demo -n mcp-nginx-demo --timeout=120s` inserted between the existing "Ensure Minikube is running" and "Create demo kubeconfig" steps.

**Verification:**
- [ ] Covered by Phase 2's end-to-end `workflow_dispatch` runs (this step can't be meaningfully verified in isolation from the runner migration).

**Dependencies:** Task 1

**Files likely touched:**
- `.github/workflows/integration-tests.yml`

**Estimated scope:** XS (1 file)

### Checkpoint: Phase 1
- [ ] `deploy/minikube/nginx-demo-workload.yaml` applies cleanly and reaches Ready locally.
- [ ] Workflow diff for the new step reviewed (no change yet to `runs-on` — still self-hosted at this point, so this phase alone doesn't need a live CI run).

### Phase 2: Runner migration

#### Task 3: Install minikube on the runner and pin the driver

**Description:** GitHub-hosted `ubuntu-latest` runners do not have minikube preinstalled (unlike "vaporwave", which apparently did). Add an explicit install step, pinned to a specific minikube release with checksum verification — consistent with this project's existing convention for `kubernetes-mcp-server` acquisition (ADR-0033, `scripts/kubernetes-mcp-server.manifest.json`). Add `--driver=docker` explicitly to `minikube start` rather than relying on a host-level default (Docker is preinstalled on `ubuntu-latest` images).

**Acceptance criteria:**
- [ ] New step installs a pinned minikube version (e.g. `minikube-linux-amd64`) and verifies its SHA-256 against a checksum recorded in the workflow or a small manifest file, failing closed on mismatch.
- [ ] `minikube start` in `integration-tests.yml:73` gains `--driver=docker`.

**Verification:**
- [ ] Covered by the Checkpoint below (first live `workflow_dispatch` run).

**Dependencies:** None (parallel with Task 1/2)

**Files likely touched:**
- `.github/workflows/integration-tests.yml`

**Estimated scope:** S (1 file)

---

#### Task 4: Switch `runs-on` to `ubuntu-latest`

**Description:** Replace `runs-on: [self-hosted, linux]` (`integration-tests.yml:43-45`) with `runs-on: ubuntu-latest`, matching every other test workflow in this repo (`ci.yml`, `keycloak-tests.yml`, `safety-e2e.yml`, `dependency-scan.yml`, `semgrep.yml`, `sonar.yml`).

**Acceptance criteria:**
- [ ] `runs-on: ubuntu-latest` replaces the self-hosted label array.

**Verification:**
- [ ] Covered by the Checkpoint below.

**Dependencies:** Tasks 1–3 (cluster provisioning and minikube install must be in place before this is meaningfully testable)

**Files likely touched:**
- `.github/workflows/integration-tests.yml`

**Estimated scope:** XS (1 file)

### Checkpoint: Phase 2 — first live run
- [ ] Trigger the workflow manually via `workflow_dispatch` on a branch with Tasks 1–4 applied.
- [ ] Confirm: minikube starts, RBAC + nginx-demo pod provision successfully, `INFRA_GATE_RUN_INTEGRATION=1` McpServer tests pass, `INFRA_GATE_REQUIRE_GATEWAY_INTEGRATION=1` Gateway tests pass (must show real `nginx-demo-*` pod data, not an empty list — this is the actual Task 18 proof).
- [ ] Re-run at least once more to check for first-run-only flakiness (cold image pulls, timing) before trusting the timeouts already in the workflow (`--wait-timeout=5m`, `--timeout=120s`).
- [ ] If flaky: widen the specific timeout that failed rather than adding retries — keep the failure mode legible.
- [ ] Human review before proceeding to Phase 3 (this is the point where the job starts costing real GitHub Actions minutes on every push/PR once triggers are re-enabled).

### Phase 3: Enable automatic triggers and clean up

#### Task 5: Restore `pull_request`/`push` triggers and update the stale comment

**Description:** Uncomment the `pull_request`/`push` blocks in `integration-tests.yml:12-28` (path filters are already correct per the existing comment — no change needed there) and rewrite the header comment (`integration-tests.yml:3-11`) to describe the hosted-runner setup instead of the "vaporwave offline" situation.

**Acceptance criteria:**
- [ ] `pull_request` and `push` triggers active with their existing path filters.
- [ ] Header comment no longer references "vaporwave" or an offline self-hosted runner.

**Verification:**
- [ ] Open a real PR touching a path in the filter list; confirm the job fires and passes.

**Dependencies:** Phase 2 checkpoint signed off

**Files likely touched:**
- `.github/workflows/integration-tests.yml`

**Estimated scope:** XS (1 file)

---

#### Task 6: Reconcile plan status docs

**Description:** Update `.agents/Plans/2026-08-09-kubernetes-mcp-server-integration-hardening.md`'s Status line and Task 18/Checkpoint H notes to point at this remediation plan's completion instead of describing the runner as blocked/offline.

**Acceptance criteria:**
- [ ] Status line reflects that hosted-CI proof is restored, with a reference to this plan.

**Verification:**
- [ ] Manual review.

**Dependencies:** Task 5

**Files likely touched:**
- `.agents/Plans/2026-08-09-kubernetes-mcp-server-integration-hardening.md`

**Estimated scope:** XS (1 file)

### Checkpoint: Complete
- [ ] `pull_request`/`push` triggers live and passing on a real PR.
- [ ] No remaining reference to the self-hosted "vaporwave" runner in `integration-tests.yml`.
- [ ] Original plan's Task 18 / Checkpoint H marked resolved with a pointer to this plan.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Ephemeral runners have no image cache; cold `minikube start` / image pulls are slower and more variable than a warm persistent host | Med | Existing generous timeouts (`--wait-timeout=5m`) likely already absorb this; re-run the Phase 2 checkpoint a few times to confirm before enabling automatic triggers |
| `ubuntu-latest` standard runner resources (4 vCPU / 16 GB RAM on public repos) run a single-node minikube, an nginx pod, and the full `.NET` build+test suite sequentially in one job | Low | Same job already builds/tests today on self-hosted with unknown-but-presumably-comparable specs; sequential (not parallel) steps mean peak concurrent resource use is bounded to one phase at a time |
| Minikube's docker driver occasionally has cgroup/permission quirks on unfamiliar hosts | Low | `docs/setup-guide.md` already documents `--driver=docker` as the supported local pattern; GitHub's `ubuntu-latest` image ships Docker pre-configured for the `runner` user, same shape as a typical dev machine |
| New nginx-demo workload manifest could drift from what `GatewayKubernetesMcpServerIntegrationTests.cs` expects if the test's pod-name-prefix assumption ever changes | Low | Both live in this repo under version control; a future test change and a manifest change would show up in the same PR diff |

## Open Questions

- Should the pinned minikube version/checksum live inline in the workflow, or in a small `scripts/minikube.manifest.json` mirroring the existing `kubernetes-mcp-server.manifest.json` pattern? Leaning toward the latter for consistency, but it's a Task 3 implementation detail, not a blocking decision.
- Is there any GitHub Actions secret, environment, or variable currently scoped only to the "vaporwave" self-hosted runner label that should be cleaned up? Not found in this repo's workflow files, but GitHub-side repo/environment secrets aren't visible from the checkout — worth a quick manual check in repo Settings before calling Task 6 fully done.
