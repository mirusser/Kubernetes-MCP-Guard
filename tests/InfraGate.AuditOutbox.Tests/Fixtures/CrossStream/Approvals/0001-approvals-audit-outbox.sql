-- Cross-stream forensic test fixture: minimal approvals schema with audit_outbox only.
-- The full approvals schema (in InfraGate.Approvals.Postgres) also includes plan_envelopes,
-- approval_challenges, approval_grants, etc., but the forensic query only needs audit_outbox.

CREATE SCHEMA IF NOT EXISTS approvals;

CREATE TABLE approvals.schema_migrations
(
    filename        text        primary key,
    checksum_sha256 text        not null,
    applied_at_utc  timestamptz not null default now()
);

CREATE TABLE approvals.audit_outbox
(
    audit_sequence       bigserial   primary key,
    event_name           text        not null,
    occurred_at_utc      timestamptz not null,
    actor_subject        text,
    actor_client_id      text,
    outcome              text,
    reason               text,
    previous_event_hash  text,
    event_hash           text        not null,
    payload_json_text    text        not null,
    published_at_utc     timestamptz,
    publish_attempts     int         not null default 0,
    last_publish_error   text,
    plan_id              text,
    challenge_id         text,
    grant_id             text,
    execution_attempt_id text
);
