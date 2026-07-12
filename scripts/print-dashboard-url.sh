#!/usr/bin/env bash
# print-dashboard-url.sh — Print the Aspire Dashboard login URL for the local-compose stack.
#
# Reads ASPIRE_DASHBOARD_TOKEN from the generated run-profile env file and
# prints a ready-to-open login URL, so you don't have to hunt through
# deploy/generated/local-compose.env by hand.
#
# Usage:
#   ./scripts/print-dashboard-url.sh [env-file]
#
# Examples:
#   ./scripts/print-dashboard-url.sh
#   ./scripts/print-dashboard-url.sh deploy/generated/local-compose.env

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# Matches deploy/local-oauth/compose.yaml's aspire-dashboard defaults.
DEFAULT_BIND_ADDRESS="127.0.0.1"
DEFAULT_UI_PORT="18888"
DEFAULT_TOKEN="dev-token-change-me"

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  echo "Usage: $0 [env-file]" >&2
  echo "Defaults to ${REPO_ROOT}/deploy/generated/local-compose.env" >&2
  exit 0
fi

ENV_FILE="${1:-${REPO_ROOT}/deploy/generated/local-compose.env}"

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "ERROR: env file not found at ${ENV_FILE}. Run ./scripts/generate-env.sh local-compose first." >&2
  exit 1
fi

token="$(grep -m1 "^ASPIRE_DASHBOARD_TOKEN=" "${ENV_FILE}" | cut -d= -f2- || true)"
token="${token:-${DEFAULT_TOKEN}}"

bind_address="${ASPIRE_DASHBOARD_BIND_ADDRESS:-${DEFAULT_BIND_ADDRESS}}"
ui_port="${ASPIRE_DASHBOARD_UI_PORT:-${DEFAULT_UI_PORT}}"

# Local dev convenience only — the Aspire Dashboard container serves plain HTTP on
# loopback/compose-network addresses and is never exposed over the network, so there
# is no clear-text-transport risk to mitigate here.
echo "http://${bind_address}:${ui_port}/login?t=${token}" # NOSONAR(shell:S5332)
