#!/usr/bin/env bash
# generate-env.sh — Generate run profile files for local use.
#
# Wraps InfraGate.RunProfiles generate and supplies standard host-path overrides
# so the generated env works with Docker Compose volume bind-mounts from any CWD.
# Host-path defaults use REPO_ROOT; pass additional --set flags to override any of them.
#
# Usage:
#   ./scripts/generate-env.sh <profile> [--output <env-path>] [--appsettings-output <path>] [--set section.field=value ...] [--force]
#
# Examples:
#   ./scripts/generate-env.sh local-compose
#   ./scripts/generate-env.sh smoke-release --output /tmp/smoke-release.env
#   ./scripts/generate-env.sh smoke-release --set host.kubeconfigHostPath=/custom/.kube/my.config

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [[ $# -lt 1 || "$1" == "--help" || "$1" == "-h" ]]; then
  echo "Usage: $0 <profile> [--output <env-path>] [--appsettings-output <path>] [--set section.field=value ...] [--force]" >&2
  echo ""                                                                                  >&2
  echo "Profiles:"                                                                         >&2
  dotnet run --project "${REPO_ROOT}/src/InfraGate.RunProfiles" -- list 2>/dev/null | \
    awk '{print "  " $0}'                                                                  >&2
  exit 1
fi

PROFILE="$1"
shift

OUTPUT="${REPO_ROOT}/deploy/generated/${PROFILE}.env"
APPSETTINGS_OUTPUT=""
EXTRA_ARGS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output)
      OUTPUT="$2"
      shift 2
      ;;
    --appsettings-output)
      APPSETTINGS_OUTPUT="$2"
      shift 2
      ;;
    *)
      EXTRA_ARGS+=("$1")
      shift
      ;;
  esac
done

if [[ -z "$APPSETTINGS_OUTPUT" ]]; then
  if [[ "$OUTPUT" == *.env ]]; then
    APPSETTINGS_OUTPUT="${OUTPUT%.env}.appsettings.json"
  else
    APPSETTINGS_OUTPUT="${OUTPUT}.appsettings.json"
  fi
fi

mkdir -p "$(dirname "$OUTPUT")"
mkdir -p "$(dirname "$APPSETTINGS_OUTPUT")"

APPSETTINGS_HOST_PATH="$(cd "$(dirname "$APPSETTINGS_OUTPUT")" && pwd)/$(basename "$APPSETTINGS_OUTPUT")"

dotnet run --project "${REPO_ROOT}/src/InfraGate.RunProfiles" -- generate "$PROFILE" \
  --set "host.kubeconfigHostPath=${REPO_ROOT}/.kube/mcp-nginx-demo.compose.config" \
  --set "host.approvalHostPath=${REPO_ROOT}/.mcp-approvals" \
  --set "host.guardAuditHostPath=${REPO_ROOT}/.mcp-guardrails" \
  --set "host.dataProtectionHostPath=${REPO_ROOT}/.mcp-dataprotection-keys" \
  --set "host.configHostPath=${APPSETTINGS_HOST_PATH}" \
  ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"} \
  --output "$OUTPUT"

dotnet run --project "${REPO_ROOT}/src/InfraGate.RunProfiles" -- generate "$PROFILE" \
  --format appsettings \
  ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"} \
  --output "$APPSETTINGS_OUTPUT"
