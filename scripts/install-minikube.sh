#!/usr/bin/env bash
# install-minikube.sh — installs the pinned upstream kubernetes/minikube
# binary used for local dev and CI's disposable integration-test cluster.
#
# Downloads the official GitHub release asset over HTTPS, verifies its
# SHA-256 against the checked-in manifest (minikube.manifest.json, next to
# this script), and sanity-checks the downloaded binary's own `version`
# output before it is ever trusted. Fails closed on checksum mismatch,
# unsupported platform, or a reported version that doesn't match the pin --
# the binary is never installed to its final path in any of those cases.
#
# Installs to /usr/local/bin/minikube by default so it lands on PATH for
# both interactive use and the other scripts in this repo that shell out to
# `minikube`/`kubectl` directly. Elevates via sudo only if the install
# directory isn't already writable (this is how CI's GitHub-hosted runner
# installs it too -- see .github/workflows/integration-tests.yml).
#
# Idempotent: if a minikube already on PATH reports the exact pinned
# version, does nothing.
#
# Usage:
#   ./scripts/install-minikube.sh
#
# Prerequisites:
#   - curl, sha256sum, jq on PATH.
#
# Overridable for testing:
#   - MINIKUBE_MANIFEST_PATH
#   - MINIKUBE_INSTALL_DIR
#   - MINIKUBE_DOWNLOAD_BASE_URL

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

MANIFEST_PATH="${MINIKUBE_MANIFEST_PATH:-${SCRIPT_DIR}/minikube.manifest.json}"
INSTALL_DIR="${MINIKUBE_INSTALL_DIR:-/usr/local/bin}"
DOWNLOAD_BASE_URL="${MINIKUBE_DOWNLOAD_BASE_URL:-https://github.com/kubernetes/minikube/releases/download}"
BINARY_PATH="${INSTALL_DIR}/minikube"

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

if command -v minikube >/dev/null 2>&1 && minikube version 2>&1 | grep -qF "${VERSION}"; then
  echo "minikube ${VERSION} already installed at $(command -v minikube); skipping."
  exit 0
fi

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
  echo "Add one: fetch the release checksum from https://github.com/kubernetes/minikube/releases/tag/${VERSION} and add a \"${PLATFORM}\" entry." >&2
  exit 1
fi

ASSET_NAME="minikube-${PLATFORM}"
DOWNLOAD_URL="${DOWNLOAD_BASE_URL}/${VERSION}/${ASSET_NAME}"

TMP_DIR="$(mktemp -d)"
TMP_FILE="${TMP_DIR}/minikube"
cleanup() { rm -rf "${TMP_DIR}"; }
trap cleanup EXIT

echo "Downloading minikube ${VERSION} (${PLATFORM})..."
curl -fsSL -o "${TMP_FILE}" "${DOWNLOAD_URL}"

ACTUAL_SHA256="$(sha256sum "${TMP_FILE}" | cut -d' ' -f1)"
if [[ "${ACTUAL_SHA256}" != "${EXPECTED_SHA256}" ]]; then
  echo "error: checksum mismatch for ${ASSET_NAME}. expected ${EXPECTED_SHA256}, got ${ACTUAL_SHA256}." >&2
  exit 1
fi

chmod +x "${TMP_FILE}"

REPORTED_VERSION="$("${TMP_FILE}" version 2>&1)"
if ! grep -qF "${VERSION}" <<<"${REPORTED_VERSION}"; then
  echo "error: downloaded binary reports version '${REPORTED_VERSION}', expected '${VERSION}'." >&2
  exit 1
fi

if [[ -w "${INSTALL_DIR}" ]]; then
  install -m 0755 "${TMP_FILE}" "${BINARY_PATH}"
elif command -v sudo >/dev/null 2>&1; then
  echo "${INSTALL_DIR} is not writable; installing with sudo (you may be prompted for your password)..."
  sudo install -m 0755 "${TMP_FILE}" "${BINARY_PATH}"
else
  echo "error: ${INSTALL_DIR} is not writable and sudo is not available." >&2
  echo "Install manually: install -m 0755 ${TMP_FILE} <a-directory-on-PATH>/minikube" >&2
  exit 1
fi

echo "Installed minikube ${VERSION} to ${BINARY_PATH}"
