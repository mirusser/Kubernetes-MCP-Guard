# Implementation Plan: Audit Outbox Remediation

## Overview
This plan remediates the gaps identified during the verification of the `audit-outbox-roadmap.md` implementation. It addresses missing unit tests, architectural drifts involving test doubles and split interfaces, code standard violations (`var`, redundant `catch(Exception)`, magic strings), and stale documentation. 

## Architecture Decisions
- **Restore Test Locality:** We will remove test doubles (`NullApprovalAuditOutbox`, `RecordingApprovalAuditOutbox`) and wire up the real, Testcontainers-backed `ApprovalAuditOutbox` in integration tests, as originally mandated by ADR-0020 and Task 2.4.
- **Unify Interface:** We will flatten the `ITransactionalApprovalAuditOutbox` split back into the primary `IApprovalAuditOutbox` interface to match the Observer and Planner seams.
- **Clean Standards:** All `var` primitives, redundant transaction `catch` blocks, and magic strings will be refactored inline.

## Task List

### Phase 1: Architectural Fixes & Standards Cleanup
- [x] **Task 1: Unify Approval Outbox Interface**
  **Description:** Merge `ITransactionalApprovalAuditOutbox` into `IApprovalAuditOutbox` and update the implementation in `ApprovalAuditOutbox.cs`.
  **Acceptance criteria:**
  - `IApprovalAuditOutbox` declares both `AppendAsync` overloads.
  - `ITransactionalApprovalAuditOutbox` is deleted.
  **Files likely touched:** `src/InfraGate.Approvals/Audit/IApprovalAuditOutbox.cs`, `src/InfraGate.Approvals.Postgres/ApprovalAuditOutbox.cs`
  **Estimated scope:** Small

- [x] **Task 2: Fix Standards Violations (`var`, `catch`, strings)**
  **Description:** Fix the identified standards violations across the outbox wrappers and entry points.
  **Acceptance criteria:**
  - `var sequence` -> `long sequence` and `var ...String` -> `string ...String` in Programs.
  - Redundant `catch(Exception)` blocks with manual `transaction.RollbackAsync` are removed.
  - Schema names ("observer", "planner") and lock prefix ("audit_outbox_migration:") are replaced with constants.
  **Files likely touched:** `ObserverAuditOutbox.cs`, `PlannerAuditOutbox.cs`, `ApprovalAuditOutbox.cs`, `PostgresAuditOutboxMigrationRunner.cs`, `Program.cs`
  **Estimated scope:** Medium

### Checkpoint: Architecture & Standards
- [x] Code builds without errors.

### Phase 2: Missing Wrapper Tests
- [x] **Task 3: Add `ApprovalAuditOutboxTests.cs`**
  **Description:** Implement the missing unit tests for the Approval stream wrapper.
  **Acceptance criteria:**
  - Tests verify typed-to-canonical extraction of correlation IDs.
  - Tests verify both `AppendAsync` overloads execute correctly.
  **Files likely touched:** `tests/InfraGate.Approvals.Postgres.Tests/ApprovalAuditOutboxTests.cs`
  **Estimated scope:** Small

- [x] **Task 4: Expand Observer and Planner Wrapper Tests**
  **Description:** Add extraction and overload tests for `ObserverAuditOutbox` and `PlannerAuditOutbox`, beyond their current null-guard tests.
  **Acceptance criteria:**
  - `ObserverAuditOutbox` correlation extraction is tested.
  - `PlannerAuditOutbox` correlation extraction is tested.
  **Files likely touched:** `tests/InfraGate.Observer.Tests/UnitTests/ObserverAuditEventsTests.cs` (or create `ObserverAuditOutboxTests.cs`), `tests/InfraGate.Planner.Tests/UnitTests/PlannerAuditEventsTests.cs` (or create `PlannerAuditOutboxTests.cs`)
  **Estimated scope:** Medium

### Checkpoint: Unit Test Coverage
- [x] `dotnet test` runs and passes for the wrapper unit tests.

### Phase 3: Integration Tests & Docs
- [x] **Task 5: Restore Persistence Integration Coverage**
  **Description:** Replace `NullApprovalAuditOutbox` and `RecordingApprovalAuditOutbox` with the actual Postgres-backed outbox in the persistence integration tests. Fix hardcoded string assertions in `CrossStreamForensicTests.cs`.
  **Acceptance criteria:**
  - `PostgresApprovalPersistenceTests` writes to the Testcontainers DB using `ApprovalAuditOutbox`.
  - `ApprovalPreExecutionGateTests` uses Testcontainers or is refactored to verify correctly without bypassing the DB.
  - `CrossStreamForensicTests.cs` uses `AuditOutboxConventions.Streams.*` and domain event constants.
  **Files likely touched:** `PostgresApprovalPersistenceTests.cs`, `ApprovalPreExecutionGateTests.cs`, `CrossStreamForensicTests.cs`
  **Estimated scope:** Large

- [x] **Task 6: Update Stale Documentation**
  **Description:** Update the docs to reflect the new `audit_outbox` schema and connection string variables.
  **Acceptance criteria:**
  - `docs/configuration.md` includes the two new connection string env vars.
  - `docs/architecture.md`, `docs/demo-failing-deployment.md`, and `docs/security-model.md` reference `audit_outbox` instead of `audit_events`.
  **Files likely touched:** `docs/configuration.md`, `docs/architecture.md`, `docs/demo-failing-deployment.md`, `docs/security-model.md`
  **Estimated scope:** Small

### Checkpoint: Complete
- [x] All tests pass.
- [x] All acceptance criteria met.
- [x] Ready for final review.

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| Restoring the real outbox in `PostgresApprovalPersistenceTests` causes existing test failures | Medium | The tests should just write more rows. If tests assert exact table counts globally, they may need scoping. |
