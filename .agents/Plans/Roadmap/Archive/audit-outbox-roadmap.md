# Audit Outbox Implementation Roadmap

**Purpose:** Implementation plan for `InfraGate.AuditOutbox` — a per-component, append-only, tamper-evident audit persistence layer that replaces the existing `approvals.audit_events` table, extends durable audit to the **Anomaly Observer** and **Remediation Planner**, and keeps every audit write same-transaction with the state mutation it describes.

**Source:** This plan is the output of a `grill-with-docs` session that walked every design branch one decision at a time. Every choice below is intentional and traceable to a grilling Q (Q1–Q8). The architecture is fixed by [ADR-0020](../../../docs/adr/0020-audit-outbox-architecture.md). The plan is sized per [`planning-and-task-breakdown`](../../skills/planning-and-task-breakdown/SKILL.md), follows [`code-standards`](../../skills/code-standards/SKILL.md), uses the architecture vocabulary from [`improve-codebase-architecture`](../../skills/improve-codebase-architecture/SKILL.md), and respects CONTEXT.md's **Audit Trail** / **Audit Spine** / **Adapter Audit Payload** / **Audit Stream** seams.

---

## 0. Executive Summary

Today InfraGate persists approval-lifecycle audit events transactionally in `approvals.audit_events`. The **Anomaly Observer** and **Remediation Planner** emit only structured Serilog and metrics — they have no durable record of the claims they make. The existing audit rows are bare INSERTs with no tamper evidence. There is no path to ship audit to external sinks reliably.

This roadmap closes those gaps by introducing:

- **`InfraGate.AuditOutbox`** — a deep, generic audit-outbox engine: `IAuditOutboxCore` interface, canonical row shape, hash-chain semantics, conventions.
- **`InfraGate.AuditOutbox.Postgres`** — Npgsql implementation of the core with per-stream Postgres advisory lock, `ApprovalCanonicalJson`-based hashing, per-schema migration runner.
- **Per-stream wrappers in component projects**: `ApprovalAuditOutbox` (in `InfraGate.Approvals.Postgres`), `ObserverAuditOutbox` (in `InfraGate.Observer`), `PlannerAuditOutbox` (in `InfraGate.Planner`).
- **Schema retrofit**: `approvals.audit_events` becomes `approvals.audit_outbox` with new spine columns and chain fields. New schemas `observer` and `planner` with their own `audit_outbox` tables.
- **Glossary additions** to CONTEXT.md (already applied during grilling): **Audit Stream** term + relationship lines tying it to **Audit Trail**, **Audit Spine**, **Approval Authority**, **Anomaly Observer**, **Remediation Planner**.

What this is **not**:

- It is **not** a new audit semantic. The **Audit Spine** is unchanged; ADR-0015's exclusion of the Observer from the Spine is preserved exactly — Observer/Planner streams are distinct streams, not Spine extensions.
- It is **not** a publisher to external sinks. The outbox columns (`published_at_utc`, `publish_attempts`, `last_publish_error`) ship dormant. A future ADR will specify the publisher.
- It is **not** a data backfill. The repo is experimental; the existing `0001-initial-approval-persistence.sql` is edited in place; no compatibility shim, no backward-compat migration.
- It is **not** a hash chain across components. Each stream chains its own rows; cross-stream correlation is by IDs (`plan_id`, `anomaly_id`, `cycle_id`, …).

What this **is**:

- ADR-0020 codifies the load-bearing choices.
- A vertical-sliced task list: foundation first (the engine, unit-tested in isolation), then a slice through Approvals (the highest-traffic stream, retrofits the existing table), then Observer, then Planner, then verification + docs.

---

## 1. Architecture Decisions (Locked)

Every decision was made deliberately during grilling. Numbering is for reference only — not implementation order.

### 1.1 Scope, intent, and what we are not solving

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.1.1 | Primary gap solved is **agent durability** (Observer / Planner have no durable audit). Secondary gap is **tamper evidence**. Tertiary gap (external sink) is deferred. | Postgres remains the source of truth; the existing same-tx atomicity for Approvals is already adequate, so split-brain is not the problem. | Q1 |
| 1.1.2 | Postgres stays the source of truth for audit data. No external store in v1. | Avoids designing for sinks we haven't specified. | Q1 |
| 1.1.3 | The OpenTelemetry / external-sink publisher (the dirty-note's Stage 2/3) is **not in scope** for this roadmap. Outbox columns exist in the schema; no publisher hosted service ships. | A is explicitly future. A no-op publisher would age prematurely. | Q8d |

### 1.2 Storage placement

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.2.1 | Single shared Postgres instance, **one schema per runtime component**: `approvals`, `observer`, `planner`. Each owns its own writes. | Cheapest blast-radius story that still keeps schemas owned by their components. Per-DB isolation deferred until SLA/retention boundaries justify it. | Q2 (i) |
| 1.2.2 | Per-stream tables, **only the correlation columns each stream actually produces** — not a shared superset across components. | Each table is honest about its columns; cross-stream forensic queries join by IDs across schemas. | Q3.3 |
| 1.2.3 | The existing `approvals.audit_events` table is **retrofitted in place** — renamed to `approvals.audit_outbox`, new spine and chain columns added. Greenfield migration: edit `0001-initial-approval-persistence.sql` directly; experimental repo authorises destructive change. | Single migration file per schema; consistent pattern across components; no backfill code needed. | Q2 (a), Q8a |

### 1.3 Row shape

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.3.1 | Top-level columns on every outbox row (every stream): `audit_sequence bigserial`, `event_name`, `occurred_at_utc`, `actor_subject`, `actor_client_id`, `outcome`, `reason`, `previous_event_hash`, `event_hash`, `payload_json_text`, `published_at_utc`, `publish_attempts`, `last_publish_error`. | These are identity, lifecycle, generic-spine, chain, and outbox-state fields — universal to all streams. | Q3.2 |
| 1.3.2 | Per-stream correlation columns are stream-local: `plan_id`, `challenge_id`, `grant_id`, `execution_attempt_id` on `approvals`; `cycle_id`, `anomaly_id`, `dedupe_key` on `observer`; `proposal_id`, `anomaly_id`, `plan_id` on `planner`. | Per-stream NOT-NULL is enforceable at the DB layer. UNIONs project columns deliberately. | Q3.3 |
| 1.3.3 | **Adapter-specific fields live inside `payload_json_text`**, never as top-level columns. (`tool_name`, `namespace`, `resource_kind`, `resource_name`, `intent_digest`, `review_digest` for the Kubernetes adapter.) | Honours the **Generic Approval Core** / **Domain Adapter** seam. Future domain adapters do not pollute the row shape. | Q3.1, CONTEXT.md / ADR-0001 |
| 1.3.4 | Spine fields `actor_subject`, `actor_client_id`, `outcome`, `reason` are **not duplicated** inside payload — they live in columns only. | Avoids divergence between column and payload. | Q3.2 |

### 1.4 Hash chain

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.4.1 | Hash input is the **canonical encoding of the whole audit-relevant row** — `previous_event_hash` || `canonical({event_name, occurred_at_utc, correlation_columns…, actor_subject, actor_client_id, outcome, reason, payload_json_text})`. | Hashing only payload would leave hoisted spine columns un-attested. | Q4a (β) |
| 1.4.2 | Canonicalization rule is `ApprovalCanonicalJson` (already in `InfraGate.Approvals`, already used for **Plan Envelope** canonical hash). | Reuse declared rule; verifiers already understand it. | Q4a (β) |
| 1.4.3 | `previous_event_hash` is **NULL on the first row of each stream**. Verifiers know row 1 → NULL. | Stream context is implied by the table the row lives in. | Q4b (α) |
| 1.4.4 | Per-stream chain — chains never cross components. Cross-stream correlation is by IDs (`plan_id`, `anomaly_id`, …). | Unified chain would force cross-component serialization. | Q4 + Q3.3 |
| 1.4.5 | Write-time serialization via Postgres **advisory lock per stream** — `pg_advisory_xact_lock(category, stream_key)`. Lock auto-releases on tx end. Audit INSERT stays in the same Npgsql transaction as the state mutation. | Preserves Approvals' same-tx atomicity. Lock-free retry was considered and rejected (worse under contention; complicates first-row NULL handling). Channel was considered and rejected (reintroduces split-brain). | Q4c (α) |
| 1.4.6 | Lock category is a dedicated constant (e.g., `AuditOutbox = 0xA0D17_011_BX`); stream keys are stable int hashes of schema name. Two-argument advisory-lock form is used to namespace deliberately. | Avoids collision with other `pg_advisory_lock` callers in the database. | Q4c (α) sub-detail |

### 1.5 Interface shape

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.5.1 | **One deep `IAuditOutboxCore` interface, three thin per-stream wrappers**: `IApprovalAuditOutbox`, `IObserverAuditOutbox`, `IPlannerAuditOutbox`. Each wrapper translates a strongly-typed component entry record into the canonical `AuditOutboxRow` and forwards to the core. | Deep core (hashing, locking, canonicalization, sequence, INSERT) has high leverage. Wrappers preserve type safety on per-stream correlations. Adding a fourth stream is one new wrapper. | Q5a (C) |
| 1.5.2 | Each wrapper exposes **two overloads**: `AppendAsync(entry, NpgsqlConnection conn, NpgsqlTransaction tx, ct)` for callers who own a transaction (Approvals state-mutation paths) and `AppendAsync(entry, ct)` for callers whose audit insert is the only work in its tx (Observer, Planner). The second is a thin convenience that opens conn + tx, calls the first, commits. | Same-tx atomicity is preserved where it matters; agent boilerplate stays minimal. | Q5b (γ) |
| 1.5.3 | **Replace** `IApprovalAuditPublisher`. Delete `NoOpApprovalAuditPublisher` and `PlanAudit` record. The 8 direct `InsertAuditAsync` call sites in `PostgresApprovalPersistence` and the pre-execution gate's audit emission all migrate to `IApprovalAuditOutbox`. | One audit-write path across the project; eliminates a vestigial interface. | Q5c (β) |
| 1.5.4 | `IAuditOutboxCore` is `internal` to `InfraGate.AuditOutbox(.Postgres)`; per-stream wrappers are the public surface. | Callers cannot bypass the typed correlation shape. | Implicit from 1.5.1 |

### 1.6 Project placement

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.6.1 | New project pair `InfraGate.AuditOutbox` (generic interfaces, row shape, conventions) + `InfraGate.AuditOutbox.Postgres` (Npgsql core implementation + per-schema migration runner). Mirrors the existing `InfraGate.Approvals` / `InfraGate.Approvals.Postgres` pattern. | Clean seam, proven repo pattern, no upstream dep on Approvals / Observer / Planner. | Q6 (a) |
| 1.6.2 | `ApprovalAuditOutbox` wrapper lives in `InfraGate.Approvals.Postgres` (it carries Npgsql signatures); `ObserverAuditOutbox` in `InfraGate.Observer`; `PlannerAuditOutbox` in `InfraGate.Planner`. | Each component owns its event names and entry records locally. | Q6 (a) |
| 1.6.3 | New test project `InfraGate.AuditOutbox.Tests` covers chain composition, advisory-lock contention, canonicalization, NULL-seed first row. Component-level wrapper tests live in each component's existing test project. | Test surfaces are aligned with code ownership. | Q6 (a) |
| 1.6.4 | **Project-reference guards**: a unit test in each component's test project asserts that `InfraGate.Observer.csproj` and `InfraGate.Planner.csproj` do **not** reference `InfraGate.Approvals` for audit publishing. Architectural separation is a build break, not a runtime surprise. | Mirrors the existing ADR-0015 enforcement pattern. | ADR-0020 Consequences |

### 1.7 Event vocabulary

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.7.1 | **Mirror the existing dotted-lowercase naming** used in `ApprovalConventions.AuditEvents` (`plan.created`, `challenge.approved`, `pre_execution.grant.validated`, etc.). No stream prefix in `event_name` — the table/schema already names the stream. | Cross-stream queries project the prefix in only when needed. Consistent with existing convention. | Q7a |
| 1.7.2 | Event names live as `const string` in per-stream conventions classes — `ObserverAuditEvents`, `PlannerAuditEvents`, alongside the existing `ApprovalConventions.AuditEvents`. | Compile-time, typo-proof, per `code-standards`. | Q7a + code-standards |
| 1.7.3 | **Observer audit-worthy event set** (5 events): `anomaly.detected`, `anomaly.suppressed`, `anomaly.resolved`, `handoff.published`, `handoff.failed`. Cycles are operational; anomalies are the substantive claim. ILogger and metrics cover the operational layer. | Audit is for claims worth reconstructing later with cryptographic confidence. | Q7b |
| 1.7.4 | **Planner audit-worthy event set** (4 events): `handoff.received` (one row per batch), `proposal.skipped` (per anomaly), `propose_plan.succeeded`, `propose_plan.failed`. | Same forensic-claim filter applied to the Planner pipeline. | Q7c |
| 1.7.5 | The existing **`execution.blocked` value collision** is kept as-is during retrofit (`ApplyDenied`, `DryRunFailed`, `DiffFailed`, `ApplyDriftDetected` all map to `"execution.blocked"`). `reason` becomes a top-level column and provides indexed disambiguation. | The four sub-causes share a forensic identity ("execution was blocked"). De-colliding is a value-format change with consumer-breakage risk. | Q7d (α) |

### 1.8 Migration story

| # | Decision | Rationale | Source |
|---|---|---|---|
| 1.8.1 | **Edit `0001-initial-approval-persistence.sql` in place**: drop the `audit_events` definition, add the new `audit_outbox` definition with full new shape (spine columns, chain columns, outbox-state columns, the indexes). No separate `0004` retrofit migration. | Repo is experimental; the user explicitly authorised modifying existing migrations. Single source of truth per schema, no historical drift. | Q8a (user override) |
| 1.8.2 | **No backfill** of any kind. No hash backfill for old rows; no spine-column hoist from old payloads. Devs drop their local `approvals` schema (or DB) before next startup — the migration runner's checksum guard will otherwise refuse to start. | Greenfield retrofit by deliberate choice. | Q8b, Q8c |
| 1.8.3 | New migration `Migrations/0001-initial-observer-audit.sql` under `InfraGate.AuditOutbox.Postgres` (or under `InfraGate.Observer.Postgres` — see Phase 3) creates `observer` schema + `observer.schema_migrations` + `observer.audit_outbox`. | Per-schema migration ownership mirrors `PostgresApprovalMigrationRunner`. | Inferred from Q2 |
| 1.8.4 | Same pattern for Planner: `Migrations/0001-initial-planner-audit.sql` creates `planner` schema + `planner.schema_migrations` + `planner.audit_outbox`. | Same as 1.8.3. | Inferred from Q2 |
| 1.8.5 | **Migration runner choice**: extract a single generic `PostgresAuditOutboxMigrationRunner` parameterised by schema + migrations directory, OR stand up three near-identical per-component runners. Decision deferred to Phase 1 implementation review (small cost either way; abstracting is cleaner but adds one file). | Pragma — pick during code review of Task 1.2. | Implementation detail |

### 1.9 Tests

| # | Decision | Rationale |
|---|---|---|
| 1.9.1 | New project `tests/InfraGate.AuditOutbox.Tests/` covering chain composition (hash matches expected), advisory-lock contention (parallel writes serialize, no chain break), canonicalization (deterministic bytes for equivalent rows), NULL-seed first row, sequence ordering. Uses Testcontainers Postgres fixture. | Engine has its own correctness surface independent of any wrapper. |
| 1.9.2 | Component wrapper tests in each existing test project: `tests/InfraGate.Approvals.Tests/` (or `.Postgres.Tests/`), `tests/InfraGate.Observer.Tests/`, `tests/InfraGate.Planner.Tests/`. Each asserts event-name constants, correlation extraction, payload shape. | Locality — wrapper tests live next to wrapper code. |
| 1.9.3 | Cross-stream forensic query test: insert rows in `approvals`, `observer`, `planner`; query `UNION ALL` joined on `plan_id` and `anomaly_id`; assert the forensic timeline reconstructs. | Proves the schema-per-component decision works for the read story. |
| 1.9.4 | Project-reference assertion tests in `tests/InfraGate.Observer.Tests/` and `tests/InfraGate.Planner.Tests/`: assert neither csproj references `InfraGate.Approvals` (only `InfraGate.AuditOutbox(.Postgres)`). | Architectural separation enforced at build time, per ADR-0015/ADR-0020 pattern. |
| 1.9.5 | No E2E test for audit in this roadmap — audit is observable through DB inspection, and component E2E suites already exist for the surrounding flows. | Avoid duplicating E2E surfaces. |

---

## 2. Glossary Delta

The following changes to `CONTEXT.md` were applied during grilling (Q2, Q4, Q5, final wrap-up):

- **New entry** `Audit Stream` (post-DRAFT, after Q wrap-up): the per-component, append-only audit record for one runtime component, written transactionally with the state mutation it describes; carries its own tamper-evident hash chain; correlated by IDs across components, not by shared chain.
- **New relationship lines** under the existing Relationships section:
  - An **Audit Stream** is owned by exactly one runtime component.
  - Three runtime components currently own an **Audit Stream**: the **Approval Authority**, the **Anomaly Observer**, and the **Remediation Planner**.
  - An **Audit Stream** carries its own tamper-evident hash chain over its own rows.
  - An **Audit Stream** is correlated to other **Audit Streams** by IDs, not by a shared hash chain.
  - An **Audit Stream** is written transactionally with the state mutation it describes, when one exists.
  - The **Approval Authority**'s **Audit Stream** is the persistent representation of the **Audit Trail**.
  - The **Anomaly Observer**'s **Audit Stream** does not extend the **Audit Spine** and does not produce **Audit Spine** events.
  - The **Remediation Planner**'s **Audit Stream** does not extend the **Audit Spine** and does not produce **Audit Spine** events.

No further glossary work is required during implementation unless new concepts surface during code review.

---

## 3. Out of Scope (v1)

Explicit non-goals so future readers don't re-litigate during implementation:

- **`AuditOutboxPublisher` hosted service.** No background polling, no OpenTelemetry sink, no `published_at_utc` stamping in v1. The columns exist; the publisher does not. Future ADR will specify retention, replay semantics, and sink choice.
- **Cross-stream hash chain.** Each stream chains its own rows. Cross-component correlation is by IDs.
- **Backfill of existing `approvals.audit_events` data.** Repo is experimental; greenfield retrofit. Devs drop their local schema.
- **Per-event-type hoisting of spine fields from old payloads.** Same reason. New rows write columns; there are no old rows in scope.
- **De-colliding the four `execution.blocked` event names** (Q7d). Kept as-is; `reason` column disambiguates.
- **Audit for components other than Approvals, Observer, Planner.** Gateway HTTP, McpServer stdio path, RunProfiles CLI — none acquire an Audit Stream in this roadmap. They remain on Serilog + metrics.
- **Audit for `cycle.started` / `cycle.completed` / `proposal.considered` / `propose_plan.called` and other operational events** (Q7b, Q7c). Audit is for forensic claims; operational events stay on ILogger and metrics.
- **Verifier CLI tool** (a script that walks an Audit Stream and validates the chain). The chain is verifiable from SQL; an ergonomic CLI is a follow-up.
- **Compaction / archival / retention policy** for `audit_outbox` rows. Rows accumulate; sizing and retention are deferred until external-sink work begins.
- **Schema versioning of the canonical encoding.** `ApprovalCanonicalJson` is treated as a stable canonicalization rule for v1. Any future schema change that affects canonical input must declare its impact (see ADR-0020 Consequences).
- **Migration tooling abstraction across components.** Either one generic runner or three per-component runners (decision deferred — see 1.8.5). Both work; abstraction is a follow-up.

---

## 4. Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Migration checksum guard rejects edited `0001-initial-approval-persistence.sql` on a developer machine that already applied the old version | Medium (every dev hits this once) | Document in the README of `InfraGate.AuditOutbox.Postgres` and in the migration's own header comment that the local `approvals` schema must be dropped before next startup. Optionally land a one-time helper script `scripts/reset-local-approvals-schema.sh`. |
| Advisory lock contention surfaces as latency, not as a metric | Low (loads are 2–3 orders of magnitude under the cap) | Per-stream `infragate.audit.lock.wait_ms` histogram via `Meter`; documented in `InfraGate.AuditOutbox` README. |
| Wrong overload picked on Approvals — `(entry, ct)` instead of `(entry, conn, tx, ct)` — would lose same-tx atomicity silently | Medium (correctness) | A single `WriteOutboxRowAsync` helper centralises the call site in `PostgresApprovalPersistence`; reviewers check that helper, not 8 sites. Unit test asserts the lock is acquired before the chain read. |
| `ApprovalCanonicalJson` evolves under us and breaks the chain | Medium (correctness) | The canonicalization is treated as stable contract; any change must be paired with an explicit ADR. A regression test pins canonical output for a fixed input row. |
| Observer/Planner schema drift — their migrations could diverge in column names while pretending to share shape | Low | A single shared constants class `AuditOutboxRowColumns` in `InfraGate.AuditOutbox` names every top-level column; per-stream migrations reference the same names. |
| Chain validation script not implemented yet, but the chain claims tamper-evidence | Low (claim still holds via SQL) | README of `InfraGate.AuditOutbox.Postgres` includes a copy-paste SQL recipe for chain verification; a CLI is a future follow-up. |
| Hoisted spine columns silently disagree with payload (e.g., `actor_subject` column says X, payload says Y) | Medium (audit trustworthiness) | Spine columns are populated **only** from the wrapper's entry record; payload **does not** carry them. Single source per field; no chance of disagreement. (1.3.4.) |
| ADR-0015's "Observer never writes through `IApprovalAuditPublisher`" property breaks accidentally if someone wires Observer to the Approvals project | Medium (architectural drift) | Project-reference assertion test in `tests/InfraGate.Observer.Tests/` and `tests/InfraGate.Planner.Tests/`. ADR-0020 explicitly carries this property forward. |
| `pg_advisory_xact_lock` collides with an unrelated caller in the same DB | Low | Use the two-argument form with a dedicated category constant (1.4.6). Document the category in the conventions class. |
| Lock auto-release semantics change between Postgres versions | Very Low | `pg_advisory_xact_lock` is stable since Postgres 9.1; documented in conventions class. |
| Wrapper overload misuse in Observer/Planner — they accidentally pass their own conn+tx instead of using the convenience overload | Low (no correctness issue — just verbose) | Wrappers' canonical example in the README shows the one-arg form. Convention check in code review. |

---

## 5. Task List

Phases are vertical-sliced. Each task is small enough for a focused session. Numbering is for reference, not implementation order — see §6 for execution order.

### Phase 1: Foundation — Audit Outbox Core

#### Task 1.1: Create `InfraGate.AuditOutbox` project (interfaces + row shape + conventions)

**Description:** New library project `src/InfraGate.AuditOutbox/InfraGate.AuditOutbox.csproj` (`net10.0`, inherits `Directory.Build.props`). Defines: `IAuditOutboxCore` (internal), `AuditOutboxRow` record (canonical row shape with all top-level columns), `AuditOutboxStream` enum or const-string set naming the three streams, `AuditOutboxConventions` static class (lock category, lock-key derivation function, column names, NULL-seed helper, chain-input canonicalization helper that delegates to `ApprovalCanonicalJson`). No public per-stream wrappers in this project — those live in component projects.

**Acceptance criteria:**

- [ ] Project builds clean; no `NoWarn` introduced.
- [ ] `IAuditOutboxCore.AppendAsync(stream, AuditOutboxRow, NpgsqlConnection, NpgsqlTransaction, CancellationToken)` matches §1.5.1.
- [ ] `AuditOutboxRow` record is positional, immutable, `sealed`, file-scoped namespace.
- [ ] `AuditOutboxConventions.LockCategory` is a `const int` with a unique sentinel value.
- [ ] `AuditOutboxConventions.StreamLockKey(string schemaName)` returns a stable `long` hash.
- [ ] `AuditOutboxConventions.ColumnNames` static class lists every top-level column as `const string`.
- [ ] One public-API snapshot test in `tests/InfraGate.AuditOutbox.Tests/` (skeleton project — implementation comes in Task 1.4).
- [ ] Project added to `InfraGate.slnx`.

**Verification:** `dotnet build src/InfraGate.AuditOutbox/InfraGate.AuditOutbox.csproj` clean.

**Dependencies:** None.

**Files likely touched:** `src/InfraGate.AuditOutbox/InfraGate.AuditOutbox.csproj`, `src/InfraGate.AuditOutbox/IAuditOutboxCore.cs`, `src/InfraGate.AuditOutbox/AuditOutboxRow.cs`, `src/InfraGate.AuditOutbox/AuditOutboxStream.cs`, `src/InfraGate.AuditOutbox/AuditOutboxConventions.cs`, `src/InfraGate.AuditOutbox/GlobalUsings.cs`, `InfraGate.slnx`.

**Estimated scope:** Medium.

---

#### Task 1.2: Create `InfraGate.AuditOutbox.Postgres` — `PostgresAuditOutboxCore`

**Description:** New library project `src/InfraGate.AuditOutbox.Postgres/InfraGate.AuditOutbox.Postgres.csproj` referencing `InfraGate.AuditOutbox`, `InfraGate.Approvals` (for `ApprovalCanonicalJson` only — internal-visible canonical helper), `Dapper`, `Npgsql`. Implements `PostgresAuditOutboxCore : IAuditOutboxCore`:

1. Acquires `pg_advisory_xact_lock(category, stream_key)`.
2. Reads the latest `event_hash` for the stream by schema-qualified table name + `ORDER BY audit_sequence DESC LIMIT 1`.
3. Computes `event_hash = sha256(prev_hash || ApprovalCanonicalJson.Serialize(canonical_row))` where `canonical_row` covers the columns listed in §1.4.1.
4. INSERTs the row into the per-stream table (table name derived from stream — `{schema}.audit_outbox`).
5. Returns the assigned `audit_sequence`.

Also includes a generic per-schema migration runner: `PostgresAuditOutboxMigrationRunner.ApplyAsync(NpgsqlDataSource, string schemaName, string migrationsDirectory, ct)` modelled directly on `PostgresApprovalMigrationRunner` (lock key derived from schema, idempotent checksum guard, schema-qualified `schema_migrations` table).

**Acceptance criteria:**

- [ ] `PostgresAuditOutboxCore` correctly acquires the advisory lock before reading prior hash.
- [ ] Canonical hash input matches §1.4.1 exactly (assertion against a pinned fixture row).
- [ ] First row of a stream has `previous_event_hash = NULL`; second row has `previous_event_hash` equal to first row's `event_hash`.
- [ ] Parallel writes from two connections to the same stream serialize via the advisory lock without chain break.
- [ ] Parallel writes to **different** streams do not contend (different lock keys).
- [ ] `PostgresAuditOutboxMigrationRunner` is fully exercised in test on a Testcontainers Postgres fixture (apply, idempotent re-apply, checksum mismatch rejection).
- [ ] No DI registrations leak — registration is opt-in via a `ServiceCollectionExtensions.AddPostgresAuditOutbox(this IServiceCollection, NpgsqlDataSource)` extension method.

**Verification:** `dotnet test tests/InfraGate.AuditOutbox.Tests/` (created in Task 1.4) green.

**Dependencies:** Task 1.1.

**Files likely touched:** `src/InfraGate.AuditOutbox.Postgres/InfraGate.AuditOutbox.Postgres.csproj`, `src/InfraGate.AuditOutbox.Postgres/PostgresAuditOutboxCore.cs`, `src/InfraGate.AuditOutbox.Postgres/PostgresAuditOutboxMigrationRunner.cs`, `src/InfraGate.AuditOutbox.Postgres/PostgresAuditOutboxConventions.cs`, `src/InfraGate.AuditOutbox.Postgres/ServiceCollectionExtensions.cs`, `src/InfraGate.AuditOutbox.Postgres/GlobalUsings.cs`, `InfraGate.slnx`.

**Estimated scope:** Large.

---

#### Task 1.3: Internal-visible `ApprovalCanonicalJson` for the audit core

**Description:** `ApprovalCanonicalJson` currently lives in `InfraGate.Approvals` and is internal/public depending on assembly. The audit core needs to call it. The two clean paths are: (a) make it `public` in `InfraGate.Approvals` (acceptable — it's already a stable contract, used by digest computation), or (b) `[InternalsVisibleTo("InfraGate.AuditOutbox.Postgres")]`. **Pick (a)** — `ApprovalCanonicalJson` is conceptually a stable, declared canonicalization rule (see CONTEXT.md "Canonicalization"); making it public formalises the contract.

**Acceptance criteria:**

- [ ] `ApprovalCanonicalJson.Serialize(object)` and `ApprovalCanonicalJson.ComputeSha256Hex(string)` are `public`.
- [ ] No behavior change in existing callers.
- [ ] Public-API snapshot test in `tests/InfraGate.Approvals.Tests/` (if not already covering this type) updated.

**Verification:** `dotnet build InfraGate.slnx` clean; `dotnet test tests/InfraGate.Approvals.Tests/` green.

**Dependencies:** None (can land before or in parallel with Task 1.1).

**Files likely touched:** `src/InfraGate.Approvals/ApprovalCanonicalJson.cs`.

**Estimated scope:** Small.

---

#### Task 1.4: Create `InfraGate.AuditOutbox.Tests` — chain, lock, canonicalization

**Description:** New test project covering the core in isolation:

- Chain composition: insert N rows in sequence; verify `event_hash[i] == sha256(event_hash[i-1] || canonical(row[i]))` for all i; assert `previous_event_hash[0] IS NULL`.
- Lock contention: two parallel `AppendAsync` calls from two connections to the same stream produce two consecutive rows with intact chain; lock key for stream A vs stream B does not contend.
- Canonicalization stability: equivalent rows (same column values) produce identical canonical bytes; differing rows produce different bytes.
- Migration runner: applies on empty DB, idempotent on re-apply, rejects checksum drift.
- Two-arg advisory lock category and key derivation matches `AuditOutboxConventions`.

**Acceptance criteria:**

- [ ] All five test classes above present, each with at least one `[Fact]` per behaviour.
- [ ] Uses Testcontainers Postgres fixture pattern matching `tests/InfraGate.Approvals.Postgres.Tests/`.
- [ ] No flaky tests; deterministic seed for random row values.

**Verification:** `dotnet test tests/InfraGate.AuditOutbox.Tests/` green locally and in CI.

**Dependencies:** Tasks 1.1, 1.2.

**Files likely touched:** `tests/InfraGate.AuditOutbox.Tests/InfraGate.AuditOutbox.Tests.csproj`, plus the five test classes.

**Estimated scope:** Large.

---

#### Checkpoint: Foundation Complete

- [ ] All Phase 1 tasks merged.
- [ ] `dotnet build InfraGate.slnx` clean; `dotnet test InfraGate.slnx` green.
- [ ] Audit core engine works in isolation with no callers yet. Chain, lock, canonicalization, migration all proven via unit tests.
- [ ] CONTEXT.md changes (Audit Stream term + relationships) already applied during grilling.
- [ ] ADR-0020 in place.

---

### Phase 2: Approvals Retrofit

#### Task 2.1: Edit `Migrations/0001-initial-approval-persistence.sql` in place

**Description:** Modify the existing migration file:

- Remove the `create table approvals.audit_events (...)` block and its `ix_audit_events_plan_sequence` index.
- Add the new `approvals.audit_outbox` table with full new shape (spine columns, chain columns, outbox-state columns, per-stream correlation columns).
- Add indexes: `ix_audit_outbox_plan_sequence (plan_id, audit_sequence)`, `ix_audit_outbox_unpublished (published_at_utc) where published_at_utc is null` (dormant publisher index, useful from day one for "how many unpublished").
- Add `created_at_utc default now()` on `schema_migrations` already exists; no change.

Header comment in the SQL file documents that this migration was modified in place during the audit-outbox retrofit (ADR-0020) and that existing dev environments must drop the `approvals` schema before next startup.

**Acceptance criteria:**

- [ ] `audit_events` no longer exists in the migration; `audit_outbox` exists with §1.3 + §1.4 column set.
- [ ] Indexes match the spec.
- [ ] Header comment present and explicit about the drop-schema requirement.
- [ ] Migration applies cleanly on an empty DB and a freshly-dropped `approvals` schema.

**Verification:** `dotnet test tests/InfraGate.Approvals.Postgres.Tests/` green after Task 2.2 lands; manual: drop local `approvals` schema, start the gateway, verify migration applies.

**Dependencies:** None (schema-only change; Task 2.2 will use the new shape).

**Files likely touched:** `src/InfraGate.Approvals.Postgres/Migrations/0001-initial-approval-persistence.sql`.

**Estimated scope:** Small.

---

#### Task 2.2: `ApprovalAuditOutbox` wrapper + `ApprovalAuditEntry` record

**Description:** New types in `InfraGate.Approvals.Postgres`:

- `ApprovalAuditEntry` record — typed correlation fields `PlanId`, `ChallengeId?`, `GrantId?`, `ExecutionAttemptId?`; spine fields `ActorSubject?`, `ActorClientId?`, `Outcome?`, `Reason?`; payload `object Payload`; `string EventName`.
- `IApprovalAuditOutbox` interface — two `AppendAsync` overloads per §1.5.2.
- `ApprovalAuditOutbox : IApprovalAuditOutbox` — translates `ApprovalAuditEntry` into `AuditOutboxRow`, calls `IAuditOutboxCore.AppendAsync` with the `approvals` stream name.

DI registration extension: `services.AddApprovalAuditOutbox()` (depends on `services.AddPostgresAuditOutbox(...)` from Task 1.2).

**Acceptance criteria:**

- [ ] `ApprovalAuditEntry` is positional, sealed, file-scoped namespace, per `code-standards`.
- [ ] Both overloads of `AppendAsync` work; the two-arg form opens conn+tx, commits.
- [ ] Wrapper extracts correlation fields into the `AuditOutboxRow` correctly.
- [ ] Payload is serialised via `ApprovalCanonicalJson` consistently with existing payload encoding.
- [ ] Unit tests in `tests/InfraGate.Approvals.Postgres.Tests/`: extraction correctness, both overloads exercise the core, event names from `ApprovalConventions.AuditEvents` accepted.

**Verification:** `dotnet test tests/InfraGate.Approvals.Postgres.Tests/`.

**Dependencies:** Tasks 1.1, 1.2, 1.3, 2.1.

**Files likely touched:** `src/InfraGate.Approvals.Postgres/ApprovalAuditEntry.cs`, `src/InfraGate.Approvals.Postgres/IApprovalAuditOutbox.cs`, `src/InfraGate.Approvals.Postgres/ApprovalAuditOutbox.cs`, `src/InfraGate.Approvals.Postgres/PostgresApprovalPersistenceServiceCollectionExtensions.cs` (register `AddApprovalAuditOutbox`), `tests/InfraGate.Approvals.Postgres.Tests/ApprovalAuditOutboxTests.cs`.

**Estimated scope:** Medium.

---

#### Task 2.3: Migrate the 8 `InsertAuditAsync` call sites in `PostgresApprovalPersistence` to `IApprovalAuditOutbox`

**Description:** Replace every direct call to the private `InsertAuditAsync(connection, transaction, eventName, payload, ct)` in `PostgresApprovalPersistence.cs` (lines 111, 353, 520, 558, 621, 633, 861, 915 as of the current `main`) with `await this.approvalAuditOutbox.AppendAsync(entry, connection, transaction, ct)`. Each call site builds an `ApprovalAuditEntry` with the correct typed correlation IDs and spine fields extracted from the surrounding context (e.g., plan creation → `PlanId = envelope.Id`, `ActorSubject = envelope.Requester.Subject`, `Outcome = null`, `Reason = null`).

Delete the private `InsertAuditAsync` static method. Delete the `AuditCorrelation.FromPayload(payload)` helper if no longer used elsewhere.

**Acceptance criteria:**

- [ ] All 8 call sites converted; no remaining direct INSERT into `approvals.audit_outbox` in `PostgresApprovalPersistence`.
- [ ] `PostgresApprovalPersistence` constructor takes `IApprovalAuditOutbox`.
- [ ] Spine columns (`actor_subject`, `actor_client_id`, `outcome`, `reason`) populated from each call site's natural context — not extracted from payload.
- [ ] Existing `tests/InfraGate.Approvals.Postgres.Tests/` suite passes unchanged where possible; tests that previously assert against `audit_events` rows are updated to assert against `audit_outbox` rows with the same row counts and event names.
- [ ] No regression in audit row counts per scenario.

**Verification:** `dotnet test tests/InfraGate.Approvals.Postgres.Tests/`, `dotnet test tests/InfraGate.McpGateway.Tests/`.

**Dependencies:** Task 2.2.

**Files likely touched:** `src/InfraGate.Approvals.Postgres/PostgresApprovalPersistence.cs` (8 sites + ctor + helper deletions), `tests/InfraGate.Approvals.Postgres.Tests/*` (assertion column-name updates).

**Estimated scope:** Large.

---

#### Task 2.4: Migrate `ApprovalPreExecutionGate` to `IApprovalAuditOutbox`

**Description:** Replace the constructor parameter `IApprovalAuditPublisher? auditPublisher = null` (and the `NoOpApprovalAuditPublisher.Instance` fallback) with `IApprovalAuditOutbox approvalAuditOutbox`. The `PreExecutionGrantValidated` event emission uses the new typed entry instead of the old `PlanAudit(eventName, payload)` wrapper.

**Acceptance criteria:**

- [ ] `ApprovalPreExecutionGate` constructor takes `IApprovalAuditOutbox` (non-optional — registration is now unconditional).
- [ ] `PreExecutionGrantValidated` event row appears in `approvals.audit_outbox` after a passing gate evaluation.
- [ ] Unit tests in `tests/InfraGate.Approvals.Tests/` updated; tests previously using `NoOpApprovalAuditPublisher` now use a Testcontainers-backed outbox.

**Verification:** `dotnet test tests/InfraGate.Approvals.Tests/`.

**Dependencies:** Task 2.3.

**Files likely touched:** `src/InfraGate.Approvals/PreExecution/ApprovalPreExecutionGate.cs`, `tests/InfraGate.Approvals.Tests/PreExecution/ApprovalPreExecutionGateTests.cs`.

**Estimated scope:** Small.

---

#### Task 2.5: Delete `IApprovalAuditPublisher`, `NoOpApprovalAuditPublisher`, `PlanAudit`

**Description:** All callers migrated as of Task 2.4. Delete the three types and their files. Remove any DI registrations and test fakes.

**Acceptance criteria:**

- [ ] `src/InfraGate.Approvals/Audit/IApprovalAuditPublisher.cs` deleted.
- [ ] `src/InfraGate.Approvals/Audit/NoOpApprovalAuditPublisher.cs` deleted.
- [ ] `src/InfraGate.Approvals/Audit/PlanAudit.cs` deleted.
- [ ] No build or test references remain.
- [ ] Public-API snapshot tests updated.

**Verification:** `dotnet build InfraGate.slnx` clean; `dotnet test InfraGate.slnx` green.

**Dependencies:** Task 2.4.

**Files likely touched:** Three file deletions in `src/InfraGate.Approvals/Audit/`; possible cleanup in DI registration in `InfraGate.McpGateway` and `InfraGate.McpServer`.

**Estimated scope:** Small.

---

#### Checkpoint: Approvals Retrofit Complete

- [ ] All Phase 2 tasks merged.
- [ ] `dotnet test InfraGate.slnx` green.
- [ ] Manual: bring up local stack; perform a `request_plan` → approve → `execute_approved_plan` flow. Verify `select event_name, actor_subject, outcome, reason, event_hash from approvals.audit_outbox order by audit_sequence` returns the full lifecycle with intact chain.
- [ ] The Audit Spine still passes the existing `tests/InfraGate.Safety.E2E.Tests/` (opt-in) assertions.

---

### Phase 3: Observer Audit Stream

#### Task 3.1: Observer schema migration

**Description:** New migrations directory under the Observer (or under `InfraGate.AuditOutbox.Postgres` — see 1.8.5; preferred path is **per-component** ownership of migrations, mirroring Approvals): `src/InfraGate.Observer/Migrations/0001-initial-observer-audit.sql` creating:

- Schema `observer`
- `observer.schema_migrations (filename, checksum_sha256, applied_at_utc default now())`
- `observer.audit_outbox` with full row shape (spine columns + chain + outbox-state + correlation columns `cycle_id`, `anomaly_id`, `dedupe_key`)
- Indexes: `ix_observer_audit_outbox_anomaly (anomaly_id, audit_sequence)`, `ix_observer_audit_outbox_cycle (cycle_id, audit_sequence)`, `ix_observer_audit_outbox_unpublished (published_at_utc) where published_at_utc is null`

The migration file is embedded as a `<Content>` item in the csproj so it ships next to the Observer's binary, mirroring the existing `InfraGate.Approvals.Postgres` pattern.

**Acceptance criteria:**

- [ ] Migration file present and applies cleanly on an empty DB.
- [ ] Schema name, table name, columns, indexes match §1.3 and §1.4.
- [ ] Embedded as build output.

**Verification:** `dotnet test tests/InfraGate.Observer.Tests/` after Task 3.2 (which exercises the runner) green.

**Dependencies:** Tasks 1.1, 1.2 (the generic migration runner from `InfraGate.AuditOutbox.Postgres`).

**Files likely touched:** `src/InfraGate.Observer/Migrations/0001-initial-observer-audit.sql`, `src/InfraGate.Observer/InfraGate.Observer.csproj`.

**Estimated scope:** Small.

---

#### Task 3.2: `ObserverAuditOutbox` wrapper + `ObserverAuditEntry` + `ObserverAuditEvents`

**Description:** New types in `InfraGate.Observer`:

- `ObserverAuditEntry` record — typed correlation fields `CycleId?`, `AnomalyId?`, `DedupeKey?`; spine fields `ActorSubject? = "service:observer"` default, `ActorClientId?`, `Outcome?`, `Reason?`; payload `object Payload`; `string EventName`.
- `IObserverAuditOutbox` interface — two `AppendAsync` overloads.
- `ObserverAuditOutbox : IObserverAuditOutbox`.
- `ObserverAuditEvents` static class with 5 `const string`s: `AnomalyDetected = "anomaly.detected"`, `AnomalySuppressed = "anomaly.suppressed"`, `AnomalyResolved = "anomaly.resolved"`, `HandoffPublished = "handoff.published"`, `HandoffFailed = "handoff.failed"`.

DI extension: `services.AddObserverAuditOutbox()` chains on `services.AddPostgresAuditOutbox(...)`.

**Acceptance criteria:**

- [ ] All five const event names defined and used as the only source for `EventName` in the call sites (Task 3.4).
- [ ] Wrapper extracts correlation fields correctly into `AuditOutboxRow`.
- [ ] Unit tests in `tests/InfraGate.Observer.Tests/`: event name constants, correlation extraction, both overloads.

**Verification:** `dotnet test tests/InfraGate.Observer.Tests/`.

**Dependencies:** Tasks 1.1, 1.2, 3.1.

**Files likely touched:** `src/InfraGate.Observer/Audit/IObserverAuditOutbox.cs`, `src/InfraGate.Observer/Audit/ObserverAuditOutbox.cs`, `src/InfraGate.Observer/Audit/ObserverAuditEntry.cs`, `src/InfraGate.Observer/Audit/ObserverAuditEvents.cs`, `src/InfraGate.Observer/Audit/ServiceCollectionExtensions.cs`.

**Estimated scope:** Medium.

---

#### Task 3.3: Wire Observer to Postgres (connection string, migration runner, DI)

**Description:** Observer gains a Postgres connection string config key `INFRA_GATE_OBSERVER_AUDIT_CONNECTION_STRING` (env var + `appsettings`). At startup, `Program.cs` calls `PostgresAuditOutboxMigrationRunner.ApplyAsync(dataSource, "observer", Path.Combine(AppContext.BaseDirectory, "Migrations"), cancellationToken)` before the host runs. `services.AddObserverAuditOutbox()` registers the wrapper.

Update `ObserverConventions.ConfigurationKeys` and `ObserverConventions.EnvironmentVariables` per the existing pattern. Update `ObserverProfile` in `InfraGate.RunProfiles` to include the new env var.

**Acceptance criteria:**

- [ ] Observer starts cleanly with a valid connection string; applies the migration on first run.
- [ ] Observer fails fast at startup if the connection string is missing.
- [ ] DI graph has `IObserverAuditOutbox` resolvable.
- [ ] `ObserverConventions` updated; `ObserverProfile` updated.
- [ ] Integration test: Observer starts, the migration is applied, the wrapper can write a test row.

**Verification:** Manual: start Observer locally, observe migration log line, query `observer.schema_migrations` and `observer.audit_outbox`.

**Dependencies:** Tasks 3.1, 3.2.

**Files likely touched:** `src/InfraGate.Observer/Program.cs`, `src/InfraGate.Observer/ObserverConventions.cs`, `src/InfraGate.Observer/ObserverOptions.cs`, `src/InfraGate.RunProfiles/ObserverProfile.cs`, `deploy/local-oauth/compose.yaml` (Observer service env vars).

**Estimated scope:** Medium.

---

#### Task 3.4: Emit the 5 Observer audit events at the correct pipeline points

**Description:** Instrument the Observer pipeline:

- `anomaly.detected` — emitted from the cycle's detection code path **after** the suppression-window check (i.e., when an anomaly is going to be reported, not when it's first observed in the snapshot). Correlation: `cycle_id`, `anomaly_id`, `dedupe_key`. Payload: severity, target ref (kind/namespace/name), evidence digest, detection rule names.
- `anomaly.suppressed` — emitted in the same cycle code path when an anomaly is observed but suppressed by the **Suppression Window**. Correlation: `cycle_id`, `dedupe_key`. Payload: anomaly_id, first-seen cycle, suppressed-in cycle.
- `anomaly.resolved` — emitted by the **Resolution Emission** path in `DedupeStateService` (or wherever the resolution check fires). Correlation: `cycle_id`, `anomaly_id`, `dedupe_key`. Payload: original severity, cycles-since-last-seen.
- `handoff.published` — emitted from `HttpAnomalyHandoffSink.PublishAsync` after a successful POST. Correlation: `cycle_id`. Payload: batch size, anomaly_ids, sink type ("http").
- `handoff.failed` — emitted from `HttpAnomalyHandoffSink.PublishAsync` on the failure paths (non-2xx, exception). Correlation: `cycle_id`. Payload: attempt count (always 1 in v1 — Observer is fire-and-forget), error class, status code.

Each call uses `ObserverAuditEvents.X` (no string literals), the convenience overload `AppendAsync(entry, ct)` (Observer has no surrounding tx).

**Acceptance criteria:**

- [ ] All five event names appear in the `observer.audit_outbox` table after exercising the Observer's cycle flow against a fixture cluster snapshot.
- [ ] Event order matches the dedupe lifecycle (`anomaly.detected` for a new anomaly, then `anomaly.suppressed` for cycles 2–N, then `anomaly.resolved` after resolution threshold).
- [ ] Cross-stream join: `select * from observer.audit_outbox o where o.anomaly_id in (select anomaly_id from planner.audit_outbox)` returns the expected joined timeline (when Planner is in scope — Phase 4).
- [ ] Existing Observer unit tests pass (no Serilog/metrics regression).
- [ ] New integration test in `tests/InfraGate.Observer.Tests/` exercises the full event sequence against the Testcontainers Postgres.

**Verification:** `dotnet test tests/InfraGate.Observer.Tests/`.

**Dependencies:** Task 3.3.

**Files likely touched:** `src/InfraGate.Observer/Cycle/AnomalyCycleHost.cs` (or equivalent), `src/InfraGate.Observer/Classification/AnomalyClassifier.cs`, `src/InfraGate.Observer/State/DedupeStateService.cs`, `src/InfraGate.Observer/Handoff/HttpAnomalyHandoffSink.cs`, `tests/InfraGate.Observer.Tests/`.

**Estimated scope:** Large.

---

#### Checkpoint: Observer Audit Stream Live

- [ ] All Phase 3 tasks merged.
- [ ] Manual: run a cycle against `examples/failing-deployment/`; query `observer.audit_outbox` and see `anomaly.detected` + `handoff.published` for the demo anomaly.
- [ ] Chain intact: each row's `previous_event_hash` equals the prior row's `event_hash`; row 1 has NULL.
- [ ] Project-reference assertion test (Task 5.1, can land in this phase) green.

---

### Phase 4: Planner Audit Stream

#### Task 4.1: Planner schema migration

**Description:** New `src/InfraGate.Planner/Migrations/0001-initial-planner-audit.sql`:

- Schema `planner`
- `planner.schema_migrations`
- `planner.audit_outbox` with row shape + correlation columns `proposal_id`, `anomaly_id`, `plan_id`
- Indexes: `ix_planner_audit_outbox_anomaly (anomaly_id, audit_sequence)`, `ix_planner_audit_outbox_plan (plan_id, audit_sequence)`, `ix_planner_audit_outbox_unpublished (published_at_utc) where published_at_utc is null`

**Acceptance criteria:**

- [ ] Migration file present, embedded as `<Content>`, applies cleanly.
- [ ] Schema, table, columns, indexes match §1.3 + §1.4.

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/` (after Task 4.2).

**Dependencies:** Tasks 1.1, 1.2.

**Files likely touched:** `src/InfraGate.Planner/Migrations/0001-initial-planner-audit.sql`, `src/InfraGate.Planner/InfraGate.Planner.csproj`.

**Estimated scope:** Small.

---

#### Task 4.2: `PlannerAuditOutbox` wrapper + `PlannerAuditEntry` + `PlannerAuditEvents`

**Description:** New types in `InfraGate.Planner`:

- `PlannerAuditEntry` — typed correlation `ProposalId?`, `AnomalyId?`, `PlanId?`; spine fields `ActorSubject? = "service:planner"` default; payload; event name.
- `IPlannerAuditOutbox` + `PlannerAuditOutbox` per the standard wrapper pattern.
- `PlannerAuditEvents` static class with 4 `const string`s: `HandoffReceived = "handoff.received"`, `ProposalSkipped = "proposal.skipped"`, `ProposePlanSucceeded = "propose_plan.succeeded"`, `ProposePlanFailed = "propose_plan.failed"`.

DI extension: `services.AddPlannerAuditOutbox()`.

**Acceptance criteria:**

- [ ] All four const event names defined and used.
- [ ] Wrapper extracts correlation fields correctly.
- [ ] Unit tests in `tests/InfraGate.Planner.Tests/`.

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/`.

**Dependencies:** Tasks 1.1, 1.2, 4.1.

**Files likely touched:** `src/InfraGate.Planner/Audit/IPlannerAuditOutbox.cs`, `src/InfraGate.Planner/Audit/PlannerAuditOutbox.cs`, `src/InfraGate.Planner/Audit/PlannerAuditEntry.cs`, `src/InfraGate.Planner/Audit/PlannerAuditEvents.cs`, `src/InfraGate.Planner/Audit/ServiceCollectionExtensions.cs`.

**Estimated scope:** Medium.

---

#### Task 4.3: Wire Planner to Postgres

**Description:** Mirror Task 3.3 for the Planner. New env var `INFRA_GATE_PLANNER_AUDIT_CONNECTION_STRING`. Startup migration runner. DI wiring. `PlannerConventions` and `PlannerProfile` updates. Compose service env var update.

**Acceptance criteria:**

- [ ] Planner starts cleanly with valid connection string; migration applies.
- [ ] DI graph has `IPlannerAuditOutbox` resolvable.
- [ ] Conventions, profile, compose updated.

**Verification:** Manual: start Planner locally, observe migration log, query `planner.schema_migrations`.

**Dependencies:** Tasks 4.1, 4.2.

**Files likely touched:** `src/InfraGate.Planner/Program.cs`, `src/InfraGate.Planner/PlannerConventions.cs`, `src/InfraGate.Planner/PlannerOptions.cs`, `src/InfraGate.RunProfiles/PlannerProfile.cs`, `deploy/local-oauth/compose.yaml`.

**Estimated scope:** Medium.

---

#### Task 4.4: Emit the 4 Planner audit events at the correct pipeline points

**Description:** Instrument the Planner pipeline:

- `handoff.received` — emitted from the `POST /handoff/anomalies` endpoint **after** auth validation and **before** the channel write. One row per batch. Correlation: none per-anomaly (this is a batch-scoped event). Payload: `cycle_id` from batch, `anomaly_ids` array, `count`.
- `proposal.skipped` — emitted from the per-anomaly decision path when the Planner decides not to propose (unsupported operation, no candidate, LLM declined, dedupe hit). Correlation: `anomaly_id`. Payload: reason code, free-text reason, LLM response excerpt if applicable.
- `propose_plan.succeeded` — emitted from the per-anomaly path after a successful `propose_plan` gateway call. Correlation: `anomaly_id`, `plan_id`, `proposal_id` (minted by the wrapper if not present). Payload: operation type, summarised arguments.
- `propose_plan.failed` — emitted on gateway rejection or HTTP failure. Correlation: `anomaly_id`. Payload: reason code, error class, status code (if HTTP), gateway error body excerpt.

Each call uses `PlannerAuditEvents.X` constants; convenience overload.

**Acceptance criteria:**

- [ ] All four event names appear in `planner.audit_outbox` after exercising the full Observer→Planner flow.
- [ ] Cross-stream join: for a successfully proposed plan, the timeline is observable as `observer.anomaly.detected → planner.handoff.received → planner.propose_plan.succeeded → approvals.plan.created → approvals.challenge.created` joined by `anomaly_id` then `plan_id`.
- [ ] Existing Planner tests pass.
- [ ] New integration test exercises the full event sequence.

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/`.

**Dependencies:** Task 4.3.

**Files likely touched:** `src/InfraGate.Planner/Endpoints/HandoffEndpoint.cs`, `src/InfraGate.Planner/Cycle/AnomalyBatchProcessor.cs` (or equivalent), `src/InfraGate.Planner/Decision/*`, `src/InfraGate.Planner/Mcp/PlannerMcpClient.cs`, `tests/InfraGate.Planner.Tests/`.

**Estimated scope:** Large.

---

#### Checkpoint: Planner Audit Stream Live

- [ ] All Phase 4 tasks merged.
- [ ] Manual: run Observer→Planner end-to-end against the demo deployment; query all three `audit_outbox` tables; reconstruct the full timeline.
- [ ] Chain intact in `planner.audit_outbox`.

---

### Phase 5: Verification + Documentation

#### Task 5.1: Project-reference assertion tests

**Description:** Add a unit test in `tests/InfraGate.Observer.Tests/` and `tests/InfraGate.Planner.Tests/` that loads the component's csproj XML, parses `<ProjectReference>` entries, and asserts:

- `InfraGate.AuditOutbox` is referenced (positive).
- `InfraGate.AuditOutbox.Postgres` is referenced (positive).
- `InfraGate.Approvals` is **not** referenced (negative — ADR-0015 / ADR-0020 constraint).
- `InfraGate.Approvals.Postgres` is **not** referenced (negative).

Mirrors the existing ADR-0015 enforcement style.

**Acceptance criteria:**

- [ ] Both tests present and passing.
- [ ] Tests fail clearly if a future change adds a forbidden reference.

**Verification:** `dotnet test tests/InfraGate.Observer.Tests/ProjectReferenceAssertionsTests.cs` etc.

**Dependencies:** Phase 3, Phase 4 complete.

**Files likely touched:** `tests/InfraGate.Observer.Tests/Architecture/ProjectReferenceAssertionsTests.cs`, `tests/InfraGate.Planner.Tests/Architecture/ProjectReferenceAssertionsTests.cs`.

**Estimated scope:** Small.

---

#### Task 5.2: Cross-stream forensic query test

**Description:** New integration test in `tests/InfraGate.AuditOutbox.Tests/` (or a new top-level cross-component test project — open call): seed rows into all three streams via the wrappers, execute the canonical forensic query (`UNION ALL` across `approvals.audit_outbox`, `observer.audit_outbox`, `planner.audit_outbox` joined on `anomaly_id` / `plan_id`), assert the timeline reconstructs in the expected order.

**Acceptance criteria:**

- [ ] Test demonstrates the cross-stream query works.
- [ ] Documents the canonical forensic-query shape (in test comments + the project README).

**Verification:** `dotnet test tests/InfraGate.AuditOutbox.Tests/CrossStreamForensicTests.cs`.

**Dependencies:** Phase 2, Phase 3, Phase 4 complete.

**Files likely touched:** `tests/InfraGate.AuditOutbox.Tests/CrossStreamForensicTests.cs`.

**Estimated scope:** Medium.

---

#### Task 5.3: SQL chain-verification recipe in `InfraGate.AuditOutbox.Postgres/README.md`

**Description:** Document a copy-paste SQL recipe that walks an outbox table, recomputes `event_hash` from `previous_event_hash || canonical(row)`, and reports rows whose stored hash disagrees with the recomputed value. Include a CLI-launch note for the future verifier tool.

**Acceptance criteria:**

- [ ] README section "Verifying the chain" present.
- [ ] SQL snippet executable on a populated `approvals.audit_outbox`.
- [ ] Links to ADR-0020 and CONTEXT.md `Audit Stream`.

**Verification:** Manual: run the SQL against the demo data, observe zero discrepancies.

**Dependencies:** Phase 2 complete.

**Files likely touched:** `src/InfraGate.AuditOutbox.Postgres/README.md` (new file).

**Estimated scope:** Small.

---

#### Task 5.4: README + docs updates

**Description:** Create or update:

- `src/InfraGate.AuditOutbox/README.md` — purpose, deep core interface, where wrappers live, glossary pointers, ADR pointer.
- `src/InfraGate.AuditOutbox.Postgres/README.md` — Postgres-specific details (advisory lock, canonicalization, migration runner, chain-verification recipe from Task 5.3).
- `src/InfraGate.Approvals.Postgres/README.md` — section on `ApprovalAuditOutbox` replacing the old `IApprovalAuditPublisher`.
- `src/InfraGate.Observer/README.md` — section on Observer Audit Stream (link to ADR-0015 + ADR-0020 + Audit Stream glossary).
- `src/InfraGate.Planner/README.md` — section on Planner Audit Stream.
- `docs/devs-readme.md` — update local-run instructions to include the three Postgres connection strings and a note on dropping the `approvals` schema after pulling Phase 2.
- `AGENTS.md` — `Solution Map` extended with the new audit-outbox project pair.
- `README.md` (root) — one-line note in the project-map section.

Run the `verify-readme-docs` skill discipline against each changed file.

**Acceptance criteria:**

- [ ] All listed READMEs created or updated.
- [ ] `verify-readme-docs` pass on each (no broken refs, no out-of-date code references).
- [ ] Cross-links between READMEs, ADR-0020, ADR-0015, and CONTEXT.md `Audit Stream` consistent.

**Verification:** Manual read-through; `verify-readme-docs` skill applied per file.

**Dependencies:** All prior phases.

**Files likely touched:** All listed READMEs above.

**Estimated scope:** Medium.

---

#### Checkpoint: Audit Outbox Complete

- [ ] All Phase 5 tasks merged.
- [ ] `dotnet test InfraGate.slnx` green.
- [ ] Manual cross-stream forensic query reconstructs full Observer→Planner→Approve→Execute timeline.
- [ ] ADR-0020 referenced from each new README.
- [ ] CONTEXT.md `Audit Stream` term is the source of truth; no `DRAFT` markers remain.
- [ ] Roadmap can be moved to `.agents/Plans/Roadmap/Archive/` once the deferred publisher work has its own roadmap.

---

## 6. Execution Order

Recommended sequencing — each step gates the next at a checkpoint.

1. **Tasks 1.3 → 1.1 → 1.2 → 1.4** (Phase 1 — Foundation). Land Task 1.3 first because it's a tiny visibility tweak; then the engine and its tests.
2. **Checkpoint: Foundation Complete.**
3. **Tasks 2.1 → 2.2 → 2.3 → 2.4 → 2.5** (Phase 2 — Approvals Retrofit). Strictly sequential; each task depends on the previous.
4. **Checkpoint: Approvals Retrofit Complete.** Verify the existing Approvals + Gateway test suites still pass.
5. **Tasks 3.1 → 3.2 → 3.3 → 3.4** (Phase 3 — Observer). 3.1 + 3.2 can land in either order; 3.3 needs both; 3.4 needs 3.3.
6. **Checkpoint: Observer Audit Stream Live.**
7. **Tasks 4.1 → 4.2 → 4.3 → 4.4** (Phase 4 — Planner). Same shape as Phase 3.
8. **Checkpoint: Planner Audit Stream Live.**
9. **Tasks 5.1 → 5.2 → 5.3 → 5.4** (Phase 5 — Verification + Docs). 5.1 can land at the end of Phase 3/4 if convenient.
10. **Checkpoint: Audit Outbox Complete.**

Phases 3 and 4 are independent — they can run in parallel after Phase 2 lands.

---

## 7. Open Questions

These are deliberately deferred. Each will be answered during implementation review or in a follow-up roadmap.

- **Migration runner abstraction**: extract a single `PostgresAuditOutboxMigrationRunner` parametrised by schema, or keep per-component runners? Resolve during Task 1.2 code review.
- **Stream lock key derivation**: use schema name's `GetHashCode()` is not stable across runtimes — pick a stable `SHA256(schemaName)[0..8] as long` instead. Confirm during Task 1.2.
- **Indexes on adapter-payload JSON paths**: do we need any from day one (e.g., a GIN index for Kubernetes `namespace`), or wait for a concrete query need? Default: wait. Add when a query proves it necessary.
- **Hash algorithm versioning**: SHA-256 is fine for v1. Future versioning is a follow-up ADR (mirror **Plan Envelope** digest's `Algorithm` field on the row?).
- **Future publisher's failure mode** (Stage 2/3 from the dirty-notes): not in scope here. Captured for the follow-up ADR.

---

## 8. Reference

- ADR-0020 (this roadmap's source of truth): `docs/adr/0020-audit-outbox-architecture.md`
- ADR-0015 (related — Observer excluded from Audit Spine; preserved): `docs/adr/0015-anomaly-observer-excluded-from-audit-spine.md`
- ADR-0010 (Postgres for generic approval persistence — sibling pattern): `docs/adr/0010-use-postgresql-for-generic-approval-persistence.md`
- CONTEXT.md `Audit Stream` term and relationships
- Dirty notes that seeded the grilling: `.human/dirty-notes/audit-architecture-plan.md`
- `planner-executor-roadmap.md` (structural template): `.agents/Plans/Roadmap/planner-executor-roadmap.md`
