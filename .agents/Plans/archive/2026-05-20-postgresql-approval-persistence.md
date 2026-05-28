# Implementation Plan: PostgreSQL Generic Approval Persistence

## Overview

Replace file-backed Generic Approval Core persistence with PostgreSQL while preserving existing ownership boundaries. Approval state and approval audit move to PostgreSQL under `InfraGate.Approvals` abstractions and a new `InfraGate.Approvals.Postgres` adapter. Guardrail audit, Serilog logs, and ASP.NET Data Protection stay out of this slice.

This plan follows ADR-0010 and the persistence grilling decisions from May 20, 2026.

## Decision Inventory

These decisions are part of the plan and should not be re-litigated during implementation unless code proves one impossible.

- Scope is the Generic Approval Core persistence slice only.
- Approval state and approval audit live under `InfraGate.Approvals` ownership.
- Runtime approval persistence is PostgreSQL only. Do not keep a supported file-backed approval adapter.
- Do not dual-write to files and PostgreSQL.
- Do not migrate existing pending file plans. Existing file-backed plans can be re-requested.
- Do not write approval `audit.jsonl` at runtime.
- Do not add an audit export command in the first slice.
- Guardrail audit remains gateway-owned and file-backed.
- ASP.NET Data Protection remains file-backed and out of scope.
- `K8S_MCP_APPROVAL_ROOT` stays for now as the existing Data Protection path anchor, even though approval persistence no longer lives there.
- Serilog file logs stay operational telemetry and out of scope.
- `InfraGate.McpServer` receives no approval database settings and should not know Generic Approval Core persistence details.
- New approval database configuration comes from generated run-profile JSON only. Do not add a new environment variable mapping for the approval connection string.
- Run profiles model structured local Postgres settings; generated appsettings contains the connection string.
- Local Compose/dev includes PostgreSQL and a generated/dev password for now.
- Same database, internal `approvals` schema. Schema name is not configurable.
- New project: `src/InfraGate.Approvals.Postgres`.
- Core project `InfraGate.Approvals` exposes approval-core workflow interfaces, approval persistence abstractions, and domain/result records.
- Gateway services must not depend directly on `IApprovalPersistence`.
- `IApprovalPersistence` is an approval-core storage seam used behind broader approval workflow services.
- Dapper and Npgsql dependencies live only in `InfraGate.Approvals.Postgres`.
- Use Dapper over a singleton `NpgsqlDataSource`.
- Runtime SQL can be inline when small; migration DDL lives in SQL files.
- Migration SQL lives under `src/InfraGate.Approvals.Postgres/Migrations`.
- A repo-level lightweight script/runner applies migrations.
- No migration library dependency.
- Migration best practices: checksums, per-file transactions, migration locking, fail with rollback semantics.
- Separate migration and application database permissions.
- Startup validates connection string, DB reachability, schema existence, and required schema version. Fail fast on any missing or stale piece.
- Store canonical JSON text and hashes; do not depend on PostgreSQL `jsonb` for canonicalization or integrity.
- Use regular relational columns for lookup, constraints, and safety checks.
- Persisted Plan Envelopes are immutable.
- Persisted Approval Challenges are immutable.
- Store terminal Challenge Outcomes separately from Approval Challenges.
- `challenge_outcomes.challenge_id` is unique: one terminal outcome per challenge.
- `ChallengeOutcome` has its own domain-generated opaque ID.
- Approval Grants are immutable and reference the approved Challenge Outcome.
- Current string statuses stay as strings.
- Persistence stores supplied IDs. Do not move Plan/Challenge/Grant/Outcome/Execution ID generation into the database.
- Standard relational primary keys, foreign keys, and unique constraints are in scope.
- Approval audit event names and payload JSON shapes stay stable.
- Approval audit is mandatory transactional lifecycle state. If audit insert fails, the state transition rolls back.
- State transitions and their audit events commit atomically.
- Notification dispatch happens after DB commit.
- Keep defense-in-depth generic grant/applied validation in both approval gating and pre-execution gating.
- Add an operational `plan_execution_claims` table keyed by Plan Identifier to prevent concurrent execution.
- Acquire the execution claim after approval gate approval and before pre-execution gate evaluation.
- Do not hold a DB transaction open across Kubernetes mutation.
- Existing claim without terminal outcome fails closed with a stable refusal.
- No automatic TTL cleanup for execution claims.
- Every approved execution try creates an immutable Execution Attempt, including attempts blocked by pre-execution gates.
- `execution.started` audit is emitted only immediately before domain execution, not for pre-execution blocks.
- Execution Attempt is immutable start metadata; Execution Outcome is immutable terminal result.
- `execution_outcomes.status` uses `blocked`, `failed`, or `succeeded`.
- `blocked` is terminal no-retry for that plan. A new plan is required.
- `failed` is retryable by default; retry semantics are Domain Adapter-owned.
- `applied_plans` is the replay-prevention success marker and references the successful Execution Attempt/Outcome.
- Tests that cross the DB seam use PostgreSQL through Testcontainers and are integration tests.
- Do not use in-memory/fake DB persistence tests.
- Existing behavior tests should keep their behavioral intent; replace file assertions/tampering with persistence/database assertions/tampering.
- Tests that exist only for file layout behavior should be removed or replaced with persistence behavior tests.
- ADR required: ADR-0010 records the storage decision.

## Dependency Graph

```text
ADR and persistence decisions
    |
    +-- Core approval contracts and immutable lifecycle records
    |       |
    |       +-- PostgreSQL schema and migrations
    |       |       |
    |       |       +-- Migration runner and DB roles
    |       |       |
    |       |       +-- Postgres adapter implementation
    |       |
    |       +-- Gateway integration with approval-core workflow interfaces
    |               |
    |               +-- Execution claim and outcome flow
    |
    +-- Run-profile/Compose PostgreSQL configuration
    |       |
    |       +-- Local manual and smoke paths
    |
    +-- Testcontainers integration test harness
            |
            +-- Approval persistence tests
            +-- Gateway behavior tests
            +-- Safety/E2E updates where file assertions existed
```

## Architecture Shape

Target modules:

- `InfraGate.Approvals`
  - owns approval-domain abstractions and result records.
  - defines `IApprovalPersistence` as an internal approval-core storage seam.
  - defines broader approval workflow interfaces that Gateway code consumes.
  - defines immutable lifecycle record types where needed: Approval Challenge, Challenge Outcome, Execution Attempt, Execution Outcome.
  - keeps ID generation in approval-domain code.

- `InfraGate.Approvals.Postgres`
  - owns Npgsql/Dapper implementation.
  - owns SQL migrations.
  - exposes DI registration and startup schema validation.
  - contains no Domain Adapter knowledge.

- `InfraGate.McpGateway`
  - composes approval-core workflow interfaces.
  - continues to own approval browser endpoints, guardrails, notification dispatch, and gateway orchestration.
  - does not know SQL details and does not inject `IApprovalPersistence` directly.

- `InfraGate.McpServer`
  - receives no approval database configuration.
  - remains unaware of Generic Approval Core persistence.

Proposed schema tables:

```text
approvals.schema_migrations
approvals.plan_envelopes
approvals.approval_challenges
approvals.challenge_outcomes
approvals.approval_grants
approvals.execution_attempts
approvals.execution_outcomes
approvals.plan_execution_claims
approvals.applied_plans
approvals.audit_events
```

Core table intent:

- `plan_envelopes`: immutable Plan Envelope source record, canonical JSON text, canonical hash, query columns.
- `approval_challenges`: immutable approval attempt record.
- `challenge_outcomes`: terminal outcome, unique by `challenge_id`.
- `approval_grants`: immutable grant, references approved `challenge_outcome_id`.
- `execution_attempts`: immutable started attempt record.
- `execution_outcomes`: terminal result for an execution attempt.
- `plan_execution_claims`: operational concurrency claim, unique by `plan_id`; released after terminal outcome except crash/unknown cases.
- `applied_plans`: durable successful execution marker, unique by `plan_id`.
- `audit_events`: append-only Audit Trail table with monotonic DB order column, event name, correlation columns, payload JSON text.

## Task List

### Phase 1: Contracts and Schema

## Task 1: Define approval-core workflow and persistence seams

**Description:** Introduce `IApprovalPersistence` in `InfraGate.Approvals` as the storage seam behind the approval core, then introduce broader approval workflow interfaces for Gateway-facing operations. Retire the plan to use `ApprovalStore` and `IApprovalChallengeStore` as public persistence seams.

**Acceptance criteria:**
- [ ] `IApprovalPersistence` exists in `InfraGate.Approvals`.
- [ ] Gateway-facing approval workflow interfaces exist for plan recording, approval challenge lifecycle, pre-execution validation, execution attempt begin/finalization, and audit publishing where needed.
- [ ] `IApprovalPersistence` returns approval-domain result types, not SQL rows or database exceptions.
- [ ] Gateway services do not need to know `IApprovalPersistence` to create plans, approve challenges, begin execution attempts, record outcomes, or publish approval audit events.

**Verification:**
- [ ] Build succeeds: `dotnet build InfraGate.slnx`
- [ ] `rg -n "IApprovalPersistence" src/InfraGate.McpGateway` returns no constructor/field usage.

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Approvals/IApprovalPersistence.cs`
- `src/InfraGate.Approvals/*Approval*.cs`
- `src/InfraGate.Approvals/ApprovalStore.cs`
- `src/InfraGate.Approvals/IApprovalChallengeStore.cs`
- `src/InfraGate.Approvals/InfraGate.Approvals.csproj`

**Estimated scope:** M

## Task 2: Align lifecycle records with immutable persistence

**Description:** Adjust approval-domain records so persisted lifecycle objects are immutable attempts plus separate outcomes where needed.

**Acceptance criteria:**
- [ ] `ApprovalChallenge` no longer depends on mutable status/outcome fields for persistence semantics.
- [ ] `ChallengeOutcome` has its own domain-generated ID and references its challenge.
- [ ] Execution Attempt and Execution Outcome records exist in `InfraGate.Approvals`.
- [ ] Status values remain string contracts using existing conventions or new convention constants.

**Verification:**
- [ ] Build succeeds: `dotnet build InfraGate.slnx`
- [ ] Existing audit payload tests still pass or are updated only for intentional model changes.

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.Approvals/ApprovalChallenge.cs`
- `src/InfraGate.Approvals/ChallengeOutcome.cs`
- `src/InfraGate.Approvals/ExecutionAttempt.cs`
- `src/InfraGate.Approvals/ExecutionOutcome.cs`
- `src/InfraGate.Approvals/ApprovalConventions.cs`

**Estimated scope:** M

## Task 3: Add PostgreSQL adapter project

**Description:** Add `InfraGate.Approvals.Postgres` as the only runtime approval persistence implementation. Keep Dapper and Npgsql dependencies isolated there.

**Acceptance criteria:**
- [ ] New project exists at `src/InfraGate.Approvals.Postgres`.
- [ ] Project references `InfraGate.Approvals`.
- [ ] Dapper and Npgsql packages are referenced only by the Postgres project.
- [ ] A DI extension registers `NpgsqlDataSource`, `IApprovalPersistence`, and startup validation services.
- [ ] The adapter project also participates in registering approval-core workflow services without exposing persistence to Gateway constructors.
- [ ] DI registration does not run migrations.

**Verification:**
- [ ] Build succeeds: `dotnet build InfraGate.slnx`
- [ ] `rg -n "Dapper|Npgsql" src/InfraGate.Approvals src/InfraGate.McpGateway` returns no dependency usage outside the Postgres adapter except composition references.
- [ ] `rg -n "IApprovalPersistence" src/InfraGate.McpGateway` returns no constructor/field usage.

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.Approvals.Postgres/InfraGate.Approvals.Postgres.csproj`
- `src/InfraGate.Approvals.Postgres/PostgresApprovalPersistenceServiceCollectionExtensions.cs`
- `src/InfraGate.Approvals.Postgres/PostgresApprovalPersistence.cs`
- `InfraGate.slnx`

**Estimated scope:** M

## Task 4: Add initial approval PostgreSQL schema migration

**Description:** Add plain SQL migration files for the `approvals` schema and all initial tables, constraints, and indexes.

**Acceptance criteria:**
- [ ] Migration creates internal `approvals` schema.
- [ ] Migration creates `schema_migrations`.
- [ ] Migration creates all target tables listed in this plan.
- [ ] Primary keys, foreign keys, and uniqueness constraints enforce the decided invariants.
- [ ] `audit_events` has a DB-generated monotonic order column.
- [ ] No `jsonb` dependency is used for correctness in the first migration.

**Verification:**
- [ ] Migration applies cleanly to an empty PostgreSQL Testcontainers database.
- [ ] Migration can be inspected as plain SQL.

**Dependencies:** Task 2, Task 3

**Files likely touched:**
- `src/InfraGate.Approvals.Postgres/Migrations/0001-initial-approval-persistence.sql`

**Estimated scope:** M

## Checkpoint: Contracts and Schema

- [ ] `dotnet build InfraGate.slnx` succeeds.
- [ ] New project and migration SQL are reviewable without wiring the gateway yet.
- [ ] No runtime file-backed approval adapter is introduced.

### Phase 2: Migrations and Configuration

## Task 5: Implement lightweight migration runner

**Description:** Add a dependency-free repo script/runner that applies Postgres approval migrations explicitly.

**Acceptance criteria:**
- [ ] Runner reads ordered SQL files from `src/InfraGate.Approvals.Postgres/Migrations`.
- [ ] Runner records filename/checksum in `approvals.schema_migrations`.
- [ ] Runner uses migration locking so concurrent deploys cannot apply migrations at the same time.
- [ ] Each migration runs in its own transaction.
- [ ] A failed migration rolls back fully and fails the command.
- [ ] Applied migration checksum drift fails clearly.

**Verification:**
- [ ] Runner applies migrations to a fresh local/Testcontainers database.
- [ ] Re-running the runner is a no-op when checksums match.
- [ ] Deliberately altered applied checksum fails.

**Dependencies:** Task 4

**Files likely touched:**
- `scripts/apply-approval-postgres-migrations.sh`
- `tools/approval-postgres-migrations/*` or a small checked-in script if shell alone is insufficient
- `docs/configuration.md`

**Estimated scope:** M

## Task 6: Wire run profiles and Compose PostgreSQL

**Description:** Extend run profiles and local Compose paths so generated JSON supplies the approval Postgres connection string and local Compose starts PostgreSQL.

**Acceptance criteria:**
- [ ] Run profile model has structured Postgres settings for approval persistence.
- [ ] Generated appsettings JSON contains the approval Postgres connection string.
- [ ] No new approval persistence environment variable mapping is added.
- [ ] Local Compose/dev includes a PostgreSQL service, persistent volume, healthcheck, and generated/dev password.
- [ ] Gateway receives the connection string through generated JSON config.
- [ ] McpServer does not receive approval DB settings.

**Verification:**
- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj` passes.
- [ ] Generated local Compose config contains gateway DB settings and no McpServer DB settings.

**Dependencies:** Task 3

**Files likely touched:**
- `deploy/run-profiles.yaml`
- `src/InfraGate.RunProfiles/*`
- `tests/InfraGate.RunProfiles.Tests/*`
- `deploy/local-oauth/compose.yaml`
- `deploy/compose/*.yaml`

**Estimated scope:** M

## Task 7: Add startup schema validation

**Description:** Validate approval database configuration and schema compatibility during gateway startup.

**Acceptance criteria:**
- [ ] Missing connection string fails startup.
- [ ] Unreachable database fails startup.
- [ ] Missing `approvals` schema or migration table fails startup.
- [ ] Stale schema version fails startup.
- [ ] Startup check does not run migrations or mutate schema.

**Verification:**
- [ ] Focused startup validation integration tests pass with Testcontainers.
- [ ] Manual run against unmigrated DB fails with a clear error.

**Dependencies:** Task 3, Task 4

**Files likely touched:**
- `src/InfraGate.Approvals.Postgres/PostgresApprovalSchemaValidator.cs`
- `src/InfraGate.Approvals.Postgres/PostgresApprovalPersistenceServiceCollectionExtensions.cs`
- `src/InfraGate.McpGateway/Program.cs`
- `tests/InfraGate.Approvals.Postgres.Tests/*`

**Estimated scope:** M

## Checkpoint: Migrations and Configuration

- [ ] Fresh local database can be migrated explicitly.
- [ ] Gateway fails fast when schema/config is missing.
- [ ] Run-profile JSON is the only new approval DB configuration source.

### Phase 3: PostgreSQL Persistence Implementation

## Task 8: Implement plan/challenge/grant workflow over PostgreSQL persistence

**Description:** Implement PostgreSQL persistence for Plan Envelopes, Approval Challenges, Challenge Outcomes, and Approval Grants, then expose that behavior through approval-core workflow services consumed by the Gateway.

**Acceptance criteria:**
- [ ] Plan creation stores immutable plan row, canonical JSON text, and canonical hash.
- [ ] Pending plan lookup preserves existing result semantics and reason codes.
- [ ] Granted plan lookup preserves existing digest, validity, grant, and applied checks.
- [ ] Challenge creation stores immutable challenge rows.
- [ ] Challenge lookup composes challenge plus optional outcome.
- [ ] Approving a challenge stores Challenge Outcome, Approval Grant, and required audit events atomically.
- [ ] Deny/reject/cancel/expire stores Challenge Outcome and required audit event atomically.
- [ ] Notification dispatch remains after commit.
- [ ] `GatewayApprovalService` no longer injects `ApprovalStore`, `IApprovalChallengeStore`, or `IApprovalPersistence` directly.

**Verification:**
- [ ] PostgreSQL integration tests cover create/read plan, drift hash, challenge lifecycle, grant creation, and outcome uniqueness.
- [ ] Existing gateway approval service behavior tests pass after persistence adaptation.

**Dependencies:** Tasks 1-4, Task 7

**Files likely touched:**
- `src/InfraGate.Approvals.Postgres/PostgresApprovalPersistence.cs`
- `src/InfraGate.Approvals/*Approval*.cs`
- `src/InfraGate.Approvals/*Result.cs`
- `src/InfraGate.McpGateway/GatewayApprovalService.cs`
- `tests/InfraGate.Approvals.Postgres.Tests/*`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayApprovalServiceTests.cs`

**Estimated scope:** M

## Task 9: Implement approval audit table writes

**Description:** Move approval audit persistence from JSONL appends to transactional database inserts while preserving event names and payload JSON shapes.

**Acceptance criteria:**
- [ ] `IApprovalAuditPublisher` uses approval persistence or is folded into `IApprovalPersistence` without leaking SQL.
- [ ] Gateway code publishes approval audit through approval-core workflow/publisher interfaces, not direct persistence.
- [ ] Audit payload JSON shape remains compatible with current typed audit payload tests.
- [ ] Audit insert participates in the same transaction as related lifecycle writes.
- [ ] Runtime no longer writes approval `audit.jsonl`.
- [ ] No approval audit export command is added.

**Verification:**
- [ ] Audit payload shape tests pass.
- [ ] Integration tests assert audit rows for plan creation, challenge outcomes, grant issuance, pre-execution validation, execution blocked/failed/succeeded.
- [ ] `rg -n "audit.jsonl|WriteAuditAsync|AppendAllText" src/InfraGate.Approvals src/InfraGate.McpGateway` shows no approval-runtime JSONL writes.
- [ ] `rg -n "IApprovalPersistence" src/InfraGate.McpGateway` returns no constructor/field usage.

**Dependencies:** Task 8

**Files likely touched:**
- `src/InfraGate.Approvals/IApprovalAuditPublisher.cs`
- `src/InfraGate.Approvals.Postgres/PostgresApprovalPersistence.cs`
- `src/InfraGate.McpGateway/GatewayToolDispatcher.cs`
- `tests/InfraGate.McpServer.Tests/UnitTests/AuditPayloadsTests.cs`
- `tests/InfraGate.Approvals.Postgres.Tests/*`

**Estimated scope:** M

## Task 10: Implement execution claims and outcomes

**Description:** Add execution attempt, claim, outcome, and applied-plan persistence behind approval-core workflow services, then wire the gateway execution flow around those services.

**Acceptance criteria:**
- [ ] `BeginExecutionAttempt` runs after approval gate approval and before pre-execution gate evaluation.
- [ ] Claim acquisition is unique by `plan_id`.
- [ ] Existing claim without terminal outcome fails closed with a stable reason code.
- [ ] Existing applied plan refuses execution.
- [ ] Existing blocked outcome refuses execution and requires a new plan.
- [ ] Failed outcomes do not block retry by default.
- [ ] Pre-execution blocked attempts store Execution Attempt, Execution Outcome `blocked`, and audit without `execution.started`.
- [ ] Passed pre-execution followed by mutation stores `execution.started` before domain execution.
- [ ] Successful execution stores Execution Outcome `succeeded`, applied-plan marker, and `execution.succeeded` audit atomically after mutation returns.
- [ ] Failed execution stores Execution Outcome `failed` and `execution.failed` audit.
- [ ] Claims are released after terminal known outcomes.
- [ ] Claims are not automatically TTL-cleaned.
- [ ] `GatewayToolDispatcher` begins/finalizes execution through approval-core workflow interfaces, not direct persistence.

**Verification:**
- [ ] PostgreSQL integration tests cover concurrent claim acquisition for the same plan.
- [ ] Gateway dispatcher tests cover blocked/no-retry, failed/retryable, already-applied, and active-claim refusal.
- [ ] Safety/E2E tests still prove single successful execution and digest-bound execution behavior.

**Dependencies:** Tasks 8-9

**Files likely touched:**
- `src/InfraGate.Approvals/ExecutionAttempt.cs`
- `src/InfraGate.Approvals/ExecutionOutcome.cs`
- `src/InfraGate.Approvals.Postgres/PostgresApprovalPersistence.cs`
- `src/InfraGate.McpGateway/GatewayToolDispatcher.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayToolDispatcherTests.cs`
- `tests/InfraGate.Approvals.Postgres.Tests/*`

**Estimated scope:** M

## Checkpoint: Core Persistence Behavior

- [ ] Approval lifecycle behavior passes against PostgreSQL.
- [ ] Concurrent execution attempts for the same plan cannot both reach Kubernetes mutation.
- [ ] Approval audit rows are transactional and queryable.
- [ ] No approval state or approval audit files are written at runtime.

### Phase 4: Remove File Persistence Coupling

## Task 11: Remove file-backed approval runtime paths

**Description:** Remove or retire file-backed approval store code from runtime composition while keeping Data Protection file behavior intact.

**Acceptance criteria:**
- [ ] Gateway runtime registers PostgreSQL approval persistence only.
- [ ] `ApprovalStore` file path APIs are removed or no longer reachable from runtime code.
- [ ] `IApprovalChallengeStore` is removed or folded into `IApprovalPersistence`.
- [ ] `IApprovalPersistence` is not injected into `InfraGate.McpGateway` services.
- [ ] `K8S_MCP_APPROVAL_ROOT` remains only for Data Protection/platform compatibility, not approval state/audit.
- [ ] Guardrail audit file store remains unchanged.
- [ ] McpServer has no approval persistence dependency or DB settings.

**Verification:**
- [ ] `rg -n "GetPendingPath|GetGrantPath|GetAppliedPath|PendingDirectory|GrantsDirectory|AppliedDirectory" src tests` finds only removed-test replacements or no runtime usage.
- [ ] `rg -n "IApprovalPersistence|ApprovalStore|IApprovalChallengeStore" src/InfraGate.McpGateway` shows no direct persistence usage in Gateway services.
- [ ] Gateway DI wiring tests pass.
- [ ] McpServer tests pass without approval persistence configuration.

**Dependencies:** Tasks 8-10

**Files likely touched:**
- `src/InfraGate.Approvals/ApprovalStore.cs`
- `src/InfraGate.Approvals/ApprovalChallengeStore.cs`
- `src/InfraGate.McpGateway/Program.cs`
- `src/InfraGate.McpServer/KubernetesMcpOptions.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayDiWiringTests.cs`

**Estimated scope:** M

## Task 12: Update behavior tests to use PostgreSQL Testcontainers

**Description:** Convert approval persistence tests from file/temp-path assertions to behavior-focused PostgreSQL integration tests.

**Acceptance criteria:**
- [ ] New test project or integration test suite uses PostgreSQL Testcontainers.
- [ ] Tests do not fake approval database persistence.
- [ ] File-layout-only tests are removed or replaced by persistence behavior tests.
- [ ] Tamper/drift tests manipulate persisted canonical text/hash intentionally instead of appending to files.
- [ ] Existing behavior intent remains covered: digest mismatch, pending plan changed, grant mismatch, same-subject binding, already applied, blocked, failed, succeeded.

**Verification:**
- [ ] `dotnet test tests/InfraGate.Approvals.Postgres.Tests/InfraGate.Approvals.Postgres.Tests.csproj` passes.
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` passes.
- [ ] Opt-in E2E test README documents PostgreSQL/Testcontainers requirements if needed.

**Dependencies:** Tasks 8-11

**Files likely touched:**
- `tests/InfraGate.Approvals.Postgres.Tests/*`
- `tests/InfraGate.McpServer.Tests/UnitTests/ApprovalStoreTests.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/ApprovalChallengeStoreTests.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayApprovalServiceTests.cs`
- `tests/InfraGate.Safety.E2E.Tests/*`

**Estimated scope:** M

## Checkpoint: File Store Removed

- [ ] Approval runtime no longer writes approval files or approval JSONL.
- [ ] Data Protection still persists file-backed keys using the existing path behavior.
- [ ] Guardrail audit still writes its existing JSONL stream.
- [ ] PostgreSQL Testcontainers tests cover approval persistence behavior.

### Phase 5: Documentation and Verification

## Task 13: Update configuration and module docs

**Description:** Update docs to describe PostgreSQL approval persistence, retained Data Protection path behavior, and explicit migration flow.

**Acceptance criteria:**
- [ ] `docs/configuration.md` no longer describes `K8S_MCP_APPROVAL_ROOT` as approval state/audit storage.
- [ ] Docs explain that `K8S_MCP_APPROVAL_ROOT` is retained for Data Protection path compatibility.
- [ ] `src/InfraGate.Approvals/README.md` describes `IApprovalPersistence` and PostgreSQL-backed runtime persistence.
- [ ] `src/InfraGate.McpGateway/README.md` describes approval DB composition and guardrail audit remaining separate.
- [ ] Demo/audit docs describe database audit inspection instead of `.mcp-approvals/audit.jsonl`.
- [ ] Run-profile docs describe generated JSON connection string and local Compose PostgreSQL.

**Verification:**
- [ ] `git diff --check` passes.
- [ ] `rg -n ".mcp-approvals/audit.jsonl|approval storage root|pending/|grants/|applied/" README.md docs src/*/README.md` shows no stale current-state claims.

**Dependencies:** Tasks 5-12

**Files likely touched:**
- `docs/configuration.md`
- `docs/architecture.md`
- `docs/demo-failing-deployment.md`
- `src/InfraGate.Approvals/README.md`
- `src/InfraGate.McpGateway/README.md`
- `docs/devs-readme.md`

**Estimated scope:** M

## Task 14: Full verification pass

**Description:** Run the focused and broad checks needed to prove the slice is ready.

**Acceptance criteria:**
- [ ] Build succeeds.
- [ ] Default test projects pass.
- [ ] PostgreSQL integration tests pass.
- [ ] Migration runner is exercised against a clean DB.
- [ ] Local Compose path starts with PostgreSQL and migrated approval schema.
- [ ] Approval flow works end-to-end with DB persistence.

**Verification:**
- [ ] `dotnet build InfraGate.slnx`
- [ ] `dotnet test tests/InfraGate.Approvals.Postgres.Tests/InfraGate.Approvals.Postgres.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`
- [ ] `dotnet test InfraGate.slnx`
- [ ] Local migration runner succeeds against a clean local Compose DB.

**Dependencies:** Tasks 1-13

**Files likely touched:**
- No planned source edits beyond fixes discovered during verification.

**Estimated scope:** S

## Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| PostgreSQL claim acquired, process crashes before outcome | High | Fail closed with retained claim; require manual recovery/inspection. No TTL cleanup in first slice. |
| Kubernetes mutates successfully but final DB commit fails | High | Keep post-mutation DB transaction small; record success outcome, applied marker, and audit atomically. Treat failures as operational incidents. |
| Too much domain logic moves into SQL | Medium | Keep domain decisions in `InfraGate.Approvals`; SQL stores supplied records and enforces standard relational constraints only. |
| Data Protection root name confuses future maintainers | Medium | ADR/docs must state `K8S_MCP_APPROVAL_ROOT` is temporary platform path compatibility, not approval persistence. |
| Test suite becomes Docker-dependent by default | Medium | Classify DB tests as integration tests. Decide whether they run in default CI once project policy is clear; do not fake persistence. |
| Migration runner grows into fragile custom tooling | Medium | Keep runner narrow: ordered files, checksums, lock, transaction per file, clear failure. Avoid feature creep. |
| Run-profile generated password handling becomes production guidance by accident | Medium | Document generated/dev password as local/dev only; production should provide secure generated JSON through deployment secret handling. |

## Open Questions

- What exact command shape should the no-dependency migration runner expose?
- Should PostgreSQL integration tests run in the default CI test workflow or an opt-in integration workflow?
- What is the manual recovery command/process for stale execution claims? This is intentionally not automatic in the first slice, but operators need a documented path before production positioning.
- Should a later platform configuration slice rename `K8S_MCP_APPROVAL_ROOT` to a Data Protection-specific setting?

## Parallelization Notes

Safe to parallelize after Task 4:

- Run-profile/Compose wiring can proceed alongside core persistence implementation once the connection string contract is stable.
- Documentation updates can begin after the schema and config names are stable.
- Test migration from file assertions to persistence behavior can proceed after `IApprovalPersistence` and the Testcontainers fixture exist.

Must stay sequential:

- Core contract before Postgres adapter.
- Initial schema before Postgres persistence implementation.
- Claim/outcome gateway integration after begin/finalize persistence methods exist.
- Docs finalization after implementation settles exact command names and config JSON shape.
