#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

DEV_SECRETS_FILE="${REPO_ROOT}/dev-secrets.env"
if [[ -f "${DEV_SECRETS_FILE}" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "${DEV_SECRETS_FILE}"
  set +a
fi

SOURCE_COMPOSE_FILE="${REPO_ROOT}/deploy/local-oauth/compose.yaml"
SOURCE_ENV_FILE="${REPO_ROOT}/deploy/generated/local-compose.env"
RELEASE_COMPOSE_FILE="${REPO_ROOT}/deploy/local-oauth/compose.release.yaml"
RELEASE_ENV_FILE="${REPO_ROOT}/deploy/local-oauth/release.env.example"
DEFAULT_LOG_TAIL="120"
OPENROUTER_API_KEY_ENV="InfraGate__OpenRouter__ApiKey"

usage() {
  cat <<EOF
Usage:
  $0 published [--tag TAG]
  $0 source
  $0 down
  $0 logs

Commands:
  published   Start the local OAuth stack from published images. Defaults to TAG=latest.
  source      Generate the local-compose env file and start the source-build stack.
              Requires ${OPENROUTER_API_KEY_ENV}, either exported or set in
              dev-secrets.env (copy dev-secrets.env.example to get started).
  down        Stop the quickstart Compose stack without deleting volumes.
  logs        Show recent logs for the local quickstart stack.
EOF
}

die() {
  echo "ERROR: $*" >&2
  exit 1
}

ensure_file() {
  local path="$1"
  local description="$2"

  [[ -f "${path}" ]] || die "${description} not found at ${path}."
}

require_command() {
  local command_name="$1"
  local message="$2"

  command -v "${command_name}" >/dev/null 2>&1 || die "${message}"
}

require_docker_compose() {
  require_command docker "Docker is required."
  docker compose version >/dev/null 2>&1 || die "Docker Compose v2 is required."
}

require_kubectl() {
  require_command kubectl "kubectl is required to prepare the demo kubeconfig."
}

require_openrouter_api_key() {
  [[ -n "${!OPENROUTER_API_KEY_ENV:-}" ]] ||
    die "Export ${OPENROUTER_API_KEY_ENV} before running the source quickstart."
}

prepare_compose_kubeconfig() {
  "${SCRIPT_DIR}/create-demo-kubeconfig.sh" --compose
}

run_published() {
  local tag="$1"

  require_docker_compose
  require_kubectl
  ensure_file "${RELEASE_COMPOSE_FILE}" "Release Compose file"
  ensure_file "${RELEASE_ENV_FILE}" "Release env file"

  prepare_compose_kubeconfig

  cd "${REPO_ROOT}"
  TAG="${tag}" docker compose --env-file "${RELEASE_ENV_FILE}" \
    -f "${RELEASE_COMPOSE_FILE}" up
}

run_source() {
  require_openrouter_api_key
  require_docker_compose
  require_kubectl
  require_command dotnet ".NET SDK is required for the source quickstart."
  ensure_file "${SOURCE_COMPOSE_FILE}" "Source Compose file"

  prepare_compose_kubeconfig
  "${SCRIPT_DIR}/generate-env.sh" local-compose

  cd "${REPO_ROOT}"
  docker compose --env-file "${SOURCE_ENV_FILE}" \
    -f "${SOURCE_COMPOSE_FILE}" up --build
}

compose_has_containers() {
  local env_file="$1"
  local compose_file="$2"
  local container_ids

  container_ids="$(docker compose --env-file "${env_file}" -f "${compose_file}" ps --status=running -q 2>/dev/null || true)"
  [[ -n "${container_ids}" ]]
}

run_down() {
  require_docker_compose
  ensure_file "${RELEASE_COMPOSE_FILE}" "Release Compose file"
  ensure_file "${RELEASE_ENV_FILE}" "Release env file"

  cd "${REPO_ROOT}"

  if [[ -f "${SOURCE_ENV_FILE}" ]]; then
    docker compose --env-file "${SOURCE_ENV_FILE}" \
      -f "${SOURCE_COMPOSE_FILE}" down --remove-orphans ||
      echo "Warning: Failed to stop source stack." >&2
  fi

  TAG="${TAG:-latest}" docker compose --env-file "${RELEASE_ENV_FILE}" \
    -f "${RELEASE_COMPOSE_FILE}" down --remove-orphans
}

run_logs() {
  require_docker_compose
  ensure_file "${RELEASE_COMPOSE_FILE}" "Release Compose file"
  ensure_file "${RELEASE_ENV_FILE}" "Release env file"

  cd "${REPO_ROOT}"

  if [[ -f "${SOURCE_ENV_FILE}" ]] &&
    compose_has_containers "${SOURCE_ENV_FILE}" "${SOURCE_COMPOSE_FILE}"; then
    docker compose --env-file "${SOURCE_ENV_FILE}" \
      -f "${SOURCE_COMPOSE_FILE}" logs --tail="${DEFAULT_LOG_TAIL}"
    return
  fi

  TAG="${TAG:-latest}" docker compose --env-file "${RELEASE_ENV_FILE}" \
    -f "${RELEASE_COMPOSE_FILE}" logs --tail="${DEFAULT_LOG_TAIL}"
}

parse_published() {
  local tag="latest"

  while [[ $# -gt 0 ]]; do
    local opt="$1"
    case "$opt" in
      --tag)
        [[ $# -ge 2 ]] || die "--tag requires a value."
        tag="$2"
        shift 2
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        die "Unknown published option: $opt"
        ;;
    esac
  done

  run_published "${tag}"
}

main() {
  if [[ $# -eq 0 ]]; then
    usage >&2
    exit 1
  fi

  local command="$1"
  shift

  case "${command}" in
    published)
      parse_published "$@"
      ;;
    source)
      [[ $# -eq 0 ]] || die "source does not accept arguments."
      run_source
      ;;
    down)
      [[ $# -eq 0 ]] || die "down does not accept arguments."
      run_down
      ;;
    logs)
      [[ $# -eq 0 ]] || die "logs does not accept arguments."
      run_logs
      ;;
    -h|--help|help)
      usage
      ;;
    *)
      die "Unknown command: ${command}"
      ;;
  esac
}

main "$@"
