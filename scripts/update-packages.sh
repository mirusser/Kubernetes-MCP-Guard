#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Controls how far updates are allowed:
#   Patch  → lock to current minor (patch updates only)
#   Minor  → lock to current major (minor + patch updates, default)
#   Major  → no lock (all updates including breaking)
SCOPE="${1:-Minor}"

check_tool() {
    local tool="$1"
    if ! dotnet tool list -g 2>/dev/null | grep -q "^$tool " && \
       ! dotnet tool list 2>/dev/null | grep -q "^$tool "; then
        echo "Required tool '$tool' is not installed."
        echo "Install globally:  dotnet tool install -g dotnet-outdated-tool"
        echo "Install locally:   dotnet tool install dotnet-outdated-tool"
        exit 1
    fi
}

# Map user-friendly scope to dotnet-outdated --version-lock values:
#   -vl Major = allow only minor+patch (lock the major version)
#   -vl Minor = allow only patch (lock major+minor)
#   -vl None  = allow all updates
scope_to_version_lock() {
    case "$1" in
        Patch) echo "Minor" ;;
        Minor) echo "Major" ;;
        Major) echo "None"  ;;
        *)
            echo "Invalid scope '$1'. Use: Patch, Minor (default), or Major." >&2
            exit 1
            ;;
    esac
}

echo "Checking required tools..."
check_tool "dotnet-outdated-tool"

VERSION_LOCK="$(scope_to_version_lock "$SCOPE")"
echo "Updating packages (scope: $SCOPE, version-lock: $VERSION_LOCK)..."
cd "$REPO_ROOT"
dotnet outdated -u:Auto -vl "$VERSION_LOCK"

echo ""
echo "Package update complete. Review changes with: git diff"
