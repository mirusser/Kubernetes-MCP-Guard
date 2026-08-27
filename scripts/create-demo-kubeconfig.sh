#!/usr/bin/env bash
set -euo pipefail

# Kubeconfigs contain bearer tokens. Restrict the file from its first write,
# before the explicit chmod/ACL handling below adjusts final container access.
umask 077

# Always use the admin kubeconfig for cluster setup, not any service-account
# kubeconfig that may be set in the caller's environment.
unset KUBECONFIG

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NAMESPACE="mcp-nginx-demo"
SERVICE_ACCOUNT="infra-gate-mcp"
VIEWER_SERVICE_ACCOUNT="infra-gate-mcp-view"
SA_NAME_FLAG=false
COMPOSE_MODE=false
OUT="${ROOT}/.kube/mcp-nginx-demo.config"
COMPOSE_OUT="${ROOT}/.kube/mcp-nginx-demo.compose.config"
VIEWER_OUT="${ROOT}/.kube/mcp-nginx-demo-viewer.config"
VIEWER_COMPOSE_OUT="${ROOT}/.kube/mcp-nginx-demo-viewer.compose.config"
# UID/GID the gateway container runs as (aspnet:10.0-noble-chiseled APP_UID).
# Must match scripts/setup-development-deploy.sh and the Dockerfile's USER directive.
GATEWAY_APP_UID="1654"
GATEWAY_APP_UGID="${GATEWAY_APP_UID}:${GATEWAY_APP_UID}"

usage() {
  cat <<EOF
Usage: $0 [--compose] [--sa-name NAME]

By default, creates ${OUT} and ${VIEWER_OUT}.
With --compose, also creates ${COMPOSE_OUT}, ${VIEWER_COMPOSE_OUT}, and local persistence directories for Docker Compose.
With --sa-name, creates only the named ServiceAccount's kubeconfig instead of the default pair.
EOF
}

for arg in "$@"; do
  case "${arg}" in
    --compose)
      COMPOSE_MODE=true
      ;;
    --sa-name)
      SA_NAME_FLAG=true
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      if [[ "${SA_NAME_FLAG}" == "true" ]]; then
        SERVICE_ACCOUNT="${arg}"
        SA_NAME_FLAG=false
      else
        usage >&2
        exit 1
      fi
      ;;
  esac
done

if [[ "${SA_NAME_FLAG}" == "true" ]]; then
  echo "Error: --sa-name requires a value." >&2
  usage >&2
  exit 1
fi

if [[ "${SERVICE_ACCOUNT}" != "infra-gate-mcp" ]]; then
  OUT="${ROOT}/.kube/mcp-nginx-demo-${SERVICE_ACCOUNT}.config"
  COMPOSE_OUT="${ROOT}/.kube/mcp-nginx-demo-${SERVICE_ACCOUNT}.compose.config"
fi

write_kubeconfig() {
  local out="$1"
  local server="$2"
  local service_account="$3"
  local token="$4"
  local tls_server_name="${5:-}"

  mkdir -p "$(dirname "${out}")"

  if [[ -d "${out}" ]]; then
    cat >&2 <<EOF
Expected a kubeconfig file at:
  ${out}
but found a directory instead. This usually happens when 'docker compose up'
runs before this script and Docker auto-creates a missing bind-mount source
as a root-owned directory. Remove it and re-run this script:
  sudo rm -rf "${out}"
EOF
    exit 1
  fi

  if [[ -e "${out}" && ! -w "${out}" ]]; then
    # grant_container_read() below may have chowned a prior kubeconfig to
    # GATEWAY_APP_UID via passwordless sudo (its setfacl-unavailable
    # fallback). We're about to regenerate this file's contents anyway, so
    # reclaiming it here is safe and breaks that fix-it/re-lock-it cycle.
    if command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
      sudo chown "$(id -u):$(id -g)" "${out}" 2>/dev/null || true
    fi
  fi

  if [[ -e "${out}" && ! -w "${out}" ]]; then
    local current_owner
    current_owner="$(stat -c '%U (uid %u)' "${out}" 2>/dev/null || echo 'unknown')"
    cat >&2 <<EOF
Cannot write to existing file:
  ${out}
Current owner: ${current_owner}

This is likely left over from a prior 'sudo' run of this script, or from
Docker auto-creating a missing bind-mount source before this script ran.
Reclaim or remove it and re-run:
  sudo chown $(id -u):$(id -g) "${out}"
  # or, since this file is fully regenerated every run:
  sudo rm -f "${out}"
EOF
    exit 1
  fi

  cat > "${out}" <<EOF
apiVersion: v1
kind: Config
clusters:
  - name: minikube
    cluster:
      server: ${server}
      certificate-authority-data: ${CA_DATA}
EOF

  if [[ -n "${tls_server_name}" ]]; then
    cat >> "${out}" <<EOF
      tls-server-name: ${tls_server_name}
EOF
  fi

  cat >> "${out}" <<EOF
users:
  - name: ${service_account}
    user:
      token: ${token}
contexts:
  - name: minikube-mcp
    context:
      cluster: minikube
      user: ${service_account}
      namespace: ${NAMESPACE}
current-context: minikube-mcp
EOF

  chmod 600 "${out}"
}

is_loopback_server() {
  [[ "$1" =~ ^https?://(127\.0\.0\.1|localhost|\[::1\])(:[0-9]+)?(/.*)?$ ]]
}

compose_server_url() {
  printf '%s' "$1" | sed -E 's#^(https?://)(127\.0\.0\.1|localhost|\[::1\])(:[0-9]+)?(/.*)?$#\1host.docker.internal\3\4#'
}

server_host() {
  local without_scheme="${1#*://}"
  local host_port="${without_scheme%%/*}"

  if [[ "${host_port}" =~ ^\[([^]]+)\] ]]; then
    printf '%s' "${BASH_REMATCH[1]}"
    return
  fi

  printf '%s' "${host_port%%:*}"
}

grant_container_read() {
  local target="$1"

  if command -v setfacl >/dev/null 2>&1 &&
    setfacl -m "u:${GATEWAY_APP_UID}:r" "${target}" 2>/dev/null; then
    return
  fi

  if chown "${GATEWAY_APP_UID}" "${target}" 2>/dev/null; then
    return
  fi

  if command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
    sudo chown "${GATEWAY_APP_UID}" "${target}"
    return
  fi

  cat >&2 <<EOF
Could not grant the gateway container UID ${GATEWAY_APP_UID} read access to:
  ${target}

This file contains a Kubernetes bearer token and will not be made
world-readable. Install setfacl, run this script with sudo, or run:
  sudo chown ${GATEWAY_APP_UID} "${target}"
EOF
  exit 1
}

prepare_compose_persistence_dirs() {
  local approval_dir="${ROOT}/.mcp-approvals"
  local guardrail_dir="${ROOT}/.mcp-guardrails"
  local dataprotection_dir="${ROOT}/.mcp-dataprotection-keys"
  local logs_dir="${ROOT}/.mcp-logs"

  # A prior run's setfacl-unavailable fallback (below) chowns these to
  # GATEWAY_APP_UGID and strips go permissions, locking the invoking user
  # out entirely. Reclaim access first via the same passwordless-sudo trust
  # boundary that locked it, so this doesn't require manual recovery.
  local persistence_dir
  for persistence_dir in "${approval_dir}" "${guardrail_dir}" "${dataprotection_dir}" "${logs_dir}"; do
    if [[ -e "${persistence_dir}" && ! -r "${persistence_dir}" ]] &&
      command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
      sudo chown -R "$(id -u):$(id -g)" "${persistence_dir}" 2>/dev/null || true
      sudo chmod -R u+rwX "${persistence_dir}" 2>/dev/null || true
    fi
  done

  # Pre-create approval subdirectories so the container finds them host-owned
  # rather than creating them as UID 1654, which would break chmod on re-runs.
  mkdir -p \
    "${approval_dir}/pending" "${approval_dir}/applied" \
    "${approval_dir}/grants" "${approval_dir}/challenges" \
    "${guardrail_dir}" "${dataprotection_dir}" "${logs_dir}"

  # Only chmod content we own; container-created files (UID 1654) are skipped.
  find "${approval_dir}" "${guardrail_dir}" "${dataprotection_dir}" "${logs_dir}" \
    -user "$(id -u)" -exec chmod u+rwX,go-rwx {} +

  # Apply ACLs to directories only; files created by the container (UID 1654)
  # are already accessible to that UID and setfacl would fail on them.
  if command -v setfacl >/dev/null 2>&1 &&
    find "${approval_dir}" "${guardrail_dir}" "${dataprotection_dir}" "${logs_dir}" \
      -type d -exec setfacl -m "u:${GATEWAY_APP_UID}:rwx,d:u:${GATEWAY_APP_UID}:rwx" {} +; then
    return
  fi

  if chown -R "${GATEWAY_APP_UGID}" "${approval_dir}" "${guardrail_dir}" "${dataprotection_dir}" "${logs_dir}" 2>/dev/null; then
    chmod -R u+rwX,go-rwx "${approval_dir}" "${guardrail_dir}" "${dataprotection_dir}" "${logs_dir}"
    return
  fi

  if command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
    sudo chown -R "${GATEWAY_APP_UGID}" "${approval_dir}" "${guardrail_dir}" "${dataprotection_dir}" "${logs_dir}"
    sudo chmod -R u+rwX,go-rwx "${approval_dir}" "${guardrail_dir}" "${dataprotection_dir}" "${logs_dir}"
    return
  fi

  cat >&2 <<EOF
Could not grant the gateway container UID ${GATEWAY_APP_UID} write access to:
  ${approval_dir}
  ${guardrail_dir}
  ${dataprotection_dir}
  ${logs_dir}

Install setfacl, run this script with sudo, or run:
  sudo chown -R ${GATEWAY_APP_UGID} "${approval_dir}" "${guardrail_dir}" "${dataprotection_dir}" "${logs_dir}"
  sudo chmod -R u+rwX,go-rwx "${approval_dir}" "${guardrail_dir}" "${dataprotection_dir}" "${logs_dir}"
EOF
  exit 1
}

ensure_cluster_ready() {
  local context
  local server

  context="$(kubectl config current-context 2>/dev/null || true)"
  server="$(kubectl config view --minify -o jsonpath='{.clusters[0].cluster.server}' 2>/dev/null || true)"

  if [[ -z "${context}" || -z "${server}" ]]; then
    echo "No current Kubernetes context is configured. Start minikube first: minikube start" >&2
    exit 1
  fi

  if [[ "${context}" != "minikube" ]]; then
    echo "Refusing to apply demo RBAC to non-demo Kubernetes context '${context}'." >&2
    echo "Select the default minikube context first: kubectl config use-context minikube" >&2
    exit 1
  fi

  if ! kubectl --request-timeout=10s get --raw=/readyz >/dev/null 2>&1; then
    echo "Current Kubernetes context '${context}' is not reachable at ${server}." >&2
    echo "Start or repair minikube first: minikube start" >&2
    exit 1
  fi
}

ensure_cluster_ready
kubectl apply --validate=false -f "${ROOT}/deploy/minikube/rbac.yaml"

SERVER="$(kubectl config view --minify -o jsonpath='{.clusters[0].cluster.server}')"
CA_DATA="$(kubectl config view --raw --minify -o jsonpath='{.clusters[0].cluster.certificate-authority-data}')"
if [[ -z "${CA_DATA}" ]]; then
  CA_FILE="$(kubectl config view --raw --minify -o jsonpath='{.clusters[0].cluster.certificate-authority}')"
  CA_DATA="$(base64 -w 0 "${CA_FILE}")"
fi
TOKEN="$(kubectl -n "${NAMESPACE}" create token "${SERVICE_ACCOUNT}" --duration=24h)"

write_kubeconfig "${OUT}" "${SERVER}" "${SERVICE_ACCOUNT}" "${TOKEN}"

if [[ "${SERVICE_ACCOUNT}" == "infra-gate-mcp" ]]; then
  VIEWER_TOKEN="$(kubectl -n "${NAMESPACE}" create token "${VIEWER_SERVICE_ACCOUNT}" --duration=24h)"
  write_kubeconfig "${VIEWER_OUT}" "${SERVER}" "${VIEWER_SERVICE_ACCOUNT}" "${VIEWER_TOKEN}"
fi

if [[ "${COMPOSE_MODE}" == "true" ]]; then
  COMPOSE_SERVER="${SERVER}"
  TLS_SERVER_NAME=""

  if is_loopback_server "${SERVER}"; then
    COMPOSE_SERVER="$(compose_server_url "${SERVER}")"
    TLS_SERVER_NAME="$(server_host "${SERVER}")"
  fi

  write_kubeconfig \
    "${COMPOSE_OUT}" \
    "${COMPOSE_SERVER}" \
    "${SERVICE_ACCOUNT}" \
    "${TOKEN}" \
    "${TLS_SERVER_NAME}"
  grant_container_read "${COMPOSE_OUT}"
  if [[ "${SERVICE_ACCOUNT}" == "infra-gate-mcp" ]]; then
    write_kubeconfig \
      "${VIEWER_COMPOSE_OUT}" \
      "${COMPOSE_SERVER}" \
      "${VIEWER_SERVICE_ACCOUNT}" \
      "${VIEWER_TOKEN}" \
      "${TLS_SERVER_NAME}"
    grant_container_read "${VIEWER_COMPOSE_OUT}"
  fi
  prepare_compose_persistence_dirs
fi

echo "${OUT}"
if [[ "${SERVICE_ACCOUNT}" == "infra-gate-mcp" ]]; then
  echo "${VIEWER_OUT}"
fi
if [[ "${COMPOSE_MODE}" == "true" ]]; then
  echo "${COMPOSE_OUT}"
  if [[ "${SERVICE_ACCOUNT}" == "infra-gate-mcp" ]]; then
    echo "${VIEWER_COMPOSE_OUT}"
  fi
fi
