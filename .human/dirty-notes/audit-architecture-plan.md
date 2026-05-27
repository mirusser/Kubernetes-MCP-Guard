outbox pattern with opentelemetry sink to immutable storage, that would be good

---

```text
Approval / execution transaction
  -> update operational Postgres state
  -> insert audit event into audit_outbox in same transaction

Outbox publisher
  -> reads undelivered audit events
  -> emits OpenTelemetry structured log/event
  -> ships via Collector to immutable/tamper-evident storage
  -> marks outbox event as delivered
```

Separation:

**Postgres approval tables** = current workflow state  
**Postgres outbox** = reliable handoff buffer  
**OpenTelemetry** = standard telemetry pipeline  
**Immutable storage** = audit evidence / compliance-style archive

Implementation in layers:

## Stage 1: Outbox table

Something like:

```sql
approval_audit_outbox
---------------------
id uuid primary key
occurred_at_utc timestamptz not null
event_type text not null
correlation_id text null
plan_id text null
challenge_id text null
grant_id text null
actor_subject text null
actor_client_id text null
tool_name text null
namespace text null
resource_kind text null
resource_name text null
intent_digest text null
review_digest text null
outcome text null
reason text null
payload_json jsonb not null
previous_event_hash text null
event_hash text null
published_at_utc timestamptz null
publish_attempts int not null default 0
last_publish_error text null
```

Then every approval/execute decision writes an event into the outbox **inside the same DB transaction** as the approval state change.

## Stage 2: Background publisher

A hosted service:

```text
AuditOutboxPublisher
```

Responsibilities:

* poll unpublished rows;
* emit structured OpenTelemetry logs/events;
* mark as published;
* retry with backoff;
* never block the approval path forever;
* expose metrics like:

  * `audit_outbox_pending`
  * `audit_outbox_publish_failures`
  * `audit_outbox_publish_latency`

## Stage 3: OpenTelemetry Collector

For local/dev, simply:

```text
App -> OTLP -> OpenTelemetry Collector -> file exporter / debug exporter
```

Later:

```text
App -> OTLP -> Collector -> S3-compatible storage / CloudWatch / Loki / SIEM
```

For a local reference implementation, even this is enough:

```text
Collector writes immutable-style JSONL files to a mounted directory
```

Then document that production deployments should use an actual immutable target.

## Stage 4: Tamper-evident chain

Add hash chaining to events:

```text
event_hash = SHA256(previous_event_hash + canonical_event_payload)
```

Then your audit story becomes:

> Audit events are written transactionally to an outbox, published through OpenTelemetry, and can optionally be chained with hashes before export to immutable storage.

That is honestly very good.

## README wording

```md
Approval state is stored in PostgreSQL. Security-relevant approval and execution events are written to a transactional audit outbox in the same database transaction as the state change. A background publisher emits those events through OpenTelemetry so they can be shipped to an external immutable or tamper-evident audit sink.

The local demo uses a development sink for inspection. Production deployments should configure an external retention-backed log store and monitor outbox delivery failures.
```

That sounds serious but not overclaiming.

## My recommendation

Implement **outbox first**, even before immutable storage.

The outbox alone already improves the architecture because it prevents the awkward split-brain problem:

> approval succeeded, but audit write failed

or

> audit says approved, but DB state did not commit

Then OpenTelemetry and immutable sink can be roadmap/follow-up work.
