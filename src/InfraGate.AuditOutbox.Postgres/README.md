# InfraGate.AuditOutbox.Postgres

`InfraGate.AuditOutbox.Postgres` is the Npgsql implementation of the audit-outbox engine. It provides `PostgresAuditOutboxCore` (the hash-chain writer), `PostgresAuditOutboxMigrationRunner` (per-schema idempotent migration), and `AuditCanonicalJson` (the stable canonicalization rule).

**Owns:** Postgres-specific audit-outbox implementation (no per-component wrappers)

See [ADR-0020](../../docs/adr/0020-audit-outbox-architecture.md) and [InfraGate.AuditOutbox README](../InfraGate.AuditOutbox/README.md).

## Hash Chain

Each `AppendAsync` call:

1. Acquires `pg_advisory_xact_lock(LockCategory, StreamLockKey(schema))` within the caller's transaction — serialises writes to the same stream, releases automatically on transaction end.
2. Reads the latest `event_hash` from `{schema}.audit_outbox ORDER BY audit_sequence DESC LIMIT 1`.
3. Computes `event_hash = SHA-256( (previous_event_hash ?? "") || AuditCanonicalJson.Serialize(canonical_input) )`.
4. INSERTs the row and returns the assigned `audit_sequence`.

The first row of each stream has `previous_event_hash = NULL` (the NULL-seed). Verifiers know that row 1 → NULL.

`AuditCanonicalJson` delegates to `ApprovalCanonicalJson` from `InfraGate.Approvals`, which is the same canonicalization rule used for Plan Envelope digests. It is treated as a stable contract; any change must be paired with an ADR update.

## Migration Runner

```csharp
await PostgresAuditOutboxMigrationRunner.ApplyAsync(
    dataSource, "observer", Path.Combine(AppContext.BaseDirectory, "Migrations"), ct);
```

- Idempotent: re-applying is a no-op if checksums match.
- Rejects a migration whose stored checksum has drifted (throws `InvalidOperationException`).
- Uses `pg_advisory_lock` (session-level) to serialise concurrent runner instances during startup.
- Each component's SQL migration file must itself create `{schema}.schema_migrations` — the runner inserts the tracking record into it after running the file.

## DI Registration

```csharp
services.AddPostgresAuditOutbox(dataSource);
```

Registers `IAuditOutboxCore` (internal). Per-stream wrappers call their own extension method which chains onto this registration.

## Verifying the Chain

The following SQL reconstructs and verifies the hash chain for any `audit_outbox` table. Run it against any stream to confirm no row has been tampered with.

```sql
-- Replace 'approvals' with 'observer' or 'planner' as needed.
WITH chain AS (
    SELECT
        audit_sequence,
        event_name,
        occurred_at_utc,
        previous_event_hash,
        event_hash,
        lag(event_hash) OVER (ORDER BY audit_sequence) AS expected_previous_hash
    FROM approvals.audit_outbox
    ORDER BY audit_sequence
)
SELECT
    audit_sequence,
    event_name,
    occurred_at_utc,
    CASE
        WHEN audit_sequence = (SELECT MIN(audit_sequence) FROM approvals.audit_outbox)
            THEN previous_event_hash IS NULL
        ELSE previous_event_hash = expected_previous_hash
    END AS chain_ok,
    previous_event_hash,
    expected_previous_hash
FROM chain
WHERE
    (audit_sequence = (SELECT MIN(audit_sequence) FROM approvals.audit_outbox)
        AND previous_event_hash IS NOT NULL)
    OR
    (audit_sequence > (SELECT MIN(audit_sequence) FROM approvals.audit_outbox)
        AND previous_event_hash IS DISTINCT FROM expected_previous_hash);
```

An empty result set means the chain is intact. Any returned row identifies a break or tampered `previous_event_hash`.

To verify the `event_hash` values themselves (i.e., that the stored hash matches the recomputed hash from the row contents), use the `CrossStreamForensicTests` in `tests/InfraGate.AuditOutbox.Tests/` as a reference for the canonical hash input construction.

A CLI verifier tool is a future follow-up — see ADR-0020 Out of Scope.

## Cross-Stream Forensic Query

Join all three streams by `anomaly_id` and `plan_id` to reconstruct the full Observer→Planner→Approvals timeline:

```sql
SELECT 'observer' AS stream, event_name, occurred_at_utc, NULL::text AS plan_id, anomaly_id
FROM observer.audit_outbox
WHERE anomaly_id = '<anomaly-id>'
UNION ALL
SELECT 'planner', event_name, occurred_at_utc, plan_id, anomaly_id
FROM planner.audit_outbox
WHERE anomaly_id = '<anomaly-id>' OR plan_id = '<plan-id>'
UNION ALL
SELECT 'approvals', event_name, occurred_at_utc, plan_id, NULL
FROM approvals.audit_outbox
WHERE plan_id = '<plan-id>'
ORDER BY occurred_at_utc;
```

Expected output for a successful remediation cycle:

| stream | event_name | occurred_at_utc |
|---|---|---|
| observer | anomaly.detected | … |
| planner | handoff.received | … |
| planner | propose_plan.succeeded | … |
| approvals | plan.created | … |
| approvals | challenge.created | … |
| approvals | challenge.approved | … |
| approvals | execution.succeeded | … |
