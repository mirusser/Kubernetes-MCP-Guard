#!/usr/bin/env bash
# generate-env.sh — Generate run profile files for local use.
#
# Wraps InfraGate.RunProfiles generate and supplies standard host-path overrides
# so the generated env works with Docker Compose volume bind-mounts from any CWD.
# Host-path defaults use REPO_ROOT; pass additional --set flags to override any of them.
#
# Usage:
#   ./scripts/generate-env.sh <profile> [--output <env-path>] [--set section.field=value ...] [--force]
#
# Examples:
#   ./scripts/generate-env.sh local-compose
#   ./scripts/generate-env.sh smoke-release --output /tmp/smoke-release.env
#   ./scripts/generate-env.sh smoke-release --set host.kubeconfigHostPath=/custom/.kube/my.config

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

DEV_SECRETS_FILE="${REPO_ROOT}/dev-secrets.env"
if [[ -f "${DEV_SECRETS_FILE}" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "${DEV_SECRETS_FILE}"
  set +a
fi

if [[ $# -lt 1 || "$1" == "--help" || "$1" == "-h" ]]; then
  echo "Usage: $0 <profile> [--output <env-path>] [--set section.field=value ...] [--force]" >&2
  echo ""                                                                                  >&2
  echo "Profiles:"                                                                         >&2
  dotnet run --project "${REPO_ROOT}/src/InfraGate.RunProfiles" -- list 2>/dev/null | \
    awk '{print "  " $0}'                                                                  >&2
  exit 1
fi

PROFILE="$1"
shift

OUTPUT="${REPO_ROOT}/deploy/generated/${PROFILE}.env"
EXTRA_ARGS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output)
      OUTPUT="$2"
      shift 2
      ;;
    *)
      EXTRA_ARGS+=("$1")
      shift
      ;;
  esac
done

mkdir -p "$(dirname "$OUTPUT")"

downstream_gateway_secret="${InfraGate__DownstreamAuth__GatewayClientSecret:-${INFRA_GATE_DOWNSTREAM_AUTH_GATEWAY_CLIENT_SECRET:-}}"
observer_client_secret="${InfraGate__Observer__ClientCredentials__ClientSecret:-${INFRA_GATE_OBSERVER_CLIENT_SECRET:-}}"
openrouter_api_key="${InfraGate__OpenRouter__ApiKey:-}"
planner_client_secret="${InfraGate__Planner__ClientCredentials__ClientSecret:-${INFRA_GATE_PLANNER_CLIENT_SECRET:-}}"
executor_client_secret="${InfraGate__Executor__ClientCredentials__ClientSecret:-${INFRA_GATE_EXECUTOR_CLIENT_SECRET:-}}"

[[ -n "$downstream_gateway_secret" ]] &&
  EXTRA_ARGS+=(--set "downstreamAuth.gatewayClientSecret=${downstream_gateway_secret}")
[[ -n "$observer_client_secret" ]] &&
  EXTRA_ARGS+=(--set "observer.clientSecret=${observer_client_secret}")
[[ -n "$openrouter_api_key" ]] &&
  EXTRA_ARGS+=(--set "openRouter.apiKey=${openrouter_api_key}")
[[ -n "$planner_client_secret" ]] &&
  EXTRA_ARGS+=(--set "planner.clientSecret=${planner_client_secret}")
[[ -n "$executor_client_secret" ]] &&
  EXTRA_ARGS+=(--set "executor.clientSecret=${executor_client_secret}")

dotnet run --project "${REPO_ROOT}/src/InfraGate.RunProfiles" -- generate "$PROFILE" \
  --set "host.kubeconfigHostPath=${REPO_ROOT}/.kube/mcp-nginx-demo.compose.config" \
  --set "host.approvalHostPath=${REPO_ROOT}/.mcp-approvals" \
  --set "host.guardAuditHostPath=${REPO_ROOT}/.mcp-guardrails" \
  --set "host.dataProtectionHostPath=${REPO_ROOT}/.mcp-dataprotection-keys" \
  --set "observer.observerHostPath=${REPO_ROOT}/.mcp-observer/findings" \
  --set "planner.plannerHostPath=${REPO_ROOT}/.mcp-remediation/proposals" \
  ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"} \
  --output "$OUTPUT"

# Pre-create agent data directories owned by the current user so that containers
# running as a non-root APP_UID can write to them. If Docker creates these
# directories first (on volume mount) they land as root:root 755 and become
# unwritable by the container user.
OBSERVER_FINDINGS="${REPO_ROOT}/.mcp-observer/findings"
PLANNER_PROPOSALS="${REPO_ROOT}/.mcp-remediation/proposals"
mkdir -p "$OBSERVER_FINDINGS" "$PLANNER_PROPOSALS"
chmod 777 "$OBSERVER_FINDINGS" "$PLANNER_PROPOSALS" 2>/dev/null || \
  echo "Warning: could not chmod agent data directories (already owned by root?)." \
       "Run: sudo chmod 777 ${OBSERVER_FINDINGS} ${PLANNER_PROPOSALS}" >&2
