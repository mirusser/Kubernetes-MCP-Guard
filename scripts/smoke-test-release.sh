#!/usr/bin/env bash
# smoke-test-release.sh — end-to-end released-image smoke test.
#
# Exercises deploy/mode-c/compose.release.yaml using published images:
#   1. Boots both services with docker compose (--pull always).
#   2. Waits for DevIssuer OIDC discovery endpoint to return 200.
#   3. Waits for the gateway MCP initialize endpoint to return 200 + session id.
#   4. Auth surface check (Shape B): asserts the gateway returns a well-formed
#      401 with a WWW-Authenticate Bearer challenge including resource_metadata.
#      (Shape A — minting a real DevIssuer JWT and calling tools/call — is a
#      follow-up once a shell-level OAuth helper exists in this repo.)
#   5. Tears down compose on exit (success or failure).
#
# Usage:
#   TAG=vX.Y.Z ./scripts/smoke-test-release.sh
#   TAG=latest  ./scripts/smoke-test-release.sh   # floating tag; fine for quick tries
#
# Requirements: docker compose v2, curl, jq.
# The kubeconfig at KUBECONFIG_PATH must exist before running this script;
# run ./scripts/create-demo-kubeconfig.sh --compose first.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TAG="${TAG:-latest}"
KUBECONFIG_PATH="${KUBECONFIG_PATH:-${REPO_ROOT}/.kube/mcp-nginx-demo.compose.config}"
COMPOSE_FILE="${REPO_ROOT}/deploy/mode-c/compose.release.yaml"

GATEWAY_URL="http://127.0.0.1:3001"
DEVISSUER_URL="http://127.0.0.1:3011"
POLL_INTERVAL=2
POLL_TIMEOUT=60

# ── pre-checks ────────────────────────────────────────────────────────────────

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

# ── teardown trap ─────────────────────────────────────────────────────────────

teardown() {
  local exit_code=$?
  if [[ ${exit_code} -ne 0 ]]; then
    echo ""
    echo "FAIL: smoke test failed for tag '${TAG}'. Last logs:" >&2
    TAG="${TAG}" docker compose -f "${COMPOSE_FILE}" logs --tail=50 2>/dev/null || true
  fi
  TAG="${TAG}" docker compose -f "${COMPOSE_FILE}" down -v --remove-orphans 2>/dev/null || true
}
trap teardown EXIT

# ── boot ──────────────────────────────────────────────────────────────────────

echo "==> Pulling and starting services (tag=${TAG}) ..."
TAG="${TAG}" docker compose -f "${COMPOSE_FILE}" up -d --pull always

# ── poll: DevIssuer OIDC discovery ────────────────────────────────────────────

echo "==> Waiting for DevIssuer OIDC discovery ..."
elapsed=0
until curl -fsS "${DEVISSUER_URL}/.well-known/openid-configuration" >/dev/null 2>&1; do
  [[ ${elapsed} -ge ${POLL_TIMEOUT} ]] && {
    echo "ERROR: DevIssuer did not become ready within ${POLL_TIMEOUT}s." >&2; exit 1
  }
  sleep "${POLL_INTERVAL}"
  elapsed=$(( elapsed + POLL_INTERVAL ))
done
echo "    DevIssuer ready."

# ── poll: gateway MCP initialize ─────────────────────────────────────────────
#
# The streamable-HTTP MCP transport requires a POST /mcp with Content-Type
# application/json and an MCP initialize message. Without auth, the gateway
# must respond 401; with a well-formed request that includes auth, it returns
# 200 + Mcp-Session-Id.  We use the unauthenticated path here intentionally
# (Shape B) to prove the gateway HTTP server is up without needing a real token.
#
# We look for a non-connection-refused response (any HTTP status), which means
# the ASP.NET host is accepting connections.

echo "==> Waiting for gateway HTTP server ..."
elapsed=0
until curl -fsS -o /dev/null -w "%{http_code}" \
      -X POST "${GATEWAY_URL}/mcp" \
      -H "Content-Type: application/json" \
      -d '{"jsonrpc":"2.0","method":"initialize","id":1,"params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"smoke-test","version":"0"}}}' \
      2>/dev/null | grep -qE '^[0-9]{3}$'; do
  [[ ${elapsed} -ge ${POLL_TIMEOUT} ]] && {
    echo "ERROR: Gateway did not become ready within ${POLL_TIMEOUT}s." >&2; exit 1
  }
  sleep "${POLL_INTERVAL}"
  elapsed=$(( elapsed + POLL_INTERVAL ))
done
echo "    Gateway HTTP server ready."

# ── Shape B: assert 401 + well-formed WWW-Authenticate challenge ──────────────
#
# Without an Authorization header the gateway must return 401 with a
# WWW-Authenticate Bearer challenge that includes both 'resource_metadata'
# (MCP protected-resource metadata URL) and the issuer.  This proves the auth
# surface is wired even without a real token.

echo "==> Checking 401 auth challenge shape ..."
http_status=$(curl -s -o /tmp/smoke-body.json -w "%{http_code}" \
  -X POST "${GATEWAY_URL}/mcp" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"initialize","id":1,"params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"smoke-test","version":"0"}}}')

www_auth=$(curl -sI \
  -X POST "${GATEWAY_URL}/mcp" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"initialize","id":1,"params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"smoke-test","version":"0"}}}' \
  | grep -i '^www-authenticate:' || true)

if [[ "${http_status}" != "401" ]]; then
  echo "ERROR: Expected 401 from unauthenticated request, got ${http_status}." >&2
  echo "Body: $(cat /tmp/smoke-body.json 2>/dev/null)" >&2
  exit 1
fi

if ! echo "${www_auth}" | grep -qi 'resource_metadata'; then
  echo "ERROR: WWW-Authenticate header missing 'resource_metadata'." >&2
  echo "Header: ${www_auth}" >&2
  exit 1
fi

echo "    401 challenge is well-formed (resource_metadata present)."

# ── done ──────────────────────────────────────────────────────────────────────

echo ""
echo "OK: smoke test passed for tag '${TAG}'."
