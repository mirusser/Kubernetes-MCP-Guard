#!/usr/bin/env bash
set -uo pipefail

# run-tests.sh — Auto-detect available infrastructure and run all possible test tiers.
# Regenerates the test kubeconfig with a fresh 24h SA token before running
# K8s-dependent tiers. Skips tiers that need Docker, Kubernetes, or the local
# agentic stack (deploy/local-oauth/compose.yaml) if those aren't available.
# Reports what ran, what passed, and what was skipped.

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

PASSED_TIERS=()
FAILED_TIERS=()
SKIPPED_TIERS=()
FAILED=0

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo -e "${CYAN}=== run-tests.sh ===${NC}"
echo ""

# ────────────────── Prerequisite checks ──────────────────

echo -e "${CYAN}Checking prerequisites...${NC}"

DOTNET_OK=false
DOCKER_OK=false
K8S_OK=false

if dotnet --version 2>/dev/null | grep -qE '^10\.'; then
    echo -e "  ${GREEN}✓${NC} .NET 10 SDK: $(dotnet --version)"
    DOTNET_OK=true
else
    echo -e "  ${RED}✗${NC} .NET 10 SDK not found (found: $(dotnet --version 2>/dev/null || echo 'none'))"
fi

if ! $DOTNET_OK; then
    echo ""
    echo -e "${RED}.NET 10 SDK is required. Install it and retry.${NC}"
    exit 1
fi

if docker info &>/dev/null; then
    echo -e "  ${GREEN}✓${NC} Docker available"
    DOCKER_OK=true
else
    echo -e "  ${YELLOW}−${NC} Docker not available — skipping Keycloak and Safety E2E tiers"
fi

# ────────────────── Kubeconfig regeneration ──────────────────

# Always regenerate the test kubeconfig so credentials never expire.
# This creates a fresh 24h SA token and applies RBAC if needed.
KUBECONFIG_REGENERATED=false
echo ""
echo -e "${CYAN}Regenerating test kubeconfig...${NC}"

if "${SCRIPT_DIR}/create-demo-kubeconfig.sh" >/dev/null 2>&1; then
    echo -e "  ${GREEN}✓${NC} Kubeconfig regenerated"
    KUBECONFIG_REGENERATED=true
else
    echo -e "  ${YELLOW}−${NC} Could not regenerate kubeconfig (cluster unreachable or kubectl unavailable)"
fi

if $DOCKER_OK && $KUBECONFIG_REGENERATED; then
    "${SCRIPT_DIR}/create-demo-kubeconfig.sh" --compose >/dev/null 2>&1 || true
    echo -e "  ${GREEN}✓${NC} Compose kubeconfig regenerated"
fi

# Derive the test kubeconfig path from the run profile so it stays in sync with
# deploy/run-profiles.yaml rather than being hardcoded here.
PROFILE_KUBECONFIG=""
if $DOTNET_OK; then
    mkdir -p "${REPO_ROOT}/deploy/generated"
    if dotnet run --project "${REPO_ROOT}/src/InfraGate.RunProfiles" -- generate test-integration \
        --output "${REPO_ROOT}/deploy/generated/test-integration.env" >/dev/null 2>&1; then
        raw_kube="$(grep '^InfraGate__Kubernetes__KubeConfig=' "${REPO_ROOT}/deploy/generated/test-integration.env" 2>/dev/null | cut -d= -f2-)"
        if [[ -n "$raw_kube" && "$raw_kube" != /* ]]; then
            PROFILE_KUBECONFIG="${REPO_ROOT}/${raw_kube}"
        else
            PROFILE_KUBECONFIG="$raw_kube"
        fi
    fi
fi
KUBECONFIG_FILE="${KUBECONFIG:-${PROFILE_KUBECONFIG:-${REPO_ROOT}/.kube/mcp-nginx-demo.config}}"

DETECT_KUBECONFIG=""
if [[ -f "$KUBECONFIG_FILE" ]] && kubectl --kubeconfig "$KUBECONFIG_FILE" -n mcp-nginx-demo get deployment &>/dev/null; then
    DETECT_KUBECONFIG="$KUBECONFIG_FILE"
elif [[ -n "${KUBECONFIG:-}" ]] && kubectl -n mcp-nginx-demo get deployment &>/dev/null; then
    DETECT_KUBECONFIG="$KUBECONFIG"
fi

if [[ -n "$DETECT_KUBECONFIG" ]]; then
    echo -e "  ${GREEN}✓${NC} K8s cluster reachable via $DETECT_KUBECONFIG"
    KUBECONFIG_FILE="$DETECT_KUBECONFIG"
    K8S_OK=true
else
    if $KUBECONFIG_REGENERATED; then
        echo -e "  ${YELLOW}−${NC} K8s cluster reachable for admin but SA token access failed — skipping integration and Safety E2E tiers"
    else
        echo -e "  ${YELLOW}−${NC} K8s cluster not reachable — skipping integration and Safety E2E tiers"
    fi
fi

echo ""

# ────────────────── Local agentic stack (compose) readiness ──────────────────

COMPOSE_STACK_OK=false
COMPOSE_READYZ_URLS=(
    "http://127.0.0.1:3001/readyz" # mcp-gateway
    "http://127.0.0.1:3003/readyz" # observer
    "http://127.0.0.1:3004/readyz" # planner
    "http://127.0.0.1:3005/readyz" # executor
)

compose_stack_ready() {
    local url
    for url in "${COMPOSE_READYZ_URLS[@]}"; do
        curl --fail --silent --max-time 3 "$url" >/dev/null 2>&1 || return 1
    done
    return 0
}

if compose_stack_ready; then
    echo -e "  ${GREEN}✓${NC} Local agentic stack reachable (Gateway/Observer/Planner/Executor)"
    COMPOSE_STACK_OK=true
else
    echo -e "  ${YELLOW}−${NC} Local agentic stack not reachable — skipping Remediation E2E and Observer E2E tiers"
fi

echo ""

# ────────────────── Helper ──────────────────

run_tier() {
    local tier_name="$1"
    local command="$2"
    local workdir="${3:-$REPO_ROOT}"

    echo -e "${CYAN}━━━ Tier: ${tier_name} ━━━${NC}"
    if (cd "$workdir" && eval "$command"); then
        echo -e "${GREEN}  ✓ PASSED${NC}"
        PASSED_TIERS+=("$tier_name")
    else
        echo -e "${RED}  ✗ FAILED${NC}"
        FAILED_TIERS+=("$tier_name")
        FAILED=1
    fi
    echo ""
    return 0
}

prepare_nginx_demo_fixture() {
    local kubeconfig_file="$1"

    echo -e "${CYAN}Preparing nginx-demo broken-deployment fixture...${NC}"
    if kubectl --kubeconfig "$kubeconfig_file" apply -f "${REPO_ROOT}/examples/failing-deployment/deployment.yaml" >/dev/null 2>&1; then
        echo -e "  ${GREEN}✓${NC} nginx-demo broken Deployment applied"
        echo ""
        return 0
    fi

    echo -e "  ${RED}✗${NC} Could not apply nginx-demo broken Deployment"
    echo ""
    return 1
}

# ────────────────── Tier 1: Unit Tests ──────────────────

run_tier \
    "Unit Tests" \
    'dotnet test InfraGate.slnx --filter "Category!=Keycloak&Category!=SafetyE2E"'

# ────────────────── Tier 2: Keycloak Integration ──────────────────

if $DOCKER_OK; then
    run_tier \
        "Keycloak Integration" \
        'dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"'
else
    SKIPPED_TIERS+=("Keycloak Integration (Docker not available)")
fi

# ────────────────── Tier 3: McpServer Integration ──────────────────

if $K8S_OK; then
    run_tier \
        "McpServer Integration" \
        "INFRA_GATE_RUN_INTEGRATION=1 KUBECONFIG=\"$KUBECONFIG_FILE\" dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj"
else
    SKIPPED_TIERS+=("McpServer Integration (K8s cluster not reachable)")
fi

# ────────────────── Tier 4: Gateway Integration ──────────────────

if $K8S_OK; then
    run_tier \
        "Gateway Integration" \
        'INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "Category!=Keycloak"'
else
    SKIPPED_TIERS+=("Gateway Integration (K8s cluster not reachable)")
fi

# ────────────────── Tier 5: Safety E2E ──────────────────

if $DOCKER_OK && $K8S_OK; then
    if prepare_nginx_demo_fixture "$KUBECONFIG_FILE"; then
        run_tier \
            "Safety E2E" \
            "INFRA_GATE_RUN_SAFETY_E2E=1 KUBECONFIG=\"$KUBECONFIG_FILE\" dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj --filter \"Category=SafetyE2E\""
    else
        FAILED_TIERS+=("Safety E2E setup (demo Deployment unavailable)")
        FAILED=1
    fi
else
    if ! $DOCKER_OK; then
        SKIPPED_TIERS+=("Safety E2E (Docker not available)")
    else
        SKIPPED_TIERS+=("Safety E2E (K8s cluster not reachable)")
    fi
fi

# ────────────────── Tier 6: Remediation E2E ──────────────────

# Tier 5 immediately above may leave nginx-demo fixed as a side effect of its own
# approval/execution flow, so re-apply the broken fixture before this tier expects
# to find it broken again.
if $COMPOSE_STACK_OK && $K8S_OK; then
    if prepare_nginx_demo_fixture "$KUBECONFIG_FILE"; then
        run_tier \
            "Remediation E2E" \
            "INFRA_GATE_RUN_REMEDIATION_E2E=1 INFRA_GATE_OBSERVER_REAL_LLM=1 KUBECONFIG=\"$KUBECONFIG_FILE\" dotnet test tests/InfraGate.Remediation.E2E.Tests/InfraGate.Remediation.E2E.Tests.csproj --filter \"Category=RemediationE2E\""
    else
        FAILED_TIERS+=("Remediation E2E setup (demo Deployment unavailable)")
        FAILED=1
    fi
else
    if ! $COMPOSE_STACK_OK; then
        SKIPPED_TIERS+=("Remediation E2E (local agentic stack not reachable)")
    else
        SKIPPED_TIERS+=("Remediation E2E (K8s cluster not reachable)")
    fi
fi

# ────────────────── Tier 6a: Observer E2E ──────────────────

if $COMPOSE_STACK_OK; then
    run_tier \
        "Observer E2E" \
        'INFRA_GATE_RUN_OBSERVER_E2E=1 INFRA_GATE_OBSERVER_REAL_LLM=1 dotnet test tests/InfraGate.Observer.E2E.Tests/InfraGate.Observer.E2E.Tests.csproj --filter "Category=ObserverE2E"'
else
    SKIPPED_TIERS+=("Observer E2E (local agentic stack not reachable)")
fi

# ────────────────── Summary ──────────────────

echo -e "${CYAN}=== Summary ===${NC}"

if [[ ${#PASSED_TIERS[@]} -gt 0 ]]; then
    echo -e "${GREEN}Passed:${NC}"
    for tier in "${PASSED_TIERS[@]}"; do
        echo -e "  ${GREEN}✓${NC} $tier"
    done
fi

if [[ ${#FAILED_TIERS[@]} -gt 0 ]]; then
    echo -e "${RED}Failed:${NC}"
    for tier in "${FAILED_TIERS[@]}"; do
        echo -e "  ${RED}✗${NC} $tier"
    done
fi

if [[ ${#SKIPPED_TIERS[@]} -gt 0 ]]; then
    echo -e "${YELLOW}Skipped:${NC}"
    for tier in "${SKIPPED_TIERS[@]}"; do
        echo -e "  ${YELLOW}−${NC} $tier"
    done
fi

echo ""
if [[ "$FAILED" -eq 0 ]]; then
    echo -e "${GREEN}All executed tests passed.${NC}"
else
    echo -e "${RED}Some tests failed. See output above.${NC}"
fi

exit $FAILED
