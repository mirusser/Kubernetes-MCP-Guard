# InfraGate.Executor

`InfraGate.Executor` is a deterministic remediation agent that receives plan ids from the Planner, waits for each carried plan to receive an **Approval Grant**, and calls the gateway's `execute_approved_plan` tool after approval. It runs as an ASP.NET `WebApplication` on port 3005 by default and authenticates to the gateway with the Executor service identity.

**Owns:** waiting and calling approved execution only

## Runtime Flow

- `Program.cs` wires Serilog, `InfraGate.RuntimeSafety`, `InfraGate.ClientCredentials`, the Executor MCP client, the concurrency gate, the plan watcher, and the A2A handler.
- A2A endpoint `/a2a/executor` accepts synchronous messages carrying a plan id from the Planner and returns the execution outcome after the watch completes.
- `PlanWatcher` tracks one task per proposal, repeatedly calls `wait_for_plan_approval` until approval, terminal status, shutdown, or timeout, then calls `execute_approved_plan` for approved plans.
- Duplicate plan ids are suppressed by an in-memory dedupe store with a bounded capacity.
- The Executor never calls read-only tools and never creates Plan Envelopes.

## Important Contracts

- **Input contract:** A2A message text containing the plan id.
- **Allowed tools:** `wait_for_plan_approval` and `execute_approved_plan`.
- **Identity and scope:** default client id `infra-gate-executor`; default scope `mcp:tools.execute`.
- **Concurrency:** default cap 64 in-flight watched plans; excess A2A handoffs return a failed dispatch outcome.
- **Watch timeout:** default 3600 seconds; the watcher uses repeated short `wait_for_plan_approval` calls until the wall-clock timeout is reached.
- **Safety model:** the Executor only acts after the gateway reports an approved plan. The gateway still owns Approval Grant validation, digest checks, freshness checks, policy checks, and Single-Execution Plan enforcement.

See [ADR-0017](../../docs/adr/0017-two-process-planner-executor-split.md), [ADR-0018](../../docs/adr/0018-propose-plan-as-new-mcp-tool.md), and [ADR-0019](../../docs/adr/0019-operator-approval-policy.md) for the load-bearing design choices.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md).

## Verification

- Unit tests: `dotnet test tests/InfraGate.Executor.Tests/InfraGate.Executor.Tests.csproj`
- Integration tests: `dotnet test tests/InfraGate.Executor.IntegrationTests/InfraGate.Executor.IntegrationTests.csproj`
- Remediation E2E tests: `INFRA_GATE_RUN_REMEDIATION_E2E=1 dotnet test tests/InfraGate.Remediation.E2E.Tests/InfraGate.Remediation.E2E.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
