create schema if not exists approvals;

create table approvals.schema_migrations
(
    filename text primary key,
    checksum_sha256 text not null,
    applied_at_utc timestamptz not null default now()
);

create table approvals.plan_envelopes
(
    plan_id text primary key,
    profile text not null,
    adapter_id text not null,
    operation text not null,
    target_namespace text not null,
    requester_subject text not null,
    requester_authentication_type text,
    created_at_utc timestamptz not null,
    valid_from_utc timestamptz not null,
    valid_until_utc timestamptz not null,
    intent_digest_algorithm text not null,
    intent_digest_canonicalization text not null,
    intent_digest_value text not null,
    review_digest_algorithm text not null,
    review_digest_canonicalization text not null,
    review_digest_value text not null,
    canonical_json_text text not null,
    canonical_sha256 text not null
);

create table approvals.approval_challenges
(
    challenge_id text primary key,
    plan_id text not null references approvals.plan_envelopes(plan_id),
    pending_plan_hash text not null,
    requester_subject text not null,
    requester_authentication_type text,
    created_at_utc timestamptz not null,
    expires_at_utc timestamptz not null,
    intent_digest_algorithm text not null,
    intent_digest_canonicalization text not null,
    intent_digest_value text not null,
    review_digest_algorithm text not null,
    review_digest_canonicalization text not null,
    review_digest_value text not null
);

create index ix_approval_challenges_plan_requester
    on approvals.approval_challenges (plan_id, requester_subject);

create table approvals.challenge_outcomes
(
    outcome_id text primary key,
    challenge_id text not null unique references approvals.approval_challenges(challenge_id),
    plan_id text not null references approvals.plan_envelopes(plan_id),
    status text not null,
    actor_subject text,
    decided_at_utc timestamptz not null,
    reason text,
    grant_id text
);

create table approvals.approval_grants
(
    grant_id text primary key,
    plan_id text not null references approvals.plan_envelopes(plan_id),
    requester_subject text not null,
    approver_subject text not null,
    source_challenge_id text not null references approvals.approval_challenges(challenge_id),
    source_outcome_id text not null unique references approvals.challenge_outcomes(outcome_id),
    intent_digest_algorithm text not null,
    intent_digest_canonicalization text not null,
    intent_digest_value text not null,
    review_digest_algorithm text not null,
    review_digest_canonicalization text not null,
    review_digest_value text not null,
    approval_policy_json_text text not null,
    execution_reuse_policy_json_text text not null,
    issued_at_utc timestamptz not null,
    expires_at_utc timestamptz not null
);

create unique index ux_approval_grants_single_plan_grant
    on approvals.approval_grants (plan_id);

create table approvals.execution_attempts
(
    attempt_id text primary key,
    plan_id text not null references approvals.plan_envelopes(plan_id),
    grant_id text not null references approvals.approval_grants(grant_id),
    started_at_utc timestamptz not null
);

create table approvals.execution_outcomes
(
    outcome_id text primary key,
    attempt_id text not null unique references approvals.execution_attempts(attempt_id),
    plan_id text not null references approvals.plan_envelopes(plan_id),
    status text not null check (status in ('blocked', 'failed', 'succeeded')),
    completed_at_utc timestamptz not null,
    message text not null,
    reason_code text,
    target_namespace text
);

create table approvals.plan_execution_claims
(
    plan_id text primary key references approvals.plan_envelopes(plan_id),
    attempt_id text not null unique references approvals.execution_attempts(attempt_id),
    claimed_at_utc timestamptz not null
);

create table approvals.applied_plans
(
    plan_id text primary key references approvals.plan_envelopes(plan_id),
    execution_attempt_id text not null unique references approvals.execution_attempts(attempt_id),
    execution_outcome_id text not null unique references approvals.execution_outcomes(outcome_id),
    grant_id text not null references approvals.approval_grants(grant_id),
    target_namespace text not null,
    applied_at_utc timestamptz not null
);

create table approvals.audit_events
(
    audit_sequence bigint generated always as identity primary key,
    event_name text not null,
    plan_id text,
    challenge_id text,
    grant_id text,
    execution_attempt_id text,
    occurred_at_utc timestamptz not null,
    payload_json_text text not null
);

create index ix_audit_events_plan_sequence
    on approvals.audit_events (plan_id, audit_sequence);
