#!/usr/bin/env bash
set -euo pipefail

# apply-approval-migrations.sh — Apply InfraGate approval PostgreSQL migrations.
#
# Prerequisites:
#   1. A PostgreSQL database with the connection string available.
#   2. The .NET SDK installed (dotnet build + run).
#
# Usage:
#   ./scripts/persistence/apply-approval-migrations.sh <connection-string>
#
#   # Or read the connection string from a generated appsettings file:
#   ./scripts/persistence/apply-approval-migrations.sh --from-appsettings <path-to-appsettings.json>
#
# Examples:
#   ./scripts/persistence/apply-approval-migrations.sh "Host=localhost;Port=5432;Database=infra-gate;Username=infra-gate;Password=infra-gate-dev-password"
#
#   ./scripts/persistence/apply-approval-migrations.sh --from-appsettings deploy/generated/local-compose.appsettings.json

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

connection_string=""

if [[ $# -eq 0 ]]; then
    echo "usage: $(basename "$0") <connection-string>" >&2
    echo "       $(basename "$0") --from-appsettings <path-to-appsettings.json>" >&2
    exit 1
fi

if [[ "$1" == "--from-appsettings" ]]; then
    if [[ $# -lt 2 ]]; then
        echo "error: --from-appsettings requires a path" >&2
        exit 1
    fi
    appsettings_path="$2"
    if [[ ! -f "$appsettings_path" ]]; then
        echo "error: appsettings file not found: $appsettings_path" >&2
        exit 1
    fi
    connection_string=$(python3 -c "
import json, sys
try:
    with open('$appsettings_path') as f:
        d = json.load(f)
    print(d['InfraGate']['Approval']['Postgres']['ConnectionString'])
except (KeyError, json.JSONDecodeError) as e:
    sys.stderr.write(f'error: could not read connection string from {appsettings_path}: {e}\n')
    sys.exit(1)
")
    if [[ -z "$connection_string" ]]; then
        echo "error: empty connection string in $appsettings_path" >&2
        exit 1
    fi
else
    connection_string="$1"
fi

echo "Applying approval PostgreSQL migrations ..."

dotnet run \
    --project "$REPO_ROOT/tools/approval-postgres-migrations/ApplyApprovalPostgresMigrations.csproj" \
    --configuration Release \
    -- \
    --connection-string "$connection_string"

echo "Migrations complete."
