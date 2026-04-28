#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <plan-id>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APPROVAL_ROOT="${K8S_MCP_APPROVAL_ROOT:-${ROOT}/.mcp-approvals}"
PLAN_ID="$1"
PENDING="${APPROVAL_ROOT}/pending/${PLAN_ID}.json"
APPROVED_DIR="${APPROVAL_ROOT}/approved"
APPROVED="${APPROVED_DIR}/${PLAN_ID}.sha256"

if [[ ! "${PLAN_ID}" =~ ^[0-9a-z-]+$ ]]; then
  echo "Invalid plan id: ${PLAN_ID}" >&2
  exit 2
fi

if [[ ! -f "${PENDING}" ]]; then
  echo "Pending plan not found: ${PENDING}" >&2
  exit 1
fi

mkdir -p "${APPROVED_DIR}"
sha256sum "${PENDING}" | awk '{print $1}' > "${APPROVED}"

echo "Approved ${PLAN_ID}"
echo "Approval file: ${APPROVED}"
