#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NAMESPACE="mcp-nginx-demo"
SERVICE_ACCOUNT="infra-gate-mcp"
OUT="${ROOT}/.kube/mcp-nginx-demo.config"

kubectl apply -f "${ROOT}/deploy/minikube/rbac.yaml"

SERVER="$(kubectl config view --minify -o jsonpath='{.clusters[0].cluster.server}')"
CA_DATA="$(kubectl config view --raw --minify -o jsonpath='{.clusters[0].cluster.certificate-authority-data}')"
if [[ -z "${CA_DATA}" ]]; then
  CA_FILE="$(kubectl config view --raw --minify -o jsonpath='{.clusters[0].cluster.certificate-authority}')"
  CA_DATA="$(base64 -w 0 "${CA_FILE}")"
fi
TOKEN="$(kubectl -n "${NAMESPACE}" create token "${SERVICE_ACCOUNT}" --duration=24h)"

mkdir -p "$(dirname "${OUT}")"

cat > "${OUT}" <<EOF
apiVersion: v1
kind: Config
clusters:
  - name: minikube
    cluster:
      server: ${SERVER}
      certificate-authority-data: ${CA_DATA}
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

chmod 600 "${OUT}"

echo "${OUT}"
