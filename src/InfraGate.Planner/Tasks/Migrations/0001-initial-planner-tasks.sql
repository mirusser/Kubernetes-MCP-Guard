create schema if not exists planner_tasks;

create table planner_tasks.schema_migrations
(
    filename text primary key,
    checksum_sha256 text not null,
    applied_at_utc timestamptz not null default now()
);

-- One durable A2A task per remediation unit of work. The full AgentTask is persisted as JSON
-- (task_json) for round-trip fidelity; the broken-out columns support ListTasks filtering by
-- context, state, and status timestamp without parsing the JSON.
create table planner_tasks.agent_tasks
(
    task_id text primary key,
    context_id text not null,
    status_state text not null,
    status_timestamp timestamptz,
    task_json text not null,
    updated_at_utc timestamptz not null default now()
);

create index ix_agent_tasks_context_id on planner_tasks.agent_tasks (context_id);

create index ix_agent_tasks_status_state on planner_tasks.agent_tasks (status_state);
