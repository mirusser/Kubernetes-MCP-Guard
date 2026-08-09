#!/usr/bin/env bash
# install-kubernetes-mcp-server.sh — installs the pinned upstream
# containers/kubernetes-mcp-server binary for use as the Gateway's secondary,
# read-only-only downstream MCP process (see docs/adr for the decision record).
#
# Installs to <repo-root>/.tools/bin/kubernetes-mcp-server via `go install`,
# independent of the caller's GOPATH/GOBIN, so the install location is
# predictable for both local dev and the Docker build context.
#
# Usage:
#   ./scripts/install-kubernetes-mcp-server.sh
#
# Prerequisites:
#   - Go 1.26.3+ toolchain (https://go.dev/dl/) on PATH — matches the pinned
#     kubernetes-mcp-server release's go.mod minimum.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Pinned upstream release. Bump deliberately; no automated-update policy yet.
KUBERNETES_MCP_SERVER_VERSION="v0.0.64"
KUBERNETES_MCP_SERVER_MODULE="github.com/containers/kubernetes-mcp-server/cmd/kubernetes-mcp-server"

INSTALL_DIR="${REPO_ROOT}/.tools/bin"

if ! command -v go >/dev/null 2>&1; then
  echo "error: Go toolchain not found on PATH. Install Go 1.26.3+ from https://go.dev/dl/ first." >&2
  exit 1
fi

mkdir -p "${INSTALL_DIR}"

echo "Installing kubernetes-mcp-server ${KUBERNETES_MCP_SERVER_VERSION} to ${INSTALL_DIR}..."
GOBIN="${INSTALL_DIR}" go install "${KUBERNETES_MCP_SERVER_MODULE}@${KUBERNETES_MCP_SERVER_VERSION}"

echo "Installed:"
go version -m "${INSTALL_DIR}/kubernetes-mcp-server" | grep -E '^\s*mod\s' | head -1
