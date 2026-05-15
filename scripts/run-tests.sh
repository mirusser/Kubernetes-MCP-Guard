#!/usr/bin/env bash
set -uo pipefail

# run-tests.sh — Auto-detect available infrastructure and run all possible test tiers.
# Skips tiers that need Docker or Kubernetes if those aren't available.
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

KUBECONFIG_FILE="${KUBECONFIG:-${REPO_ROOT}/.kube/mcp-nginx-demo.config}"

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

if docker info &>/dev/null; then
    echo -e "  ${GREEN}✓${NC} Docker available"
    DOCKER_OK=true
else
    echo -e "  ${YELLOW}−${NC} Docker not available — skipping Keycloak and Safety E2E tiers"
fi

DETECT_KUBECONFIG=""
if [ -f "$KUBECONFIG_FILE" ] && kubectl --kubeconfig "$KUBECONFIG_FILE" -n mcp-nginx-demo get deployment &>/dev/null; then
    DETECT_KUBECONFIG="$KUBECONFIG_FILE"
elif [ -n "${KUBECONFIG:-}" ] && kubectl -n mcp-nginx-demo get deployment &>/dev/null; then
    DETECT_KUBECONFIG="$KUBECONFIG"
fi

if [ -n "$DETECT_KUBECONFIG" ]; then
    echo -e "  ${GREEN}✓${NC} K8s cluster reachable via $DETECT_KUBECONFIG"
    KUBECONFIG_FILE="$DETECT_KUBECONFIG"
    K8S_OK=true
else
    echo -e "  ${YELLOW}−${NC} K8s cluster not reachable — skipping integration and Safety E2E tiers"
fi

if ! $DOTNET_OK; then
    echo ""
    echo -e "${RED}.NET 10 SDK is required. Install it and retry.${NC}"
    exit 1
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
}

# ────────────────── Tier 1: Unit Tests ──────────────────

run_tier \
    "Unit Tests" \
    'dotnet test InfraGate.slnx --filter "Category!=Keycloak"'

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
        'INFRA_GATE_RUN_INTEGRATION=1 dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj'
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
    run_tier \
        "Safety E2E" \
        "INFRA_GATE_RUN_SAFETY_E2E=1 KUBECONFIG=\"$KUBECONFIG_FILE\" dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj --filter \"Category=SafetyE2E\""
else
    if ! $DOCKER_OK; then
        SKIPPED_TIERS+=("Safety E2E (Docker not available)")
    else
        SKIPPED_TIERS+=("Safety E2E (K8s cluster not reachable)")
    fi
fi

# ────────────────── Summary ──────────────────

echo -e "${CYAN}=== Summary ===${NC}"

if [ ${#PASSED_TIERS[@]} -gt 0 ]; then
    echo -e "${GREEN}Passed:${NC}"
    for tier in "${PASSED_TIERS[@]}"; do
        echo -e "  ${GREEN}✓${NC} $tier"
    done
fi

if [ ${#FAILED_TIERS[@]} -gt 0 ]; then
    echo -e "${RED}Failed:${NC}"
    for tier in "${FAILED_TIERS[@]}"; do
        echo -e "  ${RED}✗${NC} $tier"
    done
fi

if [ ${#SKIPPED_TIERS[@]} -gt 0 ]; then
    echo -e "${YELLOW}Skipped:${NC}"
    for tier in "${SKIPPED_TIERS[@]}"; do
        echo -e "  ${YELLOW}−${NC} $tier"
    done
fi

echo ""
if [ "$FAILED" -eq 0 ]; then
    echo -e "${GREEN}All executed tests passed.${NC}"
else
    echo -e "${RED}Some tests failed. See output above.${NC}"
fi

exit $FAILED
