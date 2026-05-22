# Remediation Plan: PostgreSQL Approval Persistence Gaps

## Overview

Close the three gaps identified in the 2026-05-22 verification of the PostgreSQL approval persistence implementation: run-profile/Compose PostgreSQL configuration (Task 6), documentation updates (Task 13), and orphaned-type removal (Task 11 cleanup).

The core persistence slice (Tasks 1-5, 7-10, 12) is complete and does not need rework.

## Architecture Decisions

These are decided — do not re-litigate:

- Approval connection string comes from generated run-profile JSON only (no new env var mapping).
- Run profiles model structured local Postgres settings; generated appsettings contains the connection string.
- Local Compose/dev includes PostgreSQL and a generated/dev password.
- Same database, internal `approvals` schema.
- `K8S_MCP_APPROVAL_ROOT` stays for Data Protection path only.
- `GenericApprovalCoreProfile` gets a `PostgresConnectionString` field rendered as `InfraGate:Approval:Postgres:ConnectionString`.
- Default dev Postgres settings: host `postgres`, port `5432`, db `infra-gate`, user `infra-gate`, password `infra-gate-dev-password`.
- Local-compose and smoke-local profiles get Postgres settings.
- Do not add Postgres to non-Compose profiles (local-source-gateway, local-stdio, test-*, production).

## Task List

### Task 1: Add Postgres connection string to run profile model

**Description:** Add a `PostgresConnectionString` field to `GenericApprovalCoreProfile` and wire it through `EnvFileRenderer` and `AppSettingsRenderer` so generated config carries the connection string to the gateway.

**Acceptance criteria:**
- [ ] `GenericApprovalCoreProfile` has an optional `PostgresConnectionString` field.
- [ ] `AppSettingsRenderer.WriteApproval` writes `Postgres:ConnectionString` under `InfraGate:Approval` when the field is set.
- [ ] `EnvFileRenderer.AppendGenericApprovalCore` does NOT emit the connection string (approval DB config is JSON-only per plan).
- [ ] `RunProfileDocument.MergeGenericApprovalCore` merges the field from defaults.
- [ ] No new environment variable mapping is added.

**Verification:**
- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj` passes.
- [ ] Generated appsettings JSON for local-compose contains `InfraGate.Approval.Postgres.ConnectionString`.

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.RunProfiles/GenericApprovalCoreProfile.cs`
- `src/InfraGate.RunProfiles/AppSettingsRenderer.cs`
- `src/InfraGate.RunProfiles/EnvFileRenderer.cs`
- `src/InfraGate.RunProfiles/RunProfileDocument.cs`
- `src/InfraGate.RunProfiles/RunProfileDocumentReader.cs`
- `tests/InfraGate.RunProfiles.Tests/UnitTests/RunProfileCliTests.cs`

**Estimated scope:** S

### Task 2: Add Postgres settings to run-profiles.yaml

**Description:** Add `postgresConnectionString` to relevant profiles in `deploy/run-profiles.yaml` and to defaults so generated JSON includes the connection string.

**Acceptance criteria:**
- [ ] Default `genericApprovalCore` includes `postgresConnectionString` pointing to local Compose Postgres.
- [ ] `local-compose`, `smoke-local`, and `smoke-release` profiles include Postgres settings (or inherit from defaults).
- [ ] Non-Compose profiles (local-source-gateway, local-stdio, test-*, development, production) do NOT receive Postgres settings.
- [ ] Connection string uses Compose service name `postgres`, port `5432`, db `infra-gate`, user `infra-gate`, password `infra-gate-dev-password`.
- [ ] `GenericApprovalCoreProfile` still requires `approvalRoot` (unchanged for other profiles).

**Verification:**
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- generate --profile local-compose` produces appsettings with `InfraGate.Approval.Postgres.ConnectionString`.
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate` passes.

**Dependencies:** Task 1

**Files likely touched:**
- `deploy/run-profiles.yaml`

**Estimated scope:** S

### Task 3: Add PostgreSQL service to local Compose

**Description:** Add a PostgreSQL container to `deploy/local-oauth/compose.yaml` with persistent volume, healthcheck, and the dev password. Wire the gateway's `depends_on` to include it.

**Acceptance criteria:**
- [ ] `compose.yaml` has a `postgres` service using `postgres:17-alpine`.
- [ ] Environment sets `POSTGRES_DB=infra-gate`, `POSTGRES_USER=infra-gate`, `POSTGRES_PASSWORD=infra-gate-dev-password`.
- [ ] Named volume for persistence (e.g., `pgdata`).
- [ ] Healthcheck using `pg_isready`.
- [ ] `mcp-gateway` service `depends_on` includes `postgres` with `condition: service_healthy`.
- [ ] Postgres is on the `default` (compose-internal) network only — no port mapping to host (gateway accesses it internally).
- [ ] Release env example includes the Postgres password if needed.

**Verification:**
- [ ] `docker compose -f deploy/local-oauth/compose.yaml config` shows postgres service.
- [ ] Generated env file contains required Postgres env vars for the Compose interpolation.

**Dependencies:** Task 2

**Files likely touched:**
- `deploy/local-oauth/compose.yaml`
- `deploy/local-oauth/release.env.example`

**Estimated scope:** S

### Task 4: Add migration runner invocation at gateway startup

**Description:** The gateway should run migrations on startup (after schema validation) so a fresh Compose bring-up works end-to-end. This is a dev-convenience measure; production deployments should run migrations separately.

**Acceptance criteria:**
- [ ] After schema validation passes in `Program.cs`, apply pending migrations via `PostgresApprovalMigrationRunner.ApplyAsync`.
- [ ] Migration failure fails startup with a clear error.
- [ ] Migration is a no-op when already up-to-date.
- [ ] This behavior is development-only; document that production should run the migration runner explicitly before starting the app.

**Verification:**
- [ ] Fresh Compose `docker compose up` results in migrated schema and working gateway.
- [ ] Second `docker compose up` is a no-op for migrations.

**Dependencies:** Task 3

**Files likely touched:**
- `src/InfraGate.McpGateway/Program.cs`

**Estimated scope:** XS

### Task 5: Remove orphaned file-backed types

**Description:** Remove `IApprovalChallengeStore`, `ApprovalChallengeStore`, and `ApprovalStoreAuditPublisher` since they are no longer wired into runtime. `ApprovalStore` stays for now (it still implements `IApprovalPlanWorkflow` and is referenced by the McpServer for legacy plan ID generation via its static `NewPlanId` — but that's now in `ApprovalIds`).

**Acceptance criteria:**
- [ ] `IApprovalChallengeStore.cs` is deleted.
- [ ] `ApprovalChallengeStore.cs` is deleted.
- [ ] `ApprovalStoreAuditPublisher.cs` is deleted.
- [ ] Any test files still referencing these types are updated or removed.
- [ ] `ApprovalStore.NewPlanId()` static method is removed (callers use `ApprovalIds.NewPlanId()`).
- [ ] Build succeeds: `dotnet build InfraGate.slnx`.

**Verification:**
- [ ] `rg -n "IApprovalChallengeStore|ApprovalChallengeStore|ApprovalStoreAuditPublisher" src` returns no results.
- [ ] `rg -n "ApprovalStore\.NewPlanId" src tests` returns no results.
- [ ] Existing gateway and McpServer tests pass.

**Dependencies:** None (pure removal)

**Files likely touched:**
- `src/InfraGate.Approvals/IApprovalChallengeStore.cs` (delete)
- `src/InfraGate.Approvals/ApprovalChallengeStore.cs` (delete)
- `src/InfraGate.Approvals/ApprovalStoreAuditPublisher.cs` (delete)
- `src/InfraGate.Approvals/ApprovalStore.cs` (remove static NewPlanId)
- `src/InfraGate.KubernetesAdapter/KubernetesPlanBuilder.cs` (switch to ApprovalIds)
- `tests/InfraGate.McpGateway.Tests/UnitTests/ApprovalChallengeStoreTests.cs` (delete or update)

**Estimated scope:** S

### Task 6: Clean stale audit.jsonl references in code comments

**Description:** Update comments in `ApprovalStore.cs`, `PlanAuditPayloads.cs`, and `ChallengeAuditPayloads.cs` that reference `audit.jsonl` writes. These comments are misleading since the PostgreSQL-backed runtime writes to `approvals.audit_events` table.

**Acceptance criteria:**
- [ ] `PlanAuditPayloads.cs:1` comment no longer says "written to audit.jsonl".
- [ ] `ChallengeAuditPayloads.cs:2` comment no longer says "audit.jsonl".
- [ ] `ApprovalConventions.Storage.AuditFileName` doc comment or surrounding context clearly states it's for the legacy file-backed store only.
- [ ] No functional code changes.

**Verification:**
- [ ] `rg -n "audit\.jsonl" src` returns only the Guardrail audit path and legacy store references (no misleading approval audit comments).

**Dependencies:** Task 5

**Files likely touched:**
- `src/InfraGate.Approvals/AuditPayloads/PlanAuditPayloads.cs`
- `src/InfraGate.Approvals/AuditPayloads/ChallengeAuditPayloads.cs`
- `src/InfraGate.Approvals/ApprovalConventions.cs`

**Estimated scope:** XS

### Task 7: Update module docs for PostgreSQL persistence

**Description:** Update `InfraGate.Approvals/README.md` and `InfraGate.McpGateway/README.md` to reflect PostgreSQL-backed approval persistence, the decoupled workflow interfaces, and the retained Data Protection path behavior.

**Acceptance criteria:**
- [ ] `src/InfraGate.Approvals/README.md` describes `IApprovalPersistence` as the PostgreSQL-backed persistence seam and lists the workflow interfaces.
- [ ] `src/InfraGate.Approvals/README.md` documents `ApprovalStore` as the legacy file-backed implementation (still in codebase but not runtime-wired).
- [ ] `src/InfraGate.McpGateway/README.md` mentions the approval Postgres connection string config key.
- [ ] `docs/configuration.md` or `docs/devs-readme.md` notes the Compose PostgreSQL service and migration step.
- [ ] No stale claims about `.mcp-approvals/audit.jsonl` for approval events in docs.

**Verification:**
- [ ] `rg -n ".mcp-approvals/audit\.jsonl" README.md docs src/*/README.md` returns no approval-runtime claims (guardrail audit is fine).

**Dependencies:** Task 5, Task 6

**Files likely touched:**
- `src/InfraGate.Approvals/README.md`
- `src/InfraGate.McpGateway/README.md`
- `docs/configuration.md`

**Estimated scope:** S

### Task 8: Full verification pass

**Description:** Run the test suite, validate local Compose, and exercise the approval flow end-to-end with PostgreSQL.

**Acceptance criteria:**
- [ ] Build succeeds: `dotnet build InfraGate.slnx`.
- [ ] Run-profile tests pass.
- [ ] PostgreSQL integration tests pass.
- [ ] Gateway tests pass.
- [ ] `docker compose up` starts with PostgreSQL, migrations apply, schema validates, gateway starts.
- [ ] Approval flow works: create plan, approve via browser, execute approved plan, verify applied marker in DB.

**Verification:**
- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.Approvals.Postgres.Tests/InfraGate.Approvals.Postgres.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- [ ] Manual end-to-end smoke test with Compose.

**Dependencies:** Tasks 1-7

**Files likely touched:**
- No planned source edits (fixes only).

**Estimated scope:** S

## Checkpoint: Remediation Complete

- [ ] PostgreSQL available in local Compose with migrated schema.
- [ ] Gateway receives connection string through generated run-profile JSON.
- [ ] Orphaned file-backed types removed.
- [ ] Approval docs reflect PostgreSQL persistence.
- [ ] No misleading `audit.jsonl` comments in approval code.
- [ ] All tests pass.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Generated dev password leaks into production config | Medium | Document that production should supply its own generated JSON; dev password is only in local profiles |
| Migration-on-startup masks schema drift in production | Medium | Document that production should run migrations explicitly; startup migration is dev convenience only |
| Removing `ApprovalStore.NewPlanId` breaks McpServer plan ID generation | Low | `ApprovalIds.NewPlanId()` already exists and is the canonical replacement |

## Parallelization Notes

Tasks 1, 5, and 6 are independent and can run in parallel. Tasks 2-4 must be sequential. Task 7 depends on 5-6. Task 8 is last.
