#!/usr/bin/env bash
set -euo pipefail

# Always use the admin kubeconfig for cluster setup, not any service-account
# kubeconfig that may be set in the caller's environment.
unset KUBECONFIG

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NAMESPACE="mcp-nginx-demo"
SERVICE_ACCOUNT="infra-gate-mcp"
OUT="${ROOT}/.kube/mcp-nginx-demo.config"
COMPOSE_OUT="${ROOT}/.kube/mcp-nginx-demo.compose.config"
COMPOSE_MODE=false
GATEWAY_APP_UID="1654"
GATEWAY_APP_UGID="${GATEWAY_APP_UID}:${GATEWAY_APP_UID}"

usage() {
  cat <<EOF
Usage: $0 [--compose]

Creates ${OUT}.
With --compose, also creates ${COMPOSE_OUT} and local persistence directories for Docker Compose.
EOF
}

for arg in "$@"; do
  case "${arg}" in
    --compose)
      COMPOSE_MODE=true
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      usage >&2
      exit 1
      ;;
  esac
done

write_kubeconfig() {
  local out="$1"
  local server="$2"
  local tls_server_name="${3:-}"

  mkdir -p "$(dirname "${out}")"

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
  - name: ${SERVICE_ACCOUNT}
    user:
      token: ${TOKEN}
contexts:
  - name: minikube-mcp
    context:
      cluster: minikube
      user: ${SERVICE_ACCOUNT}
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

prepare_compose_persistence_dirs() {
  local approval_dir="${ROOT}/.mcp-approvals"
  local guardrail_dir="${ROOT}/.mcp-guardrails"

  mkdir -p "${approval_dir}" "${guardrail_dir}"
  chmod -R u+rwX,go-rwx "${approval_dir}" "${guardrail_dir}"

  if command -v setfacl >/dev/null 2>&1 &&
    setfacl -R -m "u:${GATEWAY_APP_UID}:rwx" "${approval_dir}" "${guardrail_dir}" &&
    find "${approval_dir}" "${guardrail_dir}" -type d -exec setfacl -m "d:u:${GATEWAY_APP_UID}:rwx" {} +; then
    return
  fi

  if chown -R "${GATEWAY_APP_UGID}" "${approval_dir}" "${guardrail_dir}" 2>/dev/null; then
    chmod -R u+rwX,go-rwx "${approval_dir}" "${guardrail_dir}"
    return
  fi

  if command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
    sudo chown -R "${GATEWAY_APP_UGID}" "${approval_dir}" "${guardrail_dir}"
    sudo chmod -R u+rwX,go-rwx "${approval_dir}" "${guardrail_dir}"
    return
  fi

  cat >&2 <<EOF
Could not grant the gateway container UID ${GATEWAY_APP_UID} write access to:
  ${approval_dir}
  ${guardrail_dir}

Install setfacl, run this script with sudo, or run:
  sudo chown -R ${GATEWAY_APP_UGID} "${approval_dir}" "${guardrail_dir}"
  sudo chmod -R u+rwX,go-rwx "${approval_dir}" "${guardrail_dir}"
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

write_kubeconfig "${OUT}" "${SERVER}"

if [[ "${COMPOSE_MODE}" == "true" ]]; then
  COMPOSE_SERVER="${SERVER}"
  TLS_SERVER_NAME=""

  if is_loopback_server "${SERVER}"; then
    COMPOSE_SERVER="$(compose_server_url "${SERVER}")"
    TLS_SERVER_NAME="$(server_host "${SERVER}")"
  fi

  write_kubeconfig "${COMPOSE_OUT}" "${COMPOSE_SERVER}" "${TLS_SERVER_NAME}"
  prepare_compose_persistence_dirs
fi

echo "${OUT}"
if [[ "${COMPOSE_MODE}" == "true" ]]; then
  echo "${COMPOSE_OUT}"
fi
