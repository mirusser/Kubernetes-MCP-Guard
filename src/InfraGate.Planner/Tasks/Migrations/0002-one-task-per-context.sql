drop index if exists planner_tasks.ix_agent_tasks_context_id;

create unique index ux_agent_tasks_context_id on planner_tasks.agent_tasks (context_id);
