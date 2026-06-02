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
#   # Or read the connection string from a generated run-profile env file:
#   ./scripts/persistence/apply-approval-migrations.sh --from-env <path-to-env-file>
#
# Examples:
#   ./scripts/persistence/apply-approval-migrations.sh "Host=localhost;Port=5432;Database=infra-gate;Username=infra-gate;Password=infra-gate-dev-password"
#
#   ./scripts/persistence/apply-approval-migrations.sh --from-env deploy/generated/local-compose.env

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

connection_string=""

if [[ $# -eq 0 ]]; then
    echo "usage: $(basename "$0") <connection-string>" >&2
    echo "       $(basename "$0") --from-env <path-to-env-file>" >&2
    exit 1
fi

if [[ "$1" == "--from-env" ]]; then
    if [[ $# -lt 2 ]]; then
        echo "error: --from-env requires a path" >&2
        exit 1
    fi
    env_path="$2"
    if [[ ! -f "$env_path" ]]; then
        echo "error: env file not found: $env_path" >&2
        exit 1
    fi
    while IFS='=' read -r key value; do
        if [[ "$key" == "InfraGate__Approval__Postgres__ConnectionString" ]]; then
            connection_string="${value%$'\r'}"
            break
        fi
    done < "$env_path"
    if [[ -z "$connection_string" ]]; then
        echo "error: InfraGate__Approval__Postgres__ConnectionString is missing or empty in $env_path" >&2
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
