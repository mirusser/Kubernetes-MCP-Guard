alter table approvals.plan_envelopes
    add column policy_kind text not null default 'same-subject',
    add column operator_group text;

