#!/usr/bin/env bash
# install-kubernetes-mcp-server.tests.sh — offline tests for
# install-kubernetes-mcp-server.sh's verification gates. Serves fixture
# "release assets" from a local file:// URL (via the installer's
# KUBERNETES_MCP_SERVER_DOWNLOAD_BASE_URL override) so no network access or
# real upstream binary is required, then asserts each failure mode is fully
# closed: the binary never reaches its final install path.
#
# Usage:
#   ./scripts/install-kubernetes-mcp-server.tests.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALLER="${SCRIPT_DIR}/install-kubernetes-mcp-server.sh"
PLATFORM="linux-amd64"

WORK_DIR="$(mktemp -d)"
cleanup() { rm -rf "${WORK_DIR}"; }
trap cleanup EXIT

FAILURES=0

fail() {
  echo "FAIL: $1" >&2
  FAILURES=$((FAILURES + 1))
}

pass() {
  echo "PASS: $1"
}

# Writes a fixture "release asset" that behaves like a real binary for
# `--version`, drops it into a fixture release directory, and writes a
# fixture manifest pointing at it. Echoes the asset's real sha256.
make_fixture_release() {
  local case_dir="$1" version="$2" reported_version="$3"
  local assets_dir="${case_dir}/assets/${version}"
  mkdir -p "${assets_dir}"

  local asset_path="${assets_dir}/kubernetes-mcp-server-${PLATFORM}"
  cat > "${asset_path}" <<EOF
#!/usr/bin/env bash
echo "${reported_version}"
EOF
  chmod +x "${asset_path}"

  sha256sum "${asset_path}" | cut -d' ' -f1
}

write_manifest() {
  local case_dir="$1" version="$2" checksum_json="$3"
  cat > "${case_dir}/manifest.json" <<EOF
{
  "version": "${version}",
  "checksums": ${checksum_json}
}
EOF
}

run_installer() {
  local case_dir="$1"
  KUBERNETES_MCP_SERVER_MANIFEST_PATH="${case_dir}/manifest.json" \
    KUBERNETES_MCP_SERVER_INSTALL_DIR="${case_dir}/install" \
    KUBERNETES_MCP_SERVER_DOWNLOAD_BASE_URL="file://${case_dir}/assets" \
    "${INSTALLER}"
}

test_success() {
  local name="success: valid checksum and matching reported version installs the binary"
  local case_dir="${WORK_DIR}/success"
  mkdir -p "${case_dir}"
  local version="v9.9.9"
  local checksum
  checksum="$(make_fixture_release "${case_dir}" "${version}" "${version}")"
  write_manifest "${case_dir}" "${version}" "{\"${PLATFORM}\": \"${checksum}\"}"

  if ! run_installer "${case_dir}" >"${case_dir}/stdout.log" 2>"${case_dir}/stderr.log"; then
    fail "${name} (installer exited non-zero: $(cat "${case_dir}/stderr.log"))"
    return
  fi

  local installed="${case_dir}/install/kubernetes-mcp-server"
  if [[ ! -x "${installed}" ]]; then
    fail "${name} (binary not installed at ${installed})"
    return
  fi

  local actual
  actual="$("${installed}" --version)"
  if [[ "${actual}" != "${version}" ]]; then
    fail "${name} (installed binary reports '${actual}', expected '${version}')"
    return
  fi

  local installed_manifest="${case_dir}/install/kubernetes-mcp-server.manifest.json"
  if [[ ! -f "${installed_manifest}" ]]; then
    fail "${name} (manifest not colocated at ${installed_manifest})"
    return
  fi

  local installed_manifest_version
  installed_manifest_version="$(jq -er '.version' "${installed_manifest}")"
  if [[ "${installed_manifest_version}" != "${version}" ]]; then
    fail "${name} (colocated manifest reports version '${installed_manifest_version}', expected '${version}')"
    return
  fi

  pass "${name}"
}

test_checksum_mismatch() {
  local name="checksum mismatch: fails closed and never installs the binary"
  local case_dir="${WORK_DIR}/checksum-mismatch"
  mkdir -p "${case_dir}"
  local version="v9.9.9"
  make_fixture_release "${case_dir}" "${version}" "${version}" >/dev/null
  write_manifest "${case_dir}" "${version}" "{\"${PLATFORM}\": \"deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef\"}"

  if run_installer "${case_dir}" >"${case_dir}/stdout.log" 2>"${case_dir}/stderr.log"; then
    fail "${name} (installer succeeded despite checksum mismatch)"
    return
  fi

  if ! grep -q "checksum mismatch" "${case_dir}/stderr.log"; then
    fail "${name} (expected 'checksum mismatch' in stderr, got: $(cat "${case_dir}/stderr.log"))"
    return
  fi

  if [[ -e "${case_dir}/install/kubernetes-mcp-server" ]]; then
    fail "${name} (binary was installed despite checksum mismatch)"
    return
  fi

  pass "${name}"
}

test_unsupported_platform() {
  local name="unsupported platform: fails closed when the manifest has no entry for this host"
  local case_dir="${WORK_DIR}/unsupported-platform"
  mkdir -p "${case_dir}"
  local version="v9.9.9"
  make_fixture_release "${case_dir}" "${version}" "${version}" >/dev/null
  write_manifest "${case_dir}" "${version}" "{}"

  if run_installer "${case_dir}" >"${case_dir}/stdout.log" 2>"${case_dir}/stderr.log"; then
    fail "${name} (installer succeeded despite no checksum entry for this platform)"
    return
  fi

  if ! grep -q "no pinned checksum for platform" "${case_dir}/stderr.log"; then
    fail "${name} (expected 'no pinned checksum for platform' in stderr, got: $(cat "${case_dir}/stderr.log"))"
    return
  fi

  if [[ -e "${case_dir}/install/kubernetes-mcp-server" ]]; then
    fail "${name} (binary was installed despite unsupported platform)"
    return
  fi

  pass "${name}"
}

test_version_mismatch() {
  local name="version mismatch: fails closed when the binary reports an unexpected version"
  local case_dir="${WORK_DIR}/version-mismatch"
  mkdir -p "${case_dir}"
  local version="v9.9.9"
  local checksum
  checksum="$(make_fixture_release "${case_dir}" "${version}" "v0.0.1-not-what-was-pinned")"
  write_manifest "${case_dir}" "${version}" "{\"${PLATFORM}\": \"${checksum}\"}"

  if run_installer "${case_dir}" >"${case_dir}/stdout.log" 2>"${case_dir}/stderr.log"; then
    fail "${name} (installer succeeded despite reported-version mismatch)"
    return
  fi

  if ! grep -q "reports version" "${case_dir}/stderr.log"; then
    fail "${name} (expected 'reports version' in stderr, got: $(cat "${case_dir}/stderr.log"))"
    return
  fi

  if [[ -e "${case_dir}/install/kubernetes-mcp-server" ]]; then
    fail "${name} (binary was installed despite reported-version mismatch)"
    return
  fi

  pass "${name}"
}

test_success
test_checksum_mismatch
test_unsupported_platform
test_version_mismatch

if [[ "${FAILURES}" -gt 0 ]]; then
  echo "${FAILURES} test(s) failed." >&2
  exit 1
fi

echo "All installer tests passed."
