#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SONAR_URL="${SONAR_HOST_URL:-http://localhost:9000}"
PROJECT_KEY="${SONAR_PROJECT_KEY:-mirusser_Kubernetes-MCP-Guard}"
PROJECT_NAME="${SONAR_PROJECT_NAME:-Kubernetes MCP Guard}"
STATE_DIR="${REPO_ROOT}/.sonarqube-local"
ENV_FILE="${SONAR_LOCAL_ENV_FILE:-${STATE_DIR}/local.env}"
TOKEN_NAME_PREFIX="infra-gate-local-analysis"

command -v curl >/dev/null 2>&1 || {
  echo "curl is required." >&2
  exit 1
}

command -v jq >/dev/null 2>&1 || {
  echo "jq is required." >&2
  exit 1
}

command -v openssl >/dev/null 2>&1 || {
  echo "openssl is required." >&2
  exit 1
}

if [[ -f "${ENV_FILE}" ]]; then
  # shellcheck disable=SC1090
  source "${ENV_FILE}"
fi

SONAR_ADMIN_PASSWORD="${SONAR_ADMIN_PASSWORD:-}"
SONAR_TOKEN="${SONAR_TOKEN:-}"

wait_for_sonarqube() {
  local status=""
  local attempt

  for attempt in {1..60}; do
    status="$(curl -sS "${SONAR_URL}/api/system/status" 2>/dev/null | jq -r '.status // empty' 2>/dev/null || true)"
    if [[ "${status}" == "UP" ]]; then
      echo "SonarQube is UP at ${SONAR_URL}."
      return 0
    fi

    if [[ -n "${status}" ]]; then
      echo "SonarQube status is ${status}; waiting..."
    fi

    sleep 5
  done

  echo "Timed out waiting for SonarQube at ${SONAR_URL}." >&2
  return 1
}

is_valid_admin_password() {
  local password="$1"
  curl -sf -u "admin:${password}" "${SONAR_URL}/api/authentication/validate" \
    | jq -e '.valid == true' >/dev/null
}

change_default_password_if_needed() {
  if [[ -n "${SONAR_ADMIN_PASSWORD}" ]] && is_valid_admin_password "${SONAR_ADMIN_PASSWORD}"; then
    return 0
  fi

  if is_valid_admin_password "admin"; then
    SONAR_ADMIN_PASSWORD="LocalSonar-$(openssl rand -hex 18)!A1"
    curl -sf -u "admin:admin" \
      --data-urlencode "login=admin" \
      --data-urlencode "previousPassword=admin" \
      --data-urlencode "password=${SONAR_ADMIN_PASSWORD}" \
      "${SONAR_URL}/api/users/change_password" >/dev/null
    echo "Changed default admin password."
    return 0
  fi

  if [[ -n "${SONAR_ADMIN_PASSWORD}" ]]; then
    echo "Stored SONAR_ADMIN_PASSWORD did not validate." >&2
  else
    echo "Default admin password is already changed and ${ENV_FILE} has no SONAR_ADMIN_PASSWORD." >&2
  fi

  echo "Set SONAR_ADMIN_PASSWORD or restore ${ENV_FILE}, then retry." >&2
  return 1
}

create_project_if_needed() {
  local count
  count="$(curl -sf -u "admin:${SONAR_ADMIN_PASSWORD}" \
    "${SONAR_URL}/api/projects/search?projects=${PROJECT_KEY}" \
    | jq -r '.paging.total // 0')"

  if [[ "${count}" != "0" ]]; then
    echo "Project ${PROJECT_KEY} already exists."
    return 0
  fi

  curl -sf -u "admin:${SONAR_ADMIN_PASSWORD}" \
    --data-urlencode "project=${PROJECT_KEY}" \
    --data-urlencode "name=${PROJECT_NAME}" \
    "${SONAR_URL}/api/projects/create" >/dev/null
  echo "Created project ${PROJECT_KEY}."
}

is_valid_token() {
  local token="$1"
  [[ -n "${token}" ]] || return 1

  curl -sf -u "${token}:" "${SONAR_URL}/api/authentication/validate" \
    | jq -e '.valid == true' >/dev/null
}

generate_token_if_needed() {
  if is_valid_token "${SONAR_TOKEN}"; then
    echo "Stored SONAR_TOKEN is valid."
    return 0
  fi

  local token_name
  token_name="${TOKEN_NAME_PREFIX}-$(date -u +%Y%m%d%H%M%S)"
  SONAR_TOKEN="$(curl -sf -u "admin:${SONAR_ADMIN_PASSWORD}" \
    --data-urlencode "name=${token_name}" \
    "${SONAR_URL}/api/user_tokens/generate" \
    | jq -r '.token')"

  if [[ -z "${SONAR_TOKEN}" || "${SONAR_TOKEN}" == "null" ]]; then
    echo "Failed to generate SonarQube token." >&2
    return 1
  fi

  echo "Generated analysis/API token ${token_name}."
}

write_env_file() {
  mkdir -p "${STATE_DIR}"
  {
    printf 'SONAR_HOST_URL=%q\n' "${SONAR_URL}"
    printf 'SONAR_PROJECT_KEY=%q\n' "${PROJECT_KEY}"
    printf 'SONAR_PROJECT_NAME=%q\n' "${PROJECT_NAME}"
    printf 'SONAR_ADMIN_PASSWORD=%q\n' "${SONAR_ADMIN_PASSWORD}"
    printf 'SONAR_TOKEN=%q\n' "${SONAR_TOKEN}"
  } > "${ENV_FILE}"
  chmod 600 "${ENV_FILE}"
  echo "Wrote ${ENV_FILE}."
}

wait_for_sonarqube
change_default_password_if_needed
create_project_if_needed
generate_token_if_needed
write_env_file

echo "Local SonarQube is ready for analysis."
