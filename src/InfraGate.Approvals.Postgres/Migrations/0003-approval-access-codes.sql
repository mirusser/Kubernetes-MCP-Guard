create table approvals.approval_access_codes
(
    code text primary key,
    challenge_id text not null references approvals.approval_challenges(challenge_id),
    expires_at_utc timestamptz not null,
    consumed_at_utc timestamptz
);

create index ix_approval_access_codes_challenge
    on approvals.approval_access_codes (challenge_id);
