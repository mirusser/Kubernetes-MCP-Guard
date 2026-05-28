-- Planner audit stream — initial schema (ADR-0020).
-- Per-component in-place migration: drop the planner schema and re-apply if upgrading an existing database.

CREATE SCHEMA IF NOT EXISTS planner;

CREATE TABLE planner.schema_migrations
(
    filename        text        primary key,
    checksum_sha256 text        not null,
    applied_at_utc  timestamptz not null default now()
);

CREATE TABLE planner.audit_outbox
(
    audit_sequence      bigserial   primary key,
    event_name          text        not null,
    occurred_at_utc     timestamptz not null,
    actor_subject       text,
    actor_client_id     text,
    outcome             text,
    reason              text,
    previous_event_hash text,
    event_hash          text        not null,
    payload_json_text   text        not null,
    published_at_utc    timestamptz,
    publish_attempts    int         not null default 0,
    last_publish_error  text,
    proposal_id         text,
    anomaly_id          text,
    plan_id             text
);

CREATE INDEX ix_planner_audit_outbox_anomaly
    ON planner.audit_outbox (anomaly_id, audit_sequence);

CREATE INDEX ix_planner_audit_outbox_plan
    ON planner.audit_outbox (plan_id, audit_sequence);

CREATE INDEX ix_planner_audit_outbox_unpublished
    ON planner.audit_outbox (published_at_utc)
    WHERE published_at_utc IS NULL;
