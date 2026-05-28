# Implementation Plan: Use Real Postgres Persistence in Safety E2E Tests

## Overview

Replace the in-memory `InMemoryApprovalChallengeWorkflow` and file-backed `ApprovalStore` in the Safety E2E test fixture with the real `PostgresApprovalPersistence` backed by a Postgres Testcontainers instance. This ensures the E2E safety properties are verified against the same transactional PostgreSQL persistence path used in production — including atomic audit writes, concurrent claim acquisition, and applied-plan markers.

## Architecture Decisions

- One Postgres `postgres:17-alpine` container per fixture suite (shared via `IAsyncLifetime`).
- Migrations applied once at fixture init via `PostgresApprovalMigrationRunner.ApplyAsync`.
- All four workflow interfaces (`IApprovalPlanWorkflow`, `IApprovalChallengeWorkflow`, `IApprovalExecutionWorkflow`, `IApprovalAuditPublisher`) wired through `PostgresApprovalPersistence` in the Gateway TestServer DI — matching production `Program.cs`.
- `ApprovalStore` and `InMemoryApprovalChallengeWorkflow` removed from the fixture entirely.
- Audit assertions query `approvals.audit_events` directly instead of reading `audit.jsonl`.
- File-path assertions (`File.Exists(GetGrantPath(...))`) replaced with SQL queries against `approvals.approval_grants`, `approvals.applied_plans`, and `approvals.execution_outcomes`.
- Plan tampering tests mutate persisted `canonical_json_text` or `canonical_sha256` directly in the database instead of appending to files.

## Task List

### Phase 1: Fixture Infrastructure

### Task 1: Add Postgres Testcontainers to E2E fixture

**Description:** Add `Testcontainers.PostgreSql` package reference and `InfraGate.Approvals.Postgres` project reference to the E2E test project. Add a `PostgreSqlContainer` field to `SafetyE2EFixture`, initialize it in `InitializeAsync`, apply migrations, and dispose in `DisposeAsync`. Store `NpgsqlDataSource` for test queries. Remove `InMemoryApprovalChallengeWorkflow`, `ApprovalStore`, and related fields/properties.

**Acceptance criteria:**
- [ ] `Testcontainers.PostgreSql` package added to `InfraGate.Safety.E2E.Tests.csproj`.
- [ ] `InfraGate.Approvals.Postgres` project reference added.
- [ ] `PostgreSqlContainer` field, image `postgres:17-alpine`, started in `InitializeAsync`.
- [ ] Migrations applied via `PostgresApprovalMigrationRunner.ApplyAsync` before Gateway TestServer.
- [ ] `NpgsqlDataSource` singleton created from container connection string.
- [ ] `InMemoryApprovalChallengeWorkflow` file deleted, field removed.
- [ ] `ApprovalStore` field + `ApprovalStore` property removed.
- [ ] `challengeStore` local + `ChallengeStore` property removed.

**Verification:**
- [ ] Build succeeds: `dotnet build InfraGate.slnx`.
- [ ] No compile errors from removed types.

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj`
- `tests/InfraGate.Safety.E2E.Tests/SafetyE2EFixture.cs`
- `tests/InfraGate.Safety.E2E.Tests/InMemoryApprovalChallengeWorkflow.cs` (delete)

**Estimated scope:** S

### Task 2: Replace Gateway DI wiring with PostgresApprovalPersistence

**Description:** In `CreateGatewayServer()`, replace `ApprovalStore` and `InMemoryApprovalChallengeWorkflow` registrations with `PostgresApprovalPersistence` via `AddPostgresApprovalPersistence`. Match production `Program.cs` wiring: register `NpgsqlDataSource`, `IApprovalPersistence`, workflow interfaces, and `PostgresApprovalSchemaValidator`. Remove `ApprovalStoreOptions`.

**Acceptance criteria:**
- [ ] `PostgresApprovalPersistence` registered as `IApprovalPersistence`.
- [ ] `IApprovalPlanWorkflow`, `IApprovalChallengeWorkflow`, `IApprovalExecutionWorkflow`, `IApprovalAuditPublisher` resolved from persistence.
- [ ] `ApprovalStore` and `InMemoryApprovalChallengeWorkflow` registrations removed.
- [ ] Schema validation runs on gateway startup (passes since migrations applied in Task 1).

**Verification:**
- [ ] Build succeeds.
- [ ] Gateway DI resolves without errors.

**Dependencies:** Task 1

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/SafetyE2EFixture.cs`

**Estimated scope:** S

### Task 3: Replace audit reading with PostgreSQL queries

**Description:** Replace `ReadAuditEventsAsync` (which reads `audit.jsonl`) with Dapper queries against `approvals.audit_events`. Parse `payload_json_text` as JSON per row. Return the same `IReadOnlyList<JsonElement>` shape so existing assertions don't need changes. Add optional `planId` filter.

**Acceptance criteria:**
- [ ] `ReadAuditEventsAsync` queries `severals payload_json_text from approvals.audit_events order by audit_sequence`.
- [ ] Returns `IReadOnlyList<JsonElement>` — identical shape, no test assertion changes.
- [ ] Optional `planId` parameter for plan-scoped audit queries.
- [ ] File-read code removed.

**Verification:**
- [ ] Build succeeds.
- [ ] Existing audit assertions in workflow tests compile without changes.

**Dependencies:** Task 2

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/SafetyE2EFixture.cs`

**Estimated scope:** S

### Checkpoint: Foundation

- [ ] Postgres container starts, migrations apply, gateway starts with real Postgres persistence.
- [ ] Audit can be read from `approvals.audit_events`.
- [ ] All DI wiring compiles.

### Phase 2: Update Workflow Tests

### Task 4: Replace file-path assertions with PostgreSQL queries

**Description:** Replace all `Assert.False/True(File.Exists(fixture.ApprovalStore.GetGrantPath/GrantPath/AppliedPath(...)))` with Dapper queries against `approvals.approval_grants`, `approvals.applied_plans`, etc. Add helper methods to `SafetyE2EFixture` for common queries. Replace `fixture.ApprovalStore.*` and `fixture.ChallengeStore.*` calls with persistence or DB helpers.

**Acceptance criteria:**
- [ ] Fixture helpers: `GrantExistsAsync(planId)`, `IsPlanAppliedAsync(planId)`, `HasActiveExecutionClaimAsync(planId)`, `GetExecutionOutcomeStatusAsync(planId)`.
- [ ] All `File.Exists(GetGrantPath(...))` → `GrantExistsAsync(...)`.
- [ ] All `File.Exists(GetAppliedPath(...))` → `IsPlanAppliedAsync(...)`.
- [ ] All `fixture.ApprovalStore.*` → persistence call or DB helper.
- [ ] All `fixture.ChallengeStore.*` → persistence call or DB helper.

**Verification:**
- [ ] Build succeeds.
- [ ] No `fixture.ApprovalStore` or `fixture.ChallengeStore` references in any workflow test.

**Dependencies:** Task 3

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/SafetyE2EFixture.cs`
- `tests/InfraGate.Safety.E2E.Tests/Workflows/FullApprovalFlowTests.cs`
- `tests/InfraGate.Safety.E2E.Tests/Workflows/AlreadyAppliedPlanTests.cs`
- `tests/InfraGate.Safety.E2E.Tests/Workflows/WrongUserApprovalTests.cs`
- `tests/InfraGate.Safety.E2E.Tests/Workflows/ExpiredApprovalTests.cs`
- `tests/InfraGate.Safety.E2E.Tests/Workflows/ModifiedPendingPlanTests.cs`
- `tests/InfraGate.Safety.E2E.Tests/Workflows/ReviewDigestMismatchTests.cs`
- `tests/InfraGate.Safety.E2E.Tests/Workflows/DryRunFailureTests.cs`
- `tests/InfraGate.Safety.E2E.Tests/Workflows/RbacMatrixTests.cs`

**Estimated scope:** M

### Task 5: Update plan tampering tests to tamper DB instead of files

**Description:** `ModifiedPendingPlanTests` and `ReviewDigestMismatchTests` mutate the pending plan file. `DryRunFailureTests` uses `File.AppendAllText`. Replace all file-tampering with SQL UPDATEs on `approvals.plan_envelopes.canonical_json_text` and `canonical_sha256`.

**Acceptance criteria:**
- [ ] `ModifiedPendingPlanTests` UPDATEs `canonical_json_text` to trigger `PendingPlanChanged`.
- [ ] `ReviewDigestMismatchTests` UPDATEs review digest columns to trigger `DigestChanged`.
- [ ] `DryRunFailureTests` tamper path uses DB mutation instead of `File.AppendAllText`.
- [ ] Correct reason codes asserted (`PendingPlanChanged`, `DigestChanged`).

**Verification:**
- [ ] Build succeeds.
- [ ] No file-path references in tamper tests.

**Dependencies:** Task 4

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/Workflows/ModifiedPendingPlanTests.cs`
- `tests/InfraGate.Safety.E2E.Tests/Workflows/ReviewDigestMismatchTests.cs`
- `tests/InfraGate.Safety.E2E.Tests/Workflows/DryRunFailureTests.cs`

**Estimated scope:** S

### Task 6: Update RbacMatrixTests direct store usage

**Description:** `RbacMatrixTests` calls `fixture.ApprovalStore` directly. Replace with `PostgresApprovalPersistence` workflow calls.

**Acceptance criteria:**
- [ ] `GetPendingPlanAsync` → persistence call.
- [ ] `CreateGrantAsync` → create challenge + approve via persistence.
- [ ] `MarkAppliedAsync` → `RecordExecutionSucceededAsync`.
- [ ] `File.Exists(GetAppliedPath(...))` → `IsPlanAppliedAsync(...)`.

**Verification:**
- [ ] Build succeeds.
- [ ] No `ApprovalStore` references.

**Dependencies:** Task 4

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/Workflows/RbacMatrixTests.cs`

**Estimated scope:** S

### Task 7: Update WrongUserApprovalTests

**Description:** `WrongUserApprovalTests` uses `ApprovalStore` extensively. Replace all with persistence calls and DB queries.

**Acceptance criteria:**
- [ ] `GetPendingPlanAsync` → persistence.
- [ ] `ComputeSha256Async` + `ApprovallPath` → `ApprovalCanonicalJson.ComputeSha256Hex`.
- [ ] `CreatePlanAsync` → persistence.
- [ ] `ChallengeStore.CreateChallengeAsync` / `GetChallengeAsync` → persistence.
- [ ] All `File.Exists(GetGrantPath(...))` → DB query.

**Verification:**
- [ ] Build succeeds.
- [ ] No `ApprovalStore` or `ChallengeStore` references.

**Dependencies:** Task 4, Task 5 (shared plan patterns)

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/Workflows/WrongUserApprovalTests.cs`

**Estimated scope:** M

### Checkpoint: All Tests Updated

- [ ] No `ApprovalStore`, `InMemoryApprovalChallengeWorkflow`, or `ChallengeStore` references in any file.
- [ ] No `File.Exists(...)` assertions on approval file paths.
- [ ] All tamper tests use DB mutations.

### Phase 3: Documentation and Verification

### Task 8: Update E2E README for PostgreSQL prerequisite

**Description:** Update `tests/InfraGate.Safety.E2E.Tests/README.md` to document the Postgres requirement. Postgres is automatic via Testcontainers — no manual setup needed.

**Acceptance criteria:**
- [ ] README documents Postgres as an automatic prerequisite (handled by Testcontainers).
- [ ] README states approval state/audit is PostgreSQL-backed.
- [ ] README lists infrastructure footprint: Keycloak + Postgres containers + K8s cluster.
- [ ] No stale `audit.jsonl` or file-backed approval references.

**Verification:**
- [ ] README review shows no stale claims.

**Dependencies:** Tasks 1-7

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/README.md`

**Estimated scope:** S

### Task 9: Full verification pass

**Description:** Build, run all test suites, verify E2E with `INFRA_GATE_RUN_SAFETY_E2E=1`.

**Acceptance criteria:**
- [ ] Build succeeds.
- [ ] All non-E2E test tiers pass.
- [ ] Safety E2E tests pass with `INFRA_GATE_RUN_SAFETY_E2E=1`.
- [ ] All original safety properties verified (same assertions, DB-backed).

**Verification:**
- [ ] `dotnet build InfraGate.slnx`
- [ ] `dotnet test tests/InfraGate.Approvals.Postgres.Tests/ ... --filter "Category=Postgres"`
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/ ... --filter "Category!=Postgres..."`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/ ... --filter "Category!=Postgres"`
- [ ] `INFRA_GATE_RUN_SAFETY_E2E=1 dotnet test tests/InfraGate.Safety.E2E.Tests/ ... --filter "Category=SafetyE2E"`

**Dependencies:** Tasks 1-8

**Files likely touched:**
- None (fixes only)

**Estimated scope:** M

## Checkpoint: Complete

- [ ] E2E tests exercise real PostgreSQL persistence for all approval workflows.
- [ ] Audit events verified through `approvals.audit_events` table queries.
- [ ] Concurrent claim acquisition tested at the DB level.
- [ ] All 10 E2E workflow tests pass with Postgres backend.
- [ ] `ApprovalStore`, `InMemoryApprovalChallengeWorkflow` removed from E2E fixture.
- [ ] No file-backed approval state in safety tests.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| E2E test runtime increases | Medium | One Postgres container per suite (shared fixture). Migration once. Container startup ~3-5s overhead on 30-60s suite. |
| Postgres port conflicts | Low | Testcontainers uses dynamic port mapping. |
| Schema validation fails before gateway starts | Medium | Migrations applied before TestServer creation. SQL files already `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`. |
| File-tampering scenarios have no DB equivalent | Low | Every file scenario has a direct SQL UPDATE equivalent. |
| E2E CI workflow needs changes | Low | No. Testcontainers manages its own containers. Docker is already a CI prerequisite. |

## Parallelization Notes

- Task 1 starts immediately. Tasks 2-3 sequential afterTask 1. Tasks 4-7 all independent and parallelizable after 2-3. Task 8 after tests. Task 9 last.
