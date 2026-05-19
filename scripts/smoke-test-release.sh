#!/usr/bin/env bash
# smoke-test-release.sh — released-image smoke test.
#
# Exercises deploy/local-oauth/compose.release.yaml using the published gateway image:
#   1. Generates deploy/generated/smoke-release.env and appsettings JSON from the run profile.
#   2. Boots Keycloak and the gateway with docker compose (--pull always).
#   3. Waits for Keycloak OIDC discovery and the gateway HTTP surface.
#   4. Verifies host-side volume directories exist.
#   5. Scans gateway logs for filesystem permission errors.
#   6. Verifies the unauthenticated MCP challenge includes resource_metadata.
#   7. Acquires a real Keycloak token through the local smoke client.
#   8. Confirms a bearer-authenticated /mcp request is not rejected as 401/403.
#
# Usage:
#   TAG=vX.Y.Z ./scripts/smoke-test-release.sh
#   TAG=latest  ./scripts/smoke-test-release.sh
#
# Requirements: docker compose v2, curl, jq, .NET 10 SDK (for profile generation).
# The kubeconfig at KUBECONFIG_PATH must exist before running this script;
# run ./scripts/create-demo-kubeconfig.sh --compose first.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TAG="${TAG:-latest}"
KUBECONFIG_PATH="${KUBECONFIG_PATH:-${REPO_ROOT}/.kube/mcp-nginx-demo.compose.config}"
COMPOSE_FILE="${REPO_ROOT}/deploy/local-oauth/compose.release.yaml"
ENV_FILE="${REPO_ROOT}/deploy/generated/smoke-release.env"
APPSETTINGS_FILE="${REPO_ROOT}/deploy/generated/smoke-release.appsettings.json"

GATEWAY_URL="http://127.0.0.1:3001"
KEYCLOAK_URL="http://127.0.0.1:3010"
REALM_URL="${KEYCLOAK_URL}/realms/infra-gate"
TOKEN_ENDPOINT="${REALM_URL}/protocol/openid-connect/token"
SMOKE_CLIENT_ID="mcp-smoke-client"
SMOKE_USERNAME="demo"
SMOKE_PASSWORD="demo"
SMOKE_SCOPE="mcp:tools"
POLL_INTERVAL=2
POLL_TIMEOUT=90
MCP_INITIALIZE_BODY='{"jsonrpc":"2.0","method":"initialize","id":1,"params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"keycloak-smoke-test","version":"0"}}}'

COMPOSE_VOLUME_DIRS=(
  "${REPO_ROOT}/.mcp-approvals"
  "${REPO_ROOT}/.mcp-guardrails"
  "${REPO_ROOT}/.mcp-dataprotection-keys"
)

docker compose version >/dev/null 2>&1 || {
  echo "ERROR: docker compose v2 is required." >&2; exit 1
}
command -v curl >/dev/null 2>&1 || {
  echo "ERROR: curl is required." >&2; exit 1
}
command -v jq >/dev/null 2>&1 || {
  echo "ERROR: jq is required." >&2; exit 1
}
[[ -f "${KUBECONFIG_PATH}" ]] || {
  echo "ERROR: kubeconfig not found at ${KUBECONFIG_PATH}." >&2
  echo "Run ./scripts/create-demo-kubeconfig.sh --compose first." >&2
  exit 1
}
[[ -f "${COMPOSE_FILE}" ]] || {
  echo "ERROR: compose file not found at ${COMPOSE_FILE}." >&2; exit 1
}

tmp_dir="$(mktemp -d)"
teardown() {
  local exit_code=$?
  if [[ ${exit_code} -ne 0 ]]; then
    echo ""
    echo "FAIL: release smoke test failed for tag '${TAG}'. Last logs:" >&2
    TAG="${TAG}" INFRA_GATE_GATEWAY_ENV_FILE="${ENV_FILE}" docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" logs --tail=80 2>/dev/null || true
  fi
  TAG="${TAG}" INFRA_GATE_GATEWAY_ENV_FILE="${ENV_FILE}" docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" down -v --remove-orphans 2>/dev/null || true
  rm -rf "${tmp_dir}"
}
trap teardown EXIT

echo "==> Generating run profile files (smoke-release) ..."
mkdir -p "${REPO_ROOT}/deploy/generated"
dotnet run --project "${REPO_ROOT}/src/InfraGate.RunProfiles" -- generate smoke-release \
  --set "host.kubeconfigHostPath=${KUBECONFIG_PATH}" \
  --set "host.approvalHostPath=${REPO_ROOT}/.mcp-approvals" \
  --set "host.guardAuditHostPath=${REPO_ROOT}/.mcp-guardrails" \
  --set "host.dataProtectionHostPath=${REPO_ROOT}/.mcp-dataprotection-keys" \
  --set "host.configHostPath=${APPSETTINGS_FILE}" \
  --output "${ENV_FILE}"

dotnet run --project "${REPO_ROOT}/src/InfraGate.RunProfiles" -- generate smoke-release \
  --format appsettings \
  --output "${APPSETTINGS_FILE}"

echo "==> Pulling and starting local OAuth services (tag=${TAG}) ..."
TAG="${TAG}" INFRA_GATE_GATEWAY_ENV_FILE="${ENV_FILE}" docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" up -d --pull always

echo "==> Waiting for Keycloak OIDC discovery ..."
elapsed=0
until curl -fsS "${REALM_URL}/.well-known/openid-configuration" >/dev/null 2>&1; do
  [[ ${elapsed} -ge ${POLL_TIMEOUT} ]] && {
    echo "ERROR: Keycloak did not become ready within ${POLL_TIMEOUT}s." >&2; exit 1
  }
  sleep "${POLL_INTERVAL}"
  elapsed=$(( elapsed + POLL_INTERVAL ))
done
echo "    Keycloak ready."

echo "==> Waiting for gateway HTTP server ..."
elapsed=0
until curl -sS -o /dev/null -w "%{http_code}" \
      -X POST "${GATEWAY_URL}/mcp" \
      -H "Content-Type: application/json" \
      -d "${MCP_INITIALIZE_BODY}" \
      2>/dev/null | grep -qE '^[0-9]{3}$'; do
  [[ ${elapsed} -ge ${POLL_TIMEOUT} ]] && {
    echo "ERROR: Gateway did not become ready within ${POLL_TIMEOUT}s." >&2; exit 1
  }
  sleep "${POLL_INTERVAL}"
  elapsed=$(( elapsed + POLL_INTERVAL ))
done
echo "    Gateway HTTP server ready."

echo "==> Verifying host volume directories exist ..."
for dir in "${COMPOSE_VOLUME_DIRS[@]}"; do
  if [[ ! -d "${dir}" ]]; then
    echo "ERROR: Host volume directory ${dir} does not exist." >&2
    echo "Run ./scripts/create-demo-kubeconfig.sh --compose first." >&2
    exit 1
  fi
done
echo "    All host volume directories present."

echo "==> Verifying no filesystem permission errors in gateway logs ..."
if TAG="${TAG}" INFRA_GATE_GATEWAY_ENV_FILE="${ENV_FILE}" docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" logs mcp-gateway 2>/dev/null | \
   grep -qE 'KeyRingProvider.*error|UnauthorizedAccessException|Permission denied'; then
  echo "ERROR: Gateway logs contain filesystem permission errors." >&2
  exit 1
fi
echo "    Gateway logs clean (no permission errors)."

echo "==> Checking unauthenticated 401 challenge shape ..."
http_status=$(curl -s -o "${tmp_dir}/unauth-body.json" -w "%{http_code}" \
  -D "${tmp_dir}/unauth-headers.txt" \
  -X POST "${GATEWAY_URL}/mcp" \
  -H "Content-Type: application/json" \
  -d "${MCP_INITIALIZE_BODY}")

www_auth=$(grep -i '^www-authenticate:' "${tmp_dir}/unauth-headers.txt" || true)

if [[ "${http_status}" != "401" ]]; then
  echo "ERROR: Expected 401 from unauthenticated request, got ${http_status}." >&2
  echo "Body: $(cat "${tmp_dir}/unauth-body.json" 2>/dev/null)" >&2
  exit 1
fi

if ! echo "${www_auth}" | grep -qi 'resource_metadata'; then
  echo "ERROR: WWW-Authenticate header missing 'resource_metadata'." >&2
  echo "Header: ${www_auth}" >&2
  exit 1
fi
echo "    401 challenge is well-formed (resource_metadata present)."

echo "==> Acquiring Keycloak smoke token ..."
token_response=$(curl -fsS \
  -X POST "${TOKEN_ENDPOINT}" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=password" \
  --data-urlencode "client_id=${SMOKE_CLIENT_ID}" \
  --data-urlencode "username=${SMOKE_USERNAME}" \
  --data-urlencode "password=${SMOKE_PASSWORD}" \
  --data-urlencode "scope=${SMOKE_SCOPE}")
access_token=$(jq -r '.access_token // empty' <<<"${token_response}")

if [[ -z "${access_token}" ]]; then
  echo "ERROR: Keycloak token response did not contain access_token." >&2
  echo "Response: ${token_response}" >&2
  exit 1
fi
echo "    Keycloak token acquired."

echo "==> Checking authenticated /mcp request is accepted by auth layer ..."
auth_status=$(curl -s -o "${tmp_dir}/auth-body.txt" -w "%{http_code}" \
  "${GATEWAY_URL}/mcp" \
  -H "Authorization: Bearer ${access_token}")

if [[ "${auth_status}" == "401" || "${auth_status}" == "403" || "${auth_status}" == "000" ]]; then
  echo "ERROR: Expected authenticated /mcp to pass auth, got ${auth_status}." >&2
  echo "Body: $(cat "${tmp_dir}/auth-body.txt" 2>/dev/null)" >&2
  exit 1
fi
echo "    Authenticated /mcp passed auth layer (HTTP ${auth_status})."

echo ""
echo "OK: release smoke test passed for tag '${TAG}'."
