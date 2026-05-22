#!/bin/sh
# Approval schema migration runner for the postgres initdb.d lifecycle.
# Mounted at /docker-entrypoint-initdb.d/00-run-approval-migrations.sh inside the postgres container.
# Runs automatically on first database initialization (empty volume), before pg_isready returns success.
# Subsequent docker compose up runs with an intact volume skip this script entirely.
set -e

if [ "${INFRA_GATE_RUN_APPROVAL_MIGRATIONS:-false}" != "true" ]; then
    echo "[approval-migrations] Skipped (INFRA_GATE_RUN_APPROVAL_MIGRATIONS is not 'true')."
    exit 0
fi

MIGRATIONS_DIR="/var/approval-migrations"

if [ ! -d "$MIGRATIONS_DIR" ]; then
    echo "[approval-migrations] No migrations directory at $MIGRATIONS_DIR, skipping."
    exit 0
fi

echo "[approval-migrations] Applying approval schema migrations to database '$POSTGRES_DB'..."

for f in $(ls "$MIGRATIONS_DIR"/*.sql 2>/dev/null | sort); do
    filename=$(basename "$f")
    checksum=$(sha256sum "$f" | awk '{print toupper($1)}')

    # Check if schema_migrations table already exists (idempotency guard)
    table_exists=$(psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc \
        "SELECT COUNT(*) FROM information_schema.tables \
         WHERE table_schema='approvals' AND table_name='schema_migrations'" \
        2>/dev/null || echo "0")

    if [ "$table_exists" = "1" ]; then
        already=$(psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc \
            "SELECT COUNT(*) FROM approvals.schema_migrations WHERE filename='$filename'" \
            2>/dev/null || echo "0")
        if [ "$already" = "1" ]; then
            echo "[approval-migrations]   $filename: already applied, skipping."
            continue
        fi
    fi

    echo "[approval-migrations]   Applying $filename..."
    psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -f "$f"
    psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
        "INSERT INTO approvals.schema_migrations (filename, checksum_sha256) \
         VALUES ('$filename', '$checksum');"
    echo "[approval-migrations]   $filename: applied. checksum=$checksum"
done

echo "[approval-migrations] Done."
