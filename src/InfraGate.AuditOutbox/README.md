# InfraGate.AuditOutbox

`InfraGate.AuditOutbox` is the generic audit-outbox engine for InfraGate. It defines the core interface, canonical row shape, stream names, lock conventions, and hash-chain helpers used by all per-component audit streams.

**Owns:** audit-outbox contracts and conventions (no Postgres dependency)

See [ADR-0020](../../docs/adr/0020-audit-outbox-architecture.md) for the load-bearing design choices and [CONTEXT.md](../../CONTEXT.md) for the **Audit Stream** glossary entry.

## Concepts

**Audit Stream** — a per-component, append-only audit record written transactionally with the state mutation it describes. Each stream owns its own tamper-evident hash chain. Cross-stream correlation is by IDs (`plan_id`, `anomaly_id`), not by a shared chain.

Three runtime components own an Audit Stream: the **Approval Authority** (`approvals`), the **Anomaly Observer** (`observer`), and the **Remediation Planner** (`planner`).

## Public Surface

### `AuditOutboxRow`

Canonical row shape. Every stream writes rows of this type.

| Property | Type | Notes |
|---|---|---|
| `EventName` | `string` | Dotted-lowercase event name, e.g. `anomaly.detected` |
| `OccurredAtUtc` | `DateTimeOffset` | Instant the event occurred |
| `ActorSubject` | `string?` | OAuth subject of the actor |
| `ActorClientId` | `string?` | OAuth client ID of the actor |
| `Outcome` | `string?` | `success`, `failure`, etc. |
| `Reason` | `string?` | Indexed disambiguation for collided event names |
| `PayloadJsonText` | `string` | Canonical JSON payload (adapter-specific fields live here) |
| `CorrelationColumns` | `IReadOnlyDictionary<string, object?>` | Per-stream correlation IDs (`plan_id`, `anomaly_id`, etc.) |

### `AuditOutboxConventions`

- `LockCategory` — `const int` sentinel for `pg_advisory_xact_lock` category; avoids collision with other advisory-lock callers.
- `StreamLockKey(schemaName)` — stable SHA-256-derived `int` lock key per stream.
- `BuildCanonicalInputObject(row)` — builds the `Dictionary<string, object?>` that is serialized to canonical JSON and hashed.
- `Streams` — `const string` names for the three built-in streams: `approvals`, `observer`, `planner`.
- `ColumnNames` — `const string` names for every top-level column; per-stream migrations reference the same names.

### `IAuditOutboxCore`

Internal interface. The Postgres implementation lives in `InfraGate.AuditOutbox.Postgres`. Per-stream wrappers (`ApprovalAuditOutbox`, `ObserverAuditOutbox`, `PlannerAuditOutbox`) call this interface — callers should not reference it directly.

## Per-Stream Wrappers

Each runtime component owns a thin wrapper that translates its strongly-typed entry record into `AuditOutboxRow`:

| Component | Wrapper | Project |
|---|---|---|
| Approval Authority | `ApprovalAuditOutbox` | `InfraGate.Approvals.Postgres` |
| Anomaly Observer | `ObserverAuditOutbox` | `InfraGate.Observer` |
| Remediation Planner | `PlannerAuditOutbox` | `InfraGate.Planner` |

Adding a fourth stream is one new wrapper in the new component's project. Observer and Planner must **not** reference `InfraGate.Approvals` or `InfraGate.Approvals.Postgres` — this constraint is enforced by project-reference assertion tests in `tests/InfraGate.Observer.Tests/` and `tests/InfraGate.Planner.Tests/`.
