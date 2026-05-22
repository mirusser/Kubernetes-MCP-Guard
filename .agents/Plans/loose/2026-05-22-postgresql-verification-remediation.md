# Remediation Plan: PostgreSQL Persistence Verification Findings

## Overview

Address the two true blockers and selected Important findings from the 2026-05-22 verification report. The core persistence implementation is complete and all 674 tests pass. This plan closes documentation and ADR-compliance gaps.

## Task List

### Phase 1: Blockers

### Task 1: Remove startup auto-migration (ADR-0010 compliance)

**Description:** Remove the `PostgresApprovalMigrationRunner.ApplyAsync` call from `Program.cs`. Per ADR-0010, schema mutation is an explicit deployment step; the app should only validate compatibility at startup. Also remove the now-unnecessary `using Npgsql;` import and `NpgsqlDataSource` resolution.

**Acceptance criteria:**
- [ ] `Program.cs` no longer calls `PostgresApprovalMigrationRunner.ApplyAsync`.
- [ ] `using Npgsql;` removed from `Program.cs`.
- [ ] `NpgsqlDataSource` resolution removed from `Program.cs`.
- [ ] Startup still validates schema compatibility via `PostgresApprovalSchemaValidator.ValidateAsync`.
- [ ] Build succeeds: `dotnet build InfraGate.slnx`.

**Verification:**
- [ ] `rg -n "ApplyAsync" src/InfraGate.McpGateway/Program.cs` returns no results.
- [ ] `rg -n "using Npgsql" src/InfraGate.McpGateway/Program.cs` returns no results.
- [ ] `rg -n "NpgsqlDataSource" src/InfraGate.McpGateway/` returns no results.

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.McpGateway/Program.cs`

**Estimated scope:** XS

### Task 2: Update stale approval storage claims in `docs/architecture.md`

**Description:** Update the Component Map mermaid diagram (lines 36-46) to reflect PostgreSQL-backed persistence. Update the Audit Flow diagram (lines 314-317,320) to show `approvals.audit_events` table instead of `audit.jsonl`. Update edge labels referencing `ApprovalStore` (line 80). Keep the existing diagram structure; replace stale nodes/labels only.

**Acceptance criteria:**
- [ ] Component Map mermaid no longer references file-backed paths (`pending/*.json`, `grants/*.json`, `applied/*.json`, `challenges/*.json`, `audit.jsonl` for approval).
- [ ] Approval storage nodes reference `approvals.*` PostgreSQL tables.
- [ ] Audit Flow diagram + text references PostgreSQL audit table, not `K8S_MCP_APPROVAL_ROOT/audit.jsonl`.
- [ ] Edge label `persists + loads | ApprovalStore` updated to reference `IApprovalPersistence` / PostgreSQL.

**Verification:**
- [ ] `rg -n "audit\.jsonl" docs/architecture.md` returns no results.
- [ ] `rg -n "pending/\*\.json\|grants/\*\.json\|applied/\*\.json\|challenges/\*\.json" docs/architecture.md` returns no results.

**Dependencies:** None

**Files likely touched:**
- `docs/architecture.md`

**Estimated scope:** S

### Task 3: Update stale approval docs in remaining files

**Description:** Update `docs/security-model.md`, `docs/configuration.md`, and root `README.md` to remove file-backed approval storage claims. Update `docs/why-separated-plan-from-challenge.md` file paths. Update `docs/demo-failing-deployment.md` walkthrough. Update `docs/setup-guide.md` directory tree. Update `src/InfraGate.RunProfiles/README.md` schema reference with `postgresConnectionString`.

**Acceptance criteria:**
- [ ] `docs/security-model.md:37` — grants stored in PostgreSQL `approvals.approval_grants`.
- [ ] `docs/security-model.md:41` — removes stale `ApprovalStore` contract reference.
- [ ] `docs/security-model.md:68` — approval audit is PostgreSQL-backed.
- [ ] `docs/configuration.md:21` — `K8S_MCP_APPROVAL_ROOT` described as Data Protection key path only.
- [ ] `docs/configuration.md:33` — McpGateway section no longer claims file-backed approval storage.
- [ ] `README.md:68` — root diagram updated.
- [ ] `docs/why-separated-plan-from-challenge.md` — file paths replaced with table references.
- [ ] `docs/demo-failing-deployment.md` — references PostgreSQL audit inspection instead of `.mcp-approvals/audit.jsonl`.
- [ ] `docs/setup-guide.md` — directory tree + troubleshooting updated.
- [ ] `src/InfraGate.RunProfiles/README.md` — schema and `--set` table include `postgresConnectionString`.
- [ ] `docs/configuration.md` — adds mention of `InfraGate:Approval:Postgres:ConnectionString`.
- [ ] `docs/devs-readme.md` — mentions PostgreSQL requirement for local gateway run.

**Verification:**
- [ ] `rg -nI "audit\.jsonl" docs/ README.md` returns only guardrail audit references (not approval).
- [ ] `rg -nI "pending/\*\.json\|grants/\*\.json\|applied/\*\.json\|challenges/\*\.json" docs/ README.md` returns no stale approval claims.
- [ ] `rg -nI "IApprovalChallengeStore" docs/` returns no results.
- [ ] `rg -nI "\.mcp-approvals/pending\|\.mcp-approvals/grants\|\.mcp-approvals/applied\|\.mcp-approvals/challenges" docs/` returns no stale approval claims.

**Dependencies:** None (parallelizable with Task 2)

**Files likely touched:**
- `README.md`
- `docs/security-model.md`
- `docs/configuration.md`
- `docs/why-separated-plan-from-challenge.md`
- `docs/demo-failing-deployment.md`
- `docs/setup-guide.md`
- `docs/devs-readme.md`
- `src/InfraGate.RunProfiles/README.md`

**Estimated scope:** M

### Phase 2: Importants

### Task 4: Remove dead code (`ApprovalStore`, `ApprovalStoreOptions`)

**Description:** Delete `ApprovalStore.cs` and `ApprovalStoreOptions.cs` since they are no longer wired into runtime. Also clean stale `ApprovalStore` references in comments.

**Acceptance criteria:**
- [ ] `ApprovalStore.cs` deleted.
- [ ] `ApprovalStoreOptions.cs` deleted.
- [ ] `ApprovalConventions.cs` — remove `PendingDirectory`, `AppliedDirectory`, `ChallengesDirectory`, `GrantsDirectory`, `AuditFileName`, `DefaultRootDirectory`, `JsonExtension`, `Sha256Extension` constants used only by `ApprovalStore`.
- [ ] Any remaining test references to `ApprovalStore` updated (already mostly done in Task 5 of prior remediation).
- [ ] Build succeeds.

**Verification:**
- [ ] `rg -n "ApprovalStore" src/InfraGate.Approvals/` returns no results in live code (comments are fine).
- [ ] `dotnet build InfraGate.slnx` succeeds.

**Dependencies:** Task 1 (shared Program.cs clean surface)

**Files likely touched:**
- `src/InfraGate.Approvals/ApprovalStore.cs` (delete)
- `src/InfraGate.Approvals/ApprovalStoreOptions.cs` (delete)
- `src/InfraGate.Approvals/ApprovalConventions.cs`
- `src/InfraGate.Approvals/ApprovalPreExecutionGate.cs`
- `src/InfraGate.Approvals/AuditPayloads/PlanAuditPayloads.cs`

**Estimated scope:** S

### Task 5: Fix bare catch blocks in Postgres persistence

**Description:** Replace bare `catch` blocks in `PostgresApprovalPersistence.cs` and `PostgresApprovalMigrationRunner.cs` with specific exception handling or `try/finally` pattern for transaction rollback. The standard says catch specific exceptions in library code.

**Acceptance criteria:**
- [ ] All bare `catch` blocks in `PostgresApprovalPersistence.cs` replaced with `try/finally` pattern (transaction rollback + rethrow does not need to catch; finally suffices).
- [ ] `PostgresApprovalMigrationRunner.cs:106` bare `catch` replaced.
- [ ] No functional change — rollback still happens on failure.

**Verification:**
- [ ] `rg -n "^\s*catch\s*$" src/InfraGate.Approvals.Postgres/PostgresApprovalPersistence.cs` returns no results.
- [ ] Postgres integration tests still pass.

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Approvals.Postgres/PostgresApprovalPersistence.cs`
- `src/InfraGate.Approvals.Postgres/PostgresApprovalMigrationRunner.cs`

**Estimated scope:** S

### Task 6: Fix code-standard violations

**Description:** Fix the Important and Nice-to-have code-standards findings from the verification:
- Rename `JsonOptions` → `jsonOptions` in `PostgresApprovalPersistence.cs` and `TestApprovalWorkflow.cs`
- Remove unused `PostgresApprovalConventions.Schema` constant or use it in SQL strings
- Remove vestigial `IOException`/`UnauthorizedAccessException` catch blocks in `GatewayApprovalService.cs`

**Acceptance criteria:**
- [ ] `JsonOptions` renamed to `jsonOptions` in both files.
- [ ] `PostgresApprovalConventions.Schema` either used in SQL strings or removed.
- [ ] `WriteApplyDeniedAuditAsync` no longer catches `IOException` or `UnauthorizedAccessException`.
- [ ] Build succeeds.

**Verification:**
- [ ] `rg -n "JsonOptions" src tests` returns only expected references where explicit type conventions differ.
- [ ] Gateway tests pass.

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Approvals.Postgres/PostgresApprovalPersistence.cs`
- `src/InfraGate.Approvals.Postgres/PostgresApprovalConventions.cs`
- `src/InfraGate.McpGateway/GatewayApprovalService.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/TestApprovalWorkflow.cs`

**Estimated scope:** S

### Task 7: Full verification pass

**Description:** Build, run all test suites, and verify docs are clean.

**Acceptance criteria:**
- [ ] Build succeeds.
- [ ] Postgres integration tests pass.
- [ ] Gateway unit tests pass.
- [ ] McpServer unit tests pass.
- [ ] RunProfiles tests pass.
- [ ] No stale file-backed approval claims in docs.

**Verification:**
- [ ] `dotnet build InfraGate.slnx`
- [ ] `dotnet test tests/InfraGate.Approvals.Postgres.Tests/InfraGate.Approvals.Postgres.Tests.csproj --filter "Category=Postgres"`
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "Category!=Postgres&Category!=GatewayIntegration"`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter "Category!=Postgres&Category!=KubernetesIntegration"`
- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`
- [ ] `rg -nI "audit\.jsonl" docs/ architecture.md security-model.md configuration.md` returns only guardrail audit references.

**Dependencies:** Tasks 1-6

**Files likely touched:**
- None (fixes only)

**Estimated scope:** S

## Checkpoint: Complete

- [ ] ADR-0010 compliant (no auto-migration in startup).
- [ ] No `NpgsqlDataSource` resolved directly in Gateway.
- [ ] All docs reflect PostgreSQL-backed approval persistence.
- [ ] Dead code removed or explicitly marked.
- [ ] Code-standards violations fixed.
- [ ] All tests pass.

## Deferred (Nice-to-have)

These items from the verification report are intentionally deferred:
- Test coverage gaps (denial path at persistence layer, concurrent claim, checksum drift) — existing integration + E2E tests cover these paths at higher layers
- `InternalsVisibleTo` additions — add when a test actually needs internal access
- Schema name in SQL strings vs constant — low-risk, cosmetic
- `var` usage on tuple — readability judgment call
- Multiple top-level types in one file — two small private records are acceptable per standards carve-out
- Shallow aggregate interface `IApprovalPersistence` — provides DI convenience, acceptable

## Parallelization Notes

Tasks 1, 2, 3, 5, and 6 are all independent and can run in parallel. Task 4 depends on Task 1 (shared file). Task 7 is last.
