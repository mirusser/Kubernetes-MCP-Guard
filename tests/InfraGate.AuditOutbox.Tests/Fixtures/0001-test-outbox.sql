-- Test fixture for InfraGate.AuditOutbox.Tests.
-- Creates the test_stream schema and audit_outbox table used by chain, lock, and canonicalization tests.

CREATE SCHEMA IF NOT EXISTS test_stream;

CREATE TABLE test_stream.schema_migrations
(
    filename        text        primary key,
    checksum_sha256 text        not null,
    applied_at_utc  timestamptz not null default now()
);

CREATE TABLE test_stream.audit_outbox
(
    audit_sequence     bigint generated always as identity primary key,
    event_name         text        not null,
    occurred_at_utc    timestamptz not null,
    actor_subject      text,
    actor_client_id    text,
    outcome            text,
    reason             text,
    previous_event_hash text,
    event_hash         text        not null,
    payload_json_text  text        not null,
    published_at_utc   timestamptz,
    publish_attempts   int         not null default 0,
    last_publish_error text,
    test_entity_id     text,
    plan_id            text,
    anomaly_id         text
);
