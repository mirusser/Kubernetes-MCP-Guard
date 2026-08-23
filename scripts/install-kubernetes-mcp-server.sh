#!/usr/bin/env bash
# install-kubernetes-mcp-server.sh — installs the pinned upstream
# containers/kubernetes-mcp-server binary for use as the Gateway's secondary,
# read-only-only downstream MCP process (see docs/adr for the decision record).
#
# Downloads the official GitHub release asset over HTTPS, verifies its
# SHA-256 against the checked-in manifest (kubernetes-mcp-server.manifest.json,
# next to this script) and asserts the binary's own --version output before
# it is ever trusted. Fails closed on checksum mismatch, unsupported
# platform, or a reported version that doesn't match the pin -- the binary
# is never installed to its final path in any of those cases.
#
# Installs to <repo-root>/.tools/bin/kubernetes-mcp-server, independent of
# the caller's environment, so the install location is predictable for both
# local dev and the Docker build context.
#
# Usage:
#   ./scripts/install-kubernetes-mcp-server.sh
#
# Prerequisites:
#   - curl, sha256sum, jq on PATH.
#
# Overridable for testing (see install-kubernetes-mcp-server.tests.sh):
#   - KUBERNETES_MCP_SERVER_MANIFEST_PATH
#   - KUBERNETES_MCP_SERVER_INSTALL_DIR
#   - KUBERNETES_MCP_SERVER_DOWNLOAD_BASE_URL

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

MANIFEST_PATH="${KUBERNETES_MCP_SERVER_MANIFEST_PATH:-${SCRIPT_DIR}/kubernetes-mcp-server.manifest.json}"
INSTALL_DIR="${KUBERNETES_MCP_SERVER_INSTALL_DIR:-${REPO_ROOT}/.tools/bin}"
DOWNLOAD_BASE_URL="${KUBERNETES_MCP_SERVER_DOWNLOAD_BASE_URL:-https://github.com/containers/kubernetes-mcp-server/releases/download}"
BINARY_PATH="${INSTALL_DIR}/kubernetes-mcp-server"

for tool in curl sha256sum jq; do
  if ! command -v "${tool}" >/dev/null 2>&1; then
    echo "error: '${tool}' not found on PATH. Install it first." >&2
    exit 1
  fi
done

if [[ ! -f "${MANIFEST_PATH}" ]]; then
  echo "error: manifest not found at ${MANIFEST_PATH}." >&2
  exit 1
fi

VERSION="$(jq -er '.version' "${MANIFEST_PATH}")"

OS="$(uname -s)"
ARCH="$(uname -m)"
case "${OS}" in
  Linux) PLATFORM_OS="linux" ;;
  Darwin) PLATFORM_OS="darwin" ;;
  *)
    echo "error: unsupported operating system '${OS}'." >&2
    exit 1
    ;;
esac
case "${ARCH}" in
  x86_64 | amd64) PLATFORM_ARCH="amd64" ;;
  arm64 | aarch64) PLATFORM_ARCH="arm64" ;;
  *)
    echo "error: unsupported CPU architecture '${ARCH}'." >&2
    exit 1
    ;;
esac
PLATFORM="${PLATFORM_OS}-${PLATFORM_ARCH}"

EXPECTED_SHA256="$(jq -r --arg platform "${PLATFORM}" '.checksums[$platform] // empty' "${MANIFEST_PATH}")"
if [[ -z "${EXPECTED_SHA256}" ]]; then
  echo "error: no pinned checksum for platform '${PLATFORM}' in ${MANIFEST_PATH}." >&2
  exit 1
fi

ASSET_NAME="kubernetes-mcp-server-${PLATFORM}"
DOWNLOAD_URL="${DOWNLOAD_BASE_URL}/${VERSION}/${ASSET_NAME}"

mkdir -p "${INSTALL_DIR}"
TMP_FILE="$(mktemp "${INSTALL_DIR}/.kubernetes-mcp-server.XXXXXX")"
cleanup() { rm -f "${TMP_FILE}"; }
trap cleanup EXIT

echo "Downloading kubernetes-mcp-server ${VERSION} (${PLATFORM})..."
curl -fsSL -o "${TMP_FILE}" "${DOWNLOAD_URL}"

ACTUAL_SHA256="$(sha256sum "${TMP_FILE}" | cut -d' ' -f1)"
if [[ "${ACTUAL_SHA256}" != "${EXPECTED_SHA256}" ]]; then
  echo "error: checksum mismatch for ${ASSET_NAME}. expected ${EXPECTED_SHA256}, got ${ACTUAL_SHA256}." >&2
  exit 1
fi

chmod +x "${TMP_FILE}"

REPORTED_VERSION="$("${TMP_FILE}" --version 2>&1 | tr -d '[:space:]')"
if [[ "${REPORTED_VERSION}" != "${VERSION}" ]]; then
  echo "error: installed binary reports version '${REPORTED_VERSION}', expected '${VERSION}'." >&2
  exit 1
fi

mv "${TMP_FILE}" "${BINARY_PATH}"
trap - EXIT

cp "${MANIFEST_PATH}" "${INSTALL_DIR}/kubernetes-mcp-server.manifest.json"

echo "Installed kubernetes-mcp-server ${VERSION} to ${BINARY_PATH}"
