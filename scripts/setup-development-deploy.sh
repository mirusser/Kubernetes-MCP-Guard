#!/usr/bin/env bash
# setup-development-deploy.sh — prepares the local machine for deploy-development.
#
# Creates:
#   /etc/infra-gate/development.env       - gateway runtime environment
#   /etc/infra-gate/development.kubeconfig - Kubernetes access for the gateway
#   /var/lib/infra-gate/development/       - approvals + guardrails persistence
#
# Usage:
#   sudo ./scripts/setup-development-deploy.sh
#   sudo KEYCLOAK_PORT=3010 INFRA_GATE_GATEWAY_IMAGE=ghcr.io/... ./scripts/setup-development-deploy.sh
#
# Prerequisites:
#   - minikube running (kubeconfig auto-generated via create-demo-kubeconfig.sh)
#   - Docker, Docker Compose v2, curl, kubectl
#
# This script starts/updates the local Keycloak instance from
# deploy/compose/keycloak.yaml and configures the gateway to use it.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

KEYCLOAK_PORT="${KEYCLOAK_PORT:-3010}"
GATEWAY_PORT="${GATEWAY_PORT:-3001}"

# Container-to-host networking: the gateway container reaches Keycloak on the
# host via the Docker bridge IP. Defaults to the common docker0 address.
docker_host_ip() {
  ip -4 addr show docker0 2>/dev/null \
    | awk '/inet / {print $2}' \
    | cut -d/ -f1 \
    | head -n1
}

DOCKER_HOST_IP="$(docker_host_ip)"
DOCKER_HOST_IP="${DOCKER_HOST_IP:-172.17.0.1}"
KEYCLOAK_BIND_ADDRESS="${KEYCLOAK_BIND_ADDRESS:-0.0.0.0}"

OAUTH_AUTHORITY="http://127.0.0.1:${KEYCLOAK_PORT}/realms/infra-gate"
OAUTH_RESOURCE="http://127.0.0.1:${GATEWAY_PORT}/mcp"
OAUTH_METADATA_ADDRESS="http://${DOCKER_HOST_IP}:${KEYCLOAK_PORT}/realms/infra-gate/.well-known/openid-configuration"
TOKEN_ENDPOINT="http://${DOCKER_HOST_IP}:${KEYCLOAK_PORT}/realms/infra-gate/protocol/openid-connect/token"
AUTH_ENDPOINT="http://127.0.0.1:${KEYCLOAK_PORT}/realms/infra-gate/protocol/openid-connect/auth"
APPROVAL_BASE_URL="http://127.0.0.1:${GATEWAY_PORT}"
KEYCLOAK_DISCOVERY_URL="http://127.0.0.1:${KEYCLOAK_PORT}/realms/infra-gate/.well-known/openid-configuration"

INFRA_GATE_GATEWAY_IMAGE="${INFRA_GATE_GATEWAY_IMAGE:-ghcr.io/mirusser/kubernetes-mcp-guard-gateway}"
RUNNER_USER="${RUNNER_USER:-${SUDO_USER:-}}"

DEPLOY_PATH="${DEPLOY_PATH:-/opt/infra-gate}"
ENV_FILE="/etc/infra-gate/development.env"
KUBECONFIG_FILE="/etc/infra-gate/development.kubeconfig"
APPROVAL_DIR="/var/lib/infra-gate/development/approvals"
GUARDRAIL_DIR="/var/lib/infra-gate/development/guardrails"
KEYCLOAK_COMPOSE_FILE="$REPO_ROOT/deploy/compose/keycloak.yaml"

usage() {
  cat <<EOF
Usage: sudo $0

Prepares the local machine for the deploy-development GitHub Actions job.

Configures OAuth for a local Keycloak instance at
http://127.0.0.1:${KEYCLOAK_PORT}/realms/infra-gate

Environment overrides:
  KEYCLOAK_PORT       Keycloak host port (default: ${KEYCLOAK_PORT})
  KEYCLOAK_BIND_ADDRESS
                      Keycloak host bind address (default: ${KEYCLOAK_BIND_ADDRESS})
  GATEWAY_PORT        Gateway host port (default: ${GATEWAY_PORT})
  INFRA_GATE_GATEWAY_IMAGE
                      Gateway Docker image (default: ${INFRA_GATE_GATEWAY_IMAGE})
  DEPLOY_PATH         Compose file directory (default: ${DEPLOY_PATH})
  RUNNER_USER         User that runs the self-hosted runner (default: sudo user)

Creates:
  ${ENV_FILE}
  ${KUBECONFIG_FILE}
  ${APPROVAL_DIR}
  ${GUARDRAIL_DIR}
EOF
}

if [[ $# -gt 0 ]]; then
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
fi

# ── pre-checks ────────────────────────────────────────────────────────────────

if [[ "$(id -u)" -ne 0 ]]; then
  echo "ERROR: This script must be run as root (writes to /etc and /var/lib)." >&2
  echo "  sudo $0" >&2
  exit 1
fi

command -v docker >/dev/null 2>&1 || {
  echo "ERROR: docker is required." >&2; exit 1
}

docker compose version >/dev/null 2>&1 || {
  echo "ERROR: Docker Compose v2 is required." >&2; exit 1
}

command -v curl >/dev/null 2>&1 || {
  echo "ERROR: curl is required." >&2; exit 1
}

command -v kubectl >/dev/null 2>&1 || {
  echo "ERROR: kubectl is required." >&2; exit 1
}

if [[ "$GATEWAY_PORT" != "3001" ]]; then
  echo "ERROR: GATEWAY_PORT must be 3001 for the bundled Keycloak realm." >&2
  echo "  Update deploy/keycloak/infra-gate-realm.json first if you need a different gateway port." >&2
  exit 1
fi

if [[ -n "$RUNNER_USER" ]] && ! id "$RUNNER_USER" >/dev/null 2>&1; then
  echo "ERROR: RUNNER_USER does not exist: $RUNNER_USER" >&2
  exit 1
fi

if [[ -n "$RUNNER_USER" && "$RUNNER_USER" != "root" ]] && ! command -v runuser >/dev/null 2>&1; then
  echo "ERROR: runuser is required when RUNNER_USER is not root." >&2
  exit 1
fi

# ── create directories ────────────────────────────────────────────────────────

mkdir -p "$DEPLOY_PATH"
mkdir -p "$(dirname "$ENV_FILE")"
mkdir -p "$APPROVAL_DIR"
mkdir -p "$GUARDRAIL_DIR"

if [[ -n "$RUNNER_USER" ]]; then
  chown "$RUNNER_USER:" "$DEPLOY_PATH"
fi

# Demo-only bind mounts: the container's non-root .NET app user (UID 1654)
# needs to write approval/guardrail files even when the host user owns
# these directories. Use world-writable + sticky bit, like /tmp.
chmod 1777 "$APPROVAL_DIR" "$GUARDRAIL_DIR"

# ── runtime env file ──────────────────────────────────────────────────────────

cat > "$ENV_FILE" <<ENVEOF
# Generated by setup-development-deploy.sh — $(date)

# ── Docker Compose interpolation values ───────────────────────────────────────
INFRA_GATE_GATEWAY_IMAGE=${INFRA_GATE_GATEWAY_IMAGE}
INFRA_GATE_BIND_ADDRESS=127.0.0.1
INFRA_GATE_BIND_PORT=${GATEWAY_PORT}
INFRA_GATE_KUBECONFIG_HOST_PATH=${KUBECONFIG_FILE}
INFRA_GATE_APPROVAL_HOST_PATH=${APPROVAL_DIR}
INFRA_GATE_GUARD_AUDIT_HOST_PATH=${GUARDRAIL_DIR}

# ── Gateway runtime values ────────────────────────────────────────────────────
INFRA_GATE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://0.0.0.0:3001
INFRA_GATE_DOWNSTREAM_ASSEMBLY=/app/server/InfraGate.McpServer.dll

# ── OAuth (Keycloak) ──────────────────────────────────────────────────────────
INFRA_GATE_OAUTH_AUTHORITY=${OAUTH_AUTHORITY}
INFRA_GATE_OAUTH_RESOURCE=${OAUTH_RESOURCE}
INFRA_GATE_OAUTH_SCOPE=mcp:tools
INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false
INFRA_GATE_OAUTH_METADATA_ADDRESS=${OAUTH_METADATA_ADDRESS}

# ── Approval UI OAuth (Keycloak) ──────────────────────────────────────────────
INFRA_GATE_APPROVAL_BASE_URL=${APPROVAL_BASE_URL}
INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID=infra-gate-approval-ui
INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH=/approvals/oauth/callback
INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT=${AUTH_ENDPOINT}
INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT=${TOKEN_ENDPOINT}

# ── Kubernetes ────────────────────────────────────────────────────────────────
KUBECONFIG=/run/kube/infra-gate.config
K8S_MCP_ALLOWED_NAMESPACES=mcp-nginx-demo
INFRA_GATE_GUARD_AUDIT_ROOT=/data/guardrails
K8S_MCP_APPROVAL_ROOT=/data/approvals
ENVEOF

chmod 600 "$ENV_FILE"
if [[ -n "$RUNNER_USER" ]]; then
  chown "$RUNNER_USER:" "$ENV_FILE"
fi
echo "Created $ENV_FILE"

# ── kubeconfig ────────────────────────────────────────────────────────────────

echo "Generating kubeconfig via create-demo-kubeconfig.sh ..."
if [[ -n "$RUNNER_USER" && "$RUNNER_USER" != "root" ]]; then
  mkdir -p "$REPO_ROOT/.kube"
  chown -R "$RUNNER_USER:" "$REPO_ROOT/.kube"
  runuser -u "$RUNNER_USER" -- "$REPO_ROOT/scripts/create-demo-kubeconfig.sh"
else
  "$REPO_ROOT/scripts/create-demo-kubeconfig.sh"
fi

KUBECONFIG_SRC="$REPO_ROOT/.kube/mcp-nginx-demo.config"
if [[ -f "$KUBECONFIG_SRC" ]]; then
  cp "$KUBECONFIG_SRC" "$KUBECONFIG_FILE"
  chmod 600 "$KUBECONFIG_FILE"
  chown 1654:1654 "$KUBECONFIG_FILE"
  echo "Copied $KUBECONFIG_SRC to $KUBECONFIG_FILE"
else
  echo "WARNING: kubeconfig not found at $KUBECONFIG_SRC." >&2
  echo "  You can create one with: scripts/create-demo-kubeconfig.sh" >&2
  echo "  Then copy it to: $KUBECONFIG_FILE" >&2
fi

# ── Keycloak ──────────────────────────────────────────────────────────────────

echo "Starting local Keycloak via $KEYCLOAK_COMPOSE_FILE ..."
KEYCLOAK_BIND_ADDRESS="$KEYCLOAK_BIND_ADDRESS" \
KEYCLOAK_PORT="$KEYCLOAK_PORT" \
docker compose -f "$KEYCLOAK_COMPOSE_FILE" up -d keycloak

echo "Waiting for Keycloak discovery at $KEYCLOAK_DISCOVERY_URL ..."
elapsed=0
while true; do
  if curl -fsS "$KEYCLOAK_DISCOVERY_URL" >/dev/null; then
    break
  fi

  if [[ "$elapsed" -ge 120 ]]; then
    echo "ERROR: Keycloak did not become ready within 120 seconds." >&2
    KEYCLOAK_BIND_ADDRESS="$KEYCLOAK_BIND_ADDRESS" \
    KEYCLOAK_PORT="$KEYCLOAK_PORT" \
    docker compose -f "$KEYCLOAK_COMPOSE_FILE" logs --tail=50 keycloak >&2 || true
    exit 1
  fi

  sleep 2
  elapsed=$((elapsed + 2))
done
echo "Keycloak is ready."

# ── summary ───────────────────────────────────────────────────────────────────

echo ""
echo "=== Setup complete =========================================="
echo ""
echo "Deploy path:     $DEPLOY_PATH"
echo "Env file:        $ENV_FILE"
echo "Kubeconfig:      $KUBECONFIG_FILE"
echo "Approvals:       $APPROVAL_DIR"
echo "Guard audit:     $GUARDRAIL_DIR"
echo ""
echo "Keycloak base:   $OAUTH_AUTHORITY"
echo "  (container reaches Keycloak at $DOCKER_HOST_IP:$KEYCLOAK_PORT)"
echo "Gateway image:   $INFRA_GATE_GATEWAY_IMAGE"
echo ""
echo "=== GitHub Configuration ===================================="
echo ""
echo "In repo Settings → Environments → 'development', optionally add:"
echo ""
echo "  Environment variable:"
echo "    DEPLOY_PATH = $DEPLOY_PATH  # defaults to /opt/infra-gate in the workflow"
echo ""
echo "No secrets are required (the job uses \${{ secrets.GITHUB_TOKEN }})."
echo ""
echo "=== Next Steps =============================================="
echo ""
echo "1. Verify Keycloak is reachable from the container network:"
echo "     docker run --rm alpine/curl -s http://${DOCKER_HOST_IP}:${KEYCLOAK_PORT}/realms/infra-gate/.well-known/openid-configuration"
echo ""
echo "2. Push to 'dev' to trigger the deploy-development workflow."
echo ""
echo "3. After deploy, the gateway listens on http://127.0.0.1:${GATEWAY_PORT}"
