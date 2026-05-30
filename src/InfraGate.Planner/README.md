# InfraGate.Planner

`InfraGate.Planner` is an LLM-driven remediation agent that receives **Anomaly Reports** from the Anomaly Observer, chooses a bounded v1 remediation operation, and calls the gateway's `propose_plan` tool to create an approval-pending **Plan Envelope**. It runs as an ASP.NET `WebApplication` on port 3004 by default and authenticates to the gateway with the Planner service identity.

**Owns:** bounded remediation proposal

## Runtime Flow

- `Program.cs` wires Serilog, `InfraGate.RuntimeSafety`, `InfraGate.ClientCredentials`, the Planner MCP client, and the `Microsoft.Extensions.AI` chat client.
- `POST /handoff/anomalies` accepts `AnomalyHandoffBatch` payloads from the Observer and queues them for asynchronous processing.
- `BatchProcessor` dequeues `AnomalyHandoffBatch` payloads and builds a per-anomaly **workflow graph** using `Microsoft.Agents.AI.Workflows.WorkflowBuilder`. The graph fans out from a `BatchIntakePassthroughExecutor` to N per-anomaly chains, each running five executors in sequence:
  1. `FilterExecutor` — drops resolved reports and unsupported `AnomalyKind`s; emits `proposal.skipped` audit for non-resolved drops.
  2. `DedupeGateExecutor` — skips anomalies with an already-active tracked plan; emits `proposal.skipped` audit.
  3. `DecideExecutor` — asks the LLM (via `ToolCallingAgentFactory`, using a system prompt rendered by `IPromptLibrary`) for a bounded remediation decision with a per-anomaly wall-clock cap. Returns `null` on timeout or unparseable output.
  4. `ValidateExecutor` — checks the operation type against the v1 allow-list, normalises arguments via `OperationArgumentValidator`, and deduplicates within-batch operation keys.
  5. `ProposeExecutor` — calls `propose_plan` via the MCP client; on success emits `propose_plan.succeeded` audit and yields a `RemediationProposal` as workflow output.
- Successful proposals are emitted as `RemediationProposalBatch` payloads through `IRemediationProposalSink`: logging is always on, JSON file output is opt-in, and HTTP handoff to the Executor is enabled when configured.
- The Planner may inspect the cluster through the gateway's read-only tools, but it never calls execution tools.

## Guardrails

The Planner is protected at two layers:

1. **Agent-layer tool-call guardrail** (from `InfraGate.AgentGuardrails`): `ToolCallingAgentFactory` composes a framework function-invocation middleware (`UseToolCallGuardrail`) enforcing an explicit allow-list of the 8 read-only tool names from `PlannerConventions.ToolNames` (excluding `propose_plan`, which is called deterministically by `ProposeExecutor`, never by the LLM). Any tool call outside this allow-list is blocked, not executed, and recorded as a `tool_call.blocked` guardrail metric.

2. **Workflow-layer decision validation** (`ValidateExecutor`): validates the LLM's chosen operation type against the v1 allow-list (`restart_deployment`, `scale_deployment`, `set_deployment_image`), normalizes arguments, and deduplicates in-batch operation keys. Every validation outcome — accepted, rejected (`invalid_operation`, `invalid_arguments`, `dedupe_in_batch`) — is recorded as a reason-tagged `infragate.agentguardrails.decision` metric, replacing the two formerly bespoke counter instruments.

Both layers feed the `InfraGate.AgentGuardrails` meter, registered in the Planner's telemetry pipeline. **Hallucination rate** = `decision{rejected,reason∈{invalid_operation,invalid_arguments}} / decision{accepted+rejected}`; `dedupe_in_batch` drops are excluded from the numerator.

## Important Contracts

- **Input contract:** `InfraGate.Observer.Contracts.AnomalyHandoffBatch`.
- **Output contract:** `InfraGate.Remediation.Contracts.RemediationProposalBatch`.
- **V1 operation menu:** `restart_deployment` with `name` and `namespace`; `scale_deployment` with `name`, `namespace`, and non-negative `replicas`; `set_deployment_image` with `name`, `namespace`, `container`, and `image`.
- **Execution Scope Guard:** The Planner is configured with a propose scope, limiting its discovery to `propose_plan` plus read-only inspection tools. Execution tools remain invisible.
- **Identity and scope:** default client id `infra-gate-planner`; default scope `mcp:tools.propose mcp:tools.readonly`.
- **Safety model:** the Planner creates Plan Envelopes with Operator Approval Policy through the gateway. It does not approve, grant, or execute plans.

See [ADR-0017](../../docs/adr/0017-two-process-planner-executor-split.md), [ADR-0018](../../docs/adr/0018-propose-plan-as-new-mcp-tool.md), and [ADR-0019](../../docs/adr/0019-operator-approval-policy.md) for the load-bearing design choices.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md).

## Audit Stream

`PlannerAuditOutbox` writes a tamper-evident hash chain to `planner.audit_outbox` (ADR-0020). The Planner's Audit Stream is independent of the Approval Authority's Audit Spine — it does not produce Audit Spine events and does not reference `InfraGate.Approvals` (enforced by architecture tests in `tests/InfraGate.Planner.Tests/UnitTests/Architecture/`).

Four audit-worthy events are defined in `PlannerAuditEvents`:

| Event name | When emitted |
|---|---|
| `handoff.received` | After auth validation, before the channel write; one row per batch |
| `proposal.skipped` | Anomaly is skipped (unsupported operation, dedupe hit, LLM declined, etc.) |
| `propose_plan.succeeded` | Successful `propose_plan` gateway call; carries `anomaly_id`, `plan_id` |
| `propose_plan.failed` | Gateway rejection or HTTP failure |

All emit uses the `AppendAsync(entry, ct)` convenience overload — Planner audit writes are not part of a larger state-mutation transaction.

The `planner` schema is created on startup by `PostgresAuditOutboxMigrationRunner` reading `Migrations/0001-initial-planner-audit.sql`. Connection string: `INFRA_GATE_PLANNER_AUDIT_CONNECTION_STRING`.

Cross-stream joins: `propose_plan.succeeded` rows carry `plan_id` which matches `plan.created` rows in `approvals.audit_outbox`, enabling the full Observer→Planner→Approvals forensic timeline. See [InfraGate.AuditOutbox.Postgres README](../InfraGate.AuditOutbox.Postgres/README.md).

## Verification

- Unit tests: `dotnet test tests/InfraGate.Planner.Tests/InfraGate.Planner.Tests.csproj`
- Integration tests: `dotnet test tests/InfraGate.Planner.IntegrationTests/InfraGate.Planner.IntegrationTests.csproj`
- Remediation E2E tests: `INFRA_GATE_RUN_REMEDIATION_E2E=1 dotnet test tests/InfraGate.Remediation.E2E.Tests/InfraGate.Remediation.E2E.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
