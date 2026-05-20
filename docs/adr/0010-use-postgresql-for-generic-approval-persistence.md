# ADR-0010: Use PostgreSQL for Generic Approval Persistence

**Date:** 2026-05-20
**Status:** Accepted

---

## Context

The Generic Approval Core currently persists Plan Envelopes, Approval Challenges, Approval Grants, applied markers, and approval audit events as JSON files and JSONL under `K8S_MCP_APPROVAL_ROOT`. That file layout helped prove the Mutation Approval Profile, but it leaves lifecycle transitions split across independent file writes, makes concurrency protection weak around `execute_approved_plan`, and ties approval audit inspection to local mounted storage.

InfraGate now needs a durable relational persistence layer for the approval lifecycle while preserving the existing ownership boundaries: the Generic Approval Core owns approval state and audit, Domain Adapters emit adapter payloads but do not persist the Audit Trail, the Gateway owns guardrail audit, and ASP.NET Data Protection remains platform infrastructure.

## Decision

InfraGate will replace file-backed Generic Approval Core persistence with PostgreSQL, implemented as a new `InfraGate.Approvals.Postgres` adapter project. `InfraGate.Approvals` will expose approval-domain persistence abstractions and result types, while PostgreSQL-specific dependencies such as Npgsql and Dapper remain isolated in the adapter project.

The first slice is PostgreSQL-only for approval runtime persistence. There will be no supported file-backed approval store, no dual-write migration path, and no approval `audit.jsonl` export in the first implementation. Existing pending file plans can be re-requested after the switch.

Approval tables will live in an internal `approvals` schema in the same database as the gateway's configured runtime database. The schema name is not external configuration. The only new approval persistence runtime setting is the PostgreSQL connection string in generated JSON configuration from run profiles; no new approval persistence environment variable will be added.

Approval records will be stored with explicit relational columns for approval-flow lookup, constraints, and safety checks, plus canonical JSON text for the full source records. The persistence contract will not depend on PostgreSQL `jsonb` behavior for canonicalization, integrity checks, or adapter payload semantics. The Generic Approval Core remains responsible for canonical text and hash generation so a future database swap stays plausible.

## Consequences

- `K8S_MCP_APPROVAL_ROOT` remains for now as the existing file-backed ASP.NET Data Protection path anchor. It is no longer the approval state or approval audit store. This naming debt is intentional and should be cleaned up in a later platform configuration slice.
- Guardrail audit remains file-backed under Gateway ownership and is out of scope for this decision.
- Serilog file logs remain operational telemetry and are out of scope.
- `InfraGate.McpServer` must not receive approval database configuration and should not know about Generic Approval Core persistence. The downstream server remains a swappable domain execution substrate.
- PostgreSQL migrations are explicit deploy/script work, not automatic gateway startup mutation. Startup validates that the configured schema version is present and fails fast when any required database piece is missing or stale.
- Migrations use plain SQL files, checksums, per-file transactions, migration locking, and fail with rollback semantics. Separate migration and application database roles are expected.
- Approval persistence tests that cross the database seam use PostgreSQL through Testcontainers and are integration tests. In-memory or fake database persistence is not used for this slice.
- Approval audit is mandatory transactional lifecycle state. When an approval state transition requires audit events, the state rows and audit rows commit or roll back together.
- PostgreSQL will add an operational execution-claim table keyed by Plan Identifier to prevent concurrent execution of the same plan before Kubernetes mutation begins. Claims are separate from immutable lifecycle records and fail closed if a process crashes before recording a terminal outcome.

## Considered Options

**Keep JSON files as the runtime store.** Rejected because the file store cannot provide the transactionality, concurrency protection, and operational queryability needed for the next approval lifecycle shape.

**Support both file and PostgreSQL adapters.** Rejected because this is a vertical slice replacement, not a pluggable storage feature. Supporting both would increase surface area and create two integrity sources without a current product need.

**Use PostgreSQL `jsonb` as the main record store.** Rejected for the first slice because correctness must not depend on PostgreSQL JSON representation or behavior. Canonical JSON text and hashes remain app-owned.

**Use an ORM and ORM migrations.** Rejected because the desired persistence shape is explicit SQL, small relational constraints, and canonical JSON text, not an entity model. Dapper over Npgsql keeps SQL visible without adding ORM schema conventions.

**Run migrations automatically at gateway startup.** Rejected because approval persistence is safety-critical infrastructure. Schema mutation should be an explicit deployment step, while the app should only validate compatibility at startup.
