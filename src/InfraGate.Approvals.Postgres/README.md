# InfraGate.Approvals.Postgres

`InfraGate.Approvals.Postgres` owns durable PostgreSQL persistence for the generic approval workflow. It implements `IApprovalPersistence` via Npgsql, runs schema migrations on startup, provides `PostgresApprovalAccessCodeStore` for one-time Approval Access Codes, and hosts `ApprovalAuditOutbox` — the Approvals stream's typed audit wrapper.

**Owns:** durable persistence for approval workflows

## Approval Audit Stream

`ApprovalAuditOutbox` wraps `IAuditOutboxCore` and exposes two overloads:

- `AppendAsync(ApprovalAuditEntry, CancellationToken)` — opens its own connection + transaction (convenience path, for call sites where audit is the only work in the transaction).
- `AppendAsync(ApprovalAuditEntry, NpgsqlConnection, NpgsqlTransaction, CancellationToken)` — writes within the caller's transaction, preserving same-tx atomicity with the state mutation it describes. Used by the 8 audit call sites in `PostgresApprovalPersistence`.

`ApprovalAuditEntry` carries typed correlation fields (`PlanId`, `ChallengeId?`, `GrantId?`, `ExecutionAttemptId?`) plus spine fields (`ActorSubject?`, `ActorClientId?`, `Outcome?`, `Reason?`) and a `Payload` object. Spine fields are populated directly from each call site's context — they are **not** extracted from the payload.

Event names come from `ApprovalConventions.AuditEvents` (`plan.created`, `challenge.approved`, etc.). The four `execution.blocked` sub-causes are kept collided per ADR-0020; `reason` provides indexed disambiguation.

Rows are written to `approvals.audit_outbox`, which is created by `Migrations/0001-initial-approval-persistence.sql`. This migration was retrofitted in place (ADR-0020); drop the `approvals` schema before next startup if upgrading an existing local database.

See [InfraGate.AuditOutbox.Postgres README](../InfraGate.AuditOutbox.Postgres/README.md) for the hash-chain algorithm, migration runner details, and the chain-verification SQL recipe.
