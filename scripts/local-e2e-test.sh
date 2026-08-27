#!/usr/bin/env bash
# local-e2e-test.sh — guided, mostly-automated local run of every test tier
# this repo has, including the real agentic Observer -> Planner -> Approval
# -> Executor remediation loop against live services and a real LLM call.
#
# Unlike CI's disposable runner, your local minikube cluster is a persistent,
# long-lived dev environment -- this script never stops or deletes it, and
# will ask before overwriting an nginx-demo Deployment that doesn't already
# look like the CI demo workload (e.g. a manually-applied example fixture).
#
# What this script does for you automatically:
#   - Checks for .NET, Docker, kubectl, minikube, curl.
#   - Installs the pinned minikube binary if it's missing (via
#     install-minikube.sh) -- asks first, since that's a system-wide install.
#   - Starts minikube if it isn't already running.
#   - Applies deploy/minikube/rbac.yaml and (with confirmation, see above)
#     deploy/minikube/nginx-demo-workload.yaml.
#   - Regenerates the demo kubeconfig.
#   - Checks for an OpenRouter API key (needed for the real agentic
#     remediation tiers) and, if missing, offers to prompt for one and save
#     it to dev-secrets.env for future runs.
#   - Starts the local docker-compose agentic stack (deploy/local-oauth/
#     compose.yaml) if it isn't already running, and waits for it to report
#     ready.
#   - Delegates to run-tests.sh, which auto-detects what's available and
#     runs/skips tiers accordingly. run-tests.sh itself re-applies the
#     broken examples/failing-deployment/deployment.yaml fixture immediately
#     before the Safety E2E and Remediation E2E tiers, so nginx-demo starts
#     each of those tiers broken regardless of what an earlier tier left it as.
#
# What it will ask you about:
#   - Installing minikube system-wide, if missing.
#   - Overwriting an existing nginx-demo Deployment that doesn't match the
#     expected CI demo workload image.
#   - Entering and persisting an OpenRouter API key, if one isn't already
#     configured. This key is used to make real, billed calls to OpenRouter
#     once the agentic remediation tiers run, so this step is always
#     confirmed explicitly before proceeding.
#
# What it will NOT do for you (prints instructions and stops instead):
#   - Install Docker or kubectl. These need system package manager / daemon /
#     group-membership changes this script won't make unsupervised.
#
# Usage:
#   ./scripts/local-e2e-test.sh

set -uo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
EXPECTED_WORKLOAD_IMAGE="nginx:1.27-alpine"
DEV_SECRETS_FILE="${REPO_ROOT}/dev-secrets.env"
DEV_SECRETS_EXAMPLE_FILE="${REPO_ROOT}/dev-secrets.env.example"
OPENROUTER_KEY_VAR="InfraGate__OpenRouter__ApiKey"
COMPOSE_FILE="${REPO_ROOT}/deploy/local-oauth/compose.yaml"
COMPOSE_ENV_FILE="${REPO_ROOT}/deploy/generated/local-compose.env"
COMPOSE_STACK_READY_TIMEOUT=300
COMPOSE_READYZ_URLS=(
  "http://127.0.0.1:3001/readyz" # mcp-gateway
  "http://127.0.0.1:3003/readyz" # observer
  "http://127.0.0.1:3004/readyz" # planner
  "http://127.0.0.1:3005/readyz" # executor
)

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

if [[ -f "${DEV_SECRETS_FILE}" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "${DEV_SECRETS_FILE}"
  set +a
fi

echo -e "${CYAN}=== local-e2e-test.sh ===${NC}"
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

if command -v curl >/dev/null 2>&1; then
  echo -e "  ${GREEN}✓${NC} curl: $(command -v curl)"
else
  die "'curl' is required. Install it with your package manager (e.g. apt install curl / brew install curl)."
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

# ────────────────── OpenRouter API key (for the agentic remediation tiers) ──────────────────

echo -e "${CYAN}Checking for an OpenRouter API key (needed for the real agentic remediation flow)...${NC}"

RUN_AGENTIC_STAGES=true

if [[ -n "${!OPENROUTER_KEY_VAR:-}" ]]; then
  echo -e "  ${GREEN}✓${NC} ${OPENROUTER_KEY_VAR} is set"
else
  echo -e "  ${YELLOW}−${NC} ${OPENROUTER_KEY_VAR} is not set."
  if confirm "Enter an OpenRouter API key now to run the real agentic remediation E2E tiers?"; then
    read -r -s -p "OpenRouter API key: " openrouter_key
    echo ""
    if [[ -z "${openrouter_key}" ]]; then
      echo -e "  ${YELLOW}−${NC} No key entered; skipping the agentic remediation stages."
      RUN_AGENTIC_STAGES=false
    else
      [[ -f "${DEV_SECRETS_FILE}" ]] || cp "${DEV_SECRETS_EXAMPLE_FILE}" "${DEV_SECRETS_FILE}"
      if grep -q "^${OPENROUTER_KEY_VAR}=" "${DEV_SECRETS_FILE}" 2>/dev/null; then
        sed -i.bak "s#^${OPENROUTER_KEY_VAR}=.*#${OPENROUTER_KEY_VAR}=${openrouter_key}#" "${DEV_SECRETS_FILE}"
        rm -f "${DEV_SECRETS_FILE}.bak"
      else
        echo "${OPENROUTER_KEY_VAR}=${openrouter_key}" >> "${DEV_SECRETS_FILE}"
      fi
      set -a
      # shellcheck disable=SC1090
      source "${DEV_SECRETS_FILE}"
      set +a
      echo -e "  ${GREEN}✓${NC} Saved to $(basename "${DEV_SECRETS_FILE}") for future runs"
    fi
  else
    echo -e "  ${YELLOW}−${NC} Skipping the agentic remediation stages (Remediation E2E and Observer E2E will report as skipped)."
    RUN_AGENTIC_STAGES=false
  fi
fi

if $RUN_AGENTIC_STAGES; then
  echo -e "  ${YELLOW}−${NC} Running the agentic remediation tiers starts the local docker-compose stack (if not already up) and makes real, billed OpenRouter calls."
  if ! confirm "Continue with the agentic remediation stages?"; then
    RUN_AGENTIC_STAGES=false
    echo -e "  ${YELLOW}−${NC} Skipping the agentic remediation stages (Remediation E2E and Observer E2E will report as skipped)."
  fi
fi

echo ""

# ────────────────── Local agentic stack (docker compose) ──────────────────

compose_stack_ready() {
  local url
  for url in "${COMPOSE_READYZ_URLS[@]}"; do
    curl --fail --silent --max-time 3 "$url" >/dev/null 2>&1 || return 1
  done
  return 0
}

if $RUN_AGENTIC_STAGES; then
  echo -e "${CYAN}Checking local agentic stack (docker compose)...${NC}"

  if [[ -f "${COMPOSE_ENV_FILE}" ]] && compose_stack_ready; then
    echo -e "  ${GREEN}✓${NC} Compose stack already running"
  else
    echo -e "  ${YELLOW}−${NC} Compose stack not running; generating env and starting it in the background (first build can take a few minutes)..."
    "${SCRIPT_DIR}/generate-env.sh" local-compose \
      || die "generate-env.sh failed. See output above."
    (cd "${REPO_ROOT}" && docker compose --env-file "${COMPOSE_ENV_FILE}" -f "${COMPOSE_FILE}" up -d --build) \
      || die "docker compose up failed. See output above."
  fi

  echo -e "  ${YELLOW}−${NC} Waiting for Gateway/Observer/Planner/Executor to report ready..."
  deadline=$((SECONDS + COMPOSE_STACK_READY_TIMEOUT))
  until compose_stack_ready; do
    if (( SECONDS >= deadline )); then
      die "Local agentic stack did not become ready within ${COMPOSE_STACK_READY_TIMEOUT}s. Check: docker compose --env-file ${COMPOSE_ENV_FILE} -f ${COMPOSE_FILE} logs"
    fi
    sleep 5
  done
  echo -e "  ${GREEN}✓${NC} Local agentic stack ready"
  echo ""
fi

# ────────────────── Delegate to run-tests.sh ──────────────────

echo -e "${CYAN}Handing off to run-tests.sh for test execution...${NC}"
echo ""

exec "${SCRIPT_DIR}/run-tests.sh"
