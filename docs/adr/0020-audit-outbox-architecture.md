# ADR-0020: Audit Outbox Architecture — Per-Component Audit Streams with Same-Transaction Hash Chain

**Date:** 2026-05-27
**Status:** Accepted

---

## Context

InfraGate today persists approval lifecycle audit events transactionally in `approvals.audit_events`. The **Anomaly Observer** and **Remediation Planner**, both autonomous MCP clients, emit only structured Serilog and metrics — they have no durable, tamper-evident record of the claims they make. The system has three observability gaps:

1. **Agent durability.** Observer's "I detected this anomaly" and Planner's "I proposed this plan because of this anomaly" exist only in log streams. A forensic question — *"what did the Planner decide for anomaly X on day Y, and was that decision the one that produced plan Z?"* — is not answerable from durable state.
2. **Tamper evidence.** Today's `approvals.audit_events` rows are bare INSERTs with no integrity proof. Any DB-write attacker can mutate `event_name`, `occurred_at_utc`, or payload columns silently.
3. **Future emission.** No reliable handoff exists for shipping audit events to an external sink (OpenTelemetry, SIEM, immutable archive).

CONTEXT.md scopes the existing **Audit Trail** and **Audit Spine** terms narrowly to the approval-lifecycle. ADR-0015 deliberately excluded the **Anomaly Observer** from the **Audit Spine** to keep its semantics precise. Any expansion of agent audit must respect that boundary.

## Considered Options

### Where audit lives

- **Shared Postgres instance, schema per component.** Single deployable; per-stream isolation by schema; cross-stream correlation by IDs.
- **Per-agent Postgres instances.** Maximum blast-radius isolation; three migration runners; three connection strings; more ops cost.
- **Separate audit database.** Operational state and audit decoupled at the DB level; three deployables to coordinate at write time.

### Transactional model for the audit write

- **Same-transaction with state mutation.** Audit row committed atomically with the operational state change. Preserves the current Approvals behaviour.
- **Decoupled via in-process channel.** Lock-free but reintroduces the "state committed, audit lost" split-brain that the outbox pattern is meant to prevent.
- **Optimistic retry on unique constraint.** Lock-free but redoes the entire enclosing transaction (including the state mutation) on every chain conflict.

### Tamper evidence

- **Per-stream hash chain over the canonical row encoding.** Each component's stream is independently tamper-evident; cross-stream correlation is by IDs, not by shared chain.
- **Unified hash chain across all streams.** Forces serialization across components; one shared writer; cross-component DB contention.
- **No hash chain.** Bare INSERTs; no integrity proof. Current state.

### What lives in top-level columns vs payload JSON

- **Adapter-specific fields top-level** (e.g., `tool_name`, `namespace`, `resource_kind` for Kubernetes). Better grep-ability and indexed queries; couples the outbox row shape to one **Domain Adapter**.
- **Adapter-specific fields inside `payload_json_text`.** Honours the **Generic Approval Core** / **Domain Adapter** seam defined in CONTEXT.md. JSONB GIN indexes recover query ergonomics where genuinely needed.

## Decision

InfraGate adopts a **per-component audit outbox** with same-transaction hash-chain persistence:

1. **One Postgres instance shared with operational state, one schema per runtime component.** `approvals.audit_outbox` (replaces `approvals.audit_events`), `observer.audit_outbox` (new), `planner.audit_outbox` (new). Each schema's outbox table contains only the correlation columns its stream produces (e.g., `cycle_id`, `anomaly_id`, `dedupe_key` on Observer; `plan_id`, `challenge_id`, `grant_id`, `execution_attempt_id` on Approvals; `proposal_id`, `anomaly_id`, `plan_id` on Planner).

2. **Same-transaction atomicity is preserved for Approvals.** The Postgres advisory lock primitive `pg_advisory_xact_lock(category, stream_key)` is acquired at the start of every outbox-writing transaction; the audit INSERT runs inside the same Npgsql transaction as the state mutation. The lock is auto-released on commit/rollback. Per-stream lock keys mean Approvals never blocks Observer or Planner writes.

3. **The hash chain covers the canonical encoding of the whole audit-relevant row** — `event_name`, `occurred_at_utc`, the row's correlation columns, `actor_subject`, `actor_client_id`, `outcome`, `reason`, and `payload_json_text`. `previous_event_hash` is NULL on the first row of each stream. The canonicalization rule reuses `ApprovalCanonicalJson` so the declared rule is the same as for the **Plan Envelope** digest. Per-stream chains are independent; cross-stream correlation is by IDs (`plan_id`, `anomaly_id`, etc.), never by a shared chain.

4. **Adapter-specific audit data lives inside `payload_json_text`, never as top-level columns.** The **Adapter Audit Payload** glossary term in CONTEXT.md is the load-bearing seam; promoting Kubernetes-adapter fields like `namespace`, `resource_kind`, `tool_name` to top-level columns would couple every component's outbox row shape to one domain adapter and would re-litigate ADR-0001 in the audit layer.

5. **One deep core, three thin wrappers.** A shared `IAuditOutboxCore` in `InfraGate.AuditOutbox(.Postgres)` owns hashing, locking, canonicalization, sequence assignment, and INSERT. Per-stream wrappers `IApprovalAuditOutbox`, `IObserverAuditOutbox`, `IPlannerAuditOutbox` live in each component's project and translate component-specific entry records into the canonical row shape. Each wrapper exposes two overloads — one accepting `(NpgsqlConnection, NpgsqlTransaction)` for callers that already own a transaction (Approvals' state-mutation paths), and one without for callers whose audit insert is the only work in its transaction (Observer, Planner).

6. **`IApprovalAuditPublisher` is replaced.** All current direct `InsertAuditAsync` call sites in `PostgresApprovalPersistence` and the pre-execution gate's audit emission route through `IApprovalAuditOutbox`. The old interface, `NoOpApprovalAuditPublisher`, and the `PlanAudit` record are removed in the same migration.

7. **The OpenTelemetry / external-sink publisher is deferred.** Outbox rows carry `published_at_utc`, `publish_attempts`, `last_publish_error` columns from day one but no publisher hosted service ships in this work. Rows accumulate; the schema is ready when external emission becomes a real requirement. A follow-up ADR will specify retention, replay semantics, and external-sink choice.

## Relationship to ADR-0015

This ADR refines ADR-0015 without contradicting it. ADR-0015's load-bearing constraint — the **Anomaly Observer never writes through `IApprovalAuditPublisher` and never emits Audit Spine events** — is preserved exactly. The Observer's new **Audit Stream** is a distinct mechanism:

- The Observer's audit rows land in `observer.audit_outbox`, never in `approvals.audit_outbox`.
- The Observer references `InfraGate.AuditOutbox(.Postgres)`, not `InfraGate.Approvals`, for audit publishing.
- The **Audit Spine** — the generic approval-lifecycle event sequence — is unchanged.
- CONTEXT.md's new **Audit Stream** term documents the boundary: per-component streams correlate by IDs, never by a shared chain, and Observer/Planner streams do not extend the **Audit Spine**.

What ADR-0015 listed as the observable surface for Observer activity (Serilog events, metrics, `IAnomalyHandoffSink` batches) is now extended by a fourth lane: durable, tamper-evident outbox rows. The same logic applies to the **Remediation Planner**.

## Consequences

- The **Audit Trail** glossary term (approval-lifecycle) is now persisted via the **Approval Authority**'s **Audit Stream**. The two terms remain conceptually distinct — **Audit Trail** is the lifecycle abstraction; **Audit Stream** is the per-component persistence — and CONTEXT.md is updated accordingly.
- Observer and Planner now require a Postgres connection string and run a startup migration runner each (mirroring `PostgresApprovalMigrationRunner`).
- Per-stream advisory locks bound peak audit throughput per stream to roughly 500–1000 writes/sec on a local Postgres. This is 2–3 orders of magnitude over expected load. A future bottleneck would be addressable via optimistic retry without changing row shape or chain semantics.
- The hash chain ties tamper evidence to canonical-row stability. Any future schema change must declare its canonicalization impact: if columns change, the canonicalization rule changes, and rows produced under the new rule can only be verified against verifiers that know it. The chain's declared `Canonicalization` covers this contract.
- The retrofit edits `Migrations/0001-initial-approval-persistence.sql` in place: the repository is still in the experimental phase, no production data exists, and dev environments must drop the local `approvals` schema (or the database) before next startup because the migration runner's checksum guard will otherwise refuse to start. The retrofit is greenfield by deliberate choice — no backfill, no compatibility shim.
- Adapter-specific audit data continues to live inside `payload_json_text`. Top-level columns remain generic-spine only.
- The deferral of the external-sink publisher means outbox rows accumulate. The `published_at_utc` / `publish_attempts` / `last_publish_error` columns sit dormant until a future ADR enables them.
- New project pair `InfraGate.AuditOutbox` + `InfraGate.AuditOutbox.Postgres` plus `InfraGate.AuditOutbox.Tests`. The pattern mirrors `InfraGate.Approvals` / `InfraGate.Approvals.Postgres`.
- Project-reference assertions in unit tests enforce that `InfraGate.Observer` and `InfraGate.Planner` reference `InfraGate.AuditOutbox(.Postgres)` for audit, not `InfraGate.Approvals`. Architectural separation is a build break, not a runtime surprise.
