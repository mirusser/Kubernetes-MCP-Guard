#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NAMESPACE="mcp-nginx-demo"
SERVICE_ACCOUNT="infra-gate-mcp"
OUT="${ROOT}/.kube/mcp-nginx-demo.config"
COMPOSE_OUT="${ROOT}/.kube/mcp-nginx-demo.compose.config"
COMPOSE_MODE=false

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
  mkdir -p "${ROOT}/.mcp-approvals" "${ROOT}/.mcp-guardrails"
fi

echo "${OUT}"
if [[ "${COMPOSE_MODE}" == "true" ]]; then
  echo "${COMPOSE_OUT}"
fi
