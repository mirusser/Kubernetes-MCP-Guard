#!/usr/bin/env bash
# local-integration-test.sh — guided, mostly-automated local run of the same
# path .github/workflows/integration-tests.yml exercises in CI: provision a
# working minikube cluster, apply the demo RBAC + workload, mint a demo
# kubeconfig, then run every test tier that infrastructure allows.
#
# Unlike CI's disposable runner, your local minikube cluster is a persistent,
# long-lived dev environment -- this script never stops or deletes it, and
# will ask before overwriting an nginx-demo Deployment that doesn't already
# look like the CI demo workload (e.g. a manually-applied example fixture).
#
# What this script does for you automatically:
#   - Checks for .NET, Docker, kubectl, minikube.
#   - Installs the pinned minikube binary if it's missing (via
#     install-minikube.sh) -- asks first, since that's a system-wide install.
#   - Starts minikube if it isn't already running.
#   - Applies deploy/minikube/rbac.yaml and (with confirmation, see above)
#     deploy/minikube/nginx-demo-workload.yaml.
#   - Regenerates the demo kubeconfig and delegates to run-tests.sh, which
#     auto-detects what's available and runs/skips tiers accordingly.
#
# What it will ask you about:
#   - Installing minikube system-wide, if missing.
#   - Overwriting an existing nginx-demo Deployment that doesn't match the
#     expected CI demo workload image.
#
# What it will NOT do for you (prints instructions and stops instead):
#   - Install Docker or kubectl. These need system package manager / daemon /
#     group-membership changes this script won't make unsupervised.
#
# Usage:
#   ./scripts/local-integration-test.sh

set -uo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
EXPECTED_WORKLOAD_IMAGE="nginx:1.27-alpine"

die() {
  echo -e "${RED}error:${NC} $1" >&2
  exit 1
}

confirm() {
  local prompt="$1"
  local default="${2:-y}"
  local suffix="[Y/n]"
  [[ "${default}" == "n" ]] && suffix="[y/N]"

  if [[ ! -t 0 ]]; then
    echo -e "  ${YELLOW}−${NC} Non-interactive shell; defaulting to '${default}' for: ${prompt}"
    [[ "${default}" == "y" ]]
    return
  fi

  local reply
  read -r -p "${prompt} ${suffix} " reply
  reply="${reply:-${default}}"
  [[ "${reply}" =~ ^[Yy] ]]
}

echo -e "${CYAN}=== local-integration-test.sh ===${NC}"
echo ""

# ────────────────── Prerequisite checks ──────────────────

echo -e "${CYAN}Checking prerequisites...${NC}"

if dotnet --version 2>/dev/null | grep -qE '^10\.'; then
  echo -e "  ${GREEN}✓${NC} .NET 10 SDK: $(dotnet --version)"
else
  die ".NET 10 SDK is required (found: $(dotnet --version 2>/dev/null || echo 'none')). See https://learn.microsoft.com/dotnet/core/install/linux"
fi

if command -v jq >/dev/null 2>&1; then
  echo -e "  ${GREEN}✓${NC} jq: $(command -v jq)"
else
  die "'jq' is required. Install it with your package manager (e.g. apt install jq / brew install jq)."
fi

if docker info >/dev/null 2>&1; then
  echo -e "  ${GREEN}✓${NC} Docker available"
else
  die "Docker is required and must be running. See https://docs.docker.com/engine/install/ (this script won't install it for you)."
fi

if command -v kubectl >/dev/null 2>&1; then
  echo -e "  ${GREEN}✓${NC} kubectl: $(command -v kubectl)"
else
  die "kubectl is required. See https://kubernetes.io/docs/tasks/tools/#kubectl (this script won't install it for you)."
fi

if command -v minikube >/dev/null 2>&1; then
  echo -e "  ${GREEN}✓${NC} minikube: $(minikube version --short 2>/dev/null || command -v minikube)"
else
  echo -e "  ${YELLOW}−${NC} minikube not found"
  if confirm "Install the pinned minikube version now (./scripts/install-minikube.sh)?"; then
    "${SCRIPT_DIR}/install-minikube.sh" || die "minikube install failed. See output above."
    echo -e "  ${GREEN}✓${NC} minikube installed: $(minikube version --short 2>/dev/null || command -v minikube)"
  else
    die "minikube is required. Install it yourself (https://minikube.sigs.k8s.io/docs/start/) and re-run."
  fi
fi

echo ""

# ────────────────── Ensure minikube is running ──────────────────

echo -e "${CYAN}Checking minikube cluster...${NC}"

if minikube status >/dev/null 2>&1; then
  echo -e "  ${GREEN}✓${NC} minikube already running"
else
  echo -e "  ${YELLOW}−${NC} minikube not running; starting it (this can take a couple of minutes)..."
  minikube start --driver=docker --wait=apiserver,system_pods --wait-timeout=5m \
    || die "minikube start failed. See output above."
  echo -e "  ${GREEN}✓${NC} minikube started"
fi

minikube update-context >/dev/null
kubectl config use-context minikube >/dev/null \
  || die "Could not switch kubectl to the 'minikube' context."
kubectl --request-timeout=10s get --raw=/readyz >/dev/null 2>&1 \
  || die "minikube API server is not ready."
kubectl wait --for=condition=Ready node --all --timeout=120s >/dev/null \
  || die "minikube node did not become Ready in time."

echo -e "  ${GREEN}✓${NC} minikube cluster is ready"
echo ""

# ────────────────── RBAC + demo workload ──────────────────

echo -e "${CYAN}Applying demo RBAC...${NC}"
kubectl apply --validate=false -f "${REPO_ROOT}/deploy/minikube/rbac.yaml" \
  || die "Could not apply deploy/minikube/rbac.yaml."
echo -e "  ${GREEN}✓${NC} RBAC applied"
echo ""

echo -e "${CYAN}Checking nginx-demo workload...${NC}"

APPLY_WORKLOAD=true
EXISTING_IMAGE="$(kubectl get deployment nginx-demo -n mcp-nginx-demo \
  -o jsonpath='{.spec.template.spec.containers[0].image}' 2>/dev/null || true)"

if [[ -n "${EXISTING_IMAGE}" && "${EXISTING_IMAGE}" != "${EXPECTED_WORKLOAD_IMAGE}" ]]; then
  echo -e "  ${YELLOW}−${NC} Existing nginx-demo Deployment in mcp-nginx-demo uses image '${EXISTING_IMAGE}', not the expected CI demo image '${EXPECTED_WORKLOAD_IMAGE}'."
  echo -e "  ${YELLOW}−${NC} This looks like a different fixture (e.g. examples/failing-deployment) rather than the CI demo workload."
  if confirm "Overwrite it with deploy/minikube/nginx-demo-workload.yaml?" "n"; then
    APPLY_WORKLOAD=true
  else
    APPLY_WORKLOAD=false
    echo -e "  ${YELLOW}−${NC} Leaving the existing Deployment in place; tests that depend on the CI demo workload's Ready pod may fail or hang."
  fi
elif [[ -n "${EXISTING_IMAGE}" ]]; then
  echo -e "  ${GREEN}✓${NC} Existing nginx-demo Deployment already matches the expected CI demo image"
fi

if [[ "${APPLY_WORKLOAD}" == "true" ]]; then
  kubectl apply -f "${REPO_ROOT}/deploy/minikube/nginx-demo-workload.yaml" \
    || die "Could not apply deploy/minikube/nginx-demo-workload.yaml."
  # kubectl wait on a pod label selector races the Deployment controller:
  # right after apply, no Pod object exists yet, so `wait` sees zero
  # matches and exits immediately with "no matching resources found"
  # instead of waiting. The Deployment itself is created synchronously by
  # apply, so wait on its rollout status instead.
  kubectl rollout status deployment/nginx-demo -n mcp-nginx-demo --timeout=120s \
    || die "nginx-demo Deployment did not become ready in time."
  echo -e "  ${GREEN}✓${NC} nginx-demo workload ready"
fi

echo ""

# ────────────────── Delegate to run-tests.sh ──────────────────

echo -e "${CYAN}Handing off to run-tests.sh for test execution...${NC}"
echo ""

exec "${SCRIPT_DIR}/run-tests.sh"
