# InfraGate.AgentGuardrails

`InfraGate.AgentGuardrails` is a shared guardrail module that owns the guardrail vocabulary, the tool-call guardrail middleware, and the consolidated guardrail/hallucination metric used by both the Observer and Planner agents. It is a dedicated deep seam — one `UseToolCallGuardrail(...)` call guards every agent; one `AgentGuardrailMetrics` names every guardrail outcome.

**Owns:** agent-layer tool-call guardrail, guardrail metric taxonomy, decision-layer hallucination-rate metric

## Module Surface

- `AgentGuardrailConventions` — `const`/`static` names: meter name, instrument names, tag keys, reason values.
- `AgentGuardrailPolicy` — `record` holding `IReadOnlySet<string> AllowedToolNames`.
- `AgentGuardrailMetrics` — instance type over the module's static `Meter`; methods `RecordToolBlocked(agentName, toolName, reason)` and `RecordDecision(outcome, reason)`. DI singleton.
- `GuardrailDecisionOutcome` — `Accepted | Rejected` enum.
- `ToolCallGuardrailExtensions.UseToolCallGuardrail(this AIAgentBuilder, policy, metrics, agentName)` — framework function-invocation middleware: allow → `next`; deny → record `tool_call.blocked` + return blocked result, skip `next`.
- `AgentGuardrailServiceCollectionExtensions.AddAgentGuardrails(this IServiceCollection)` — registers `AgentGuardrailMetrics` as a singleton.

## Guardrail Metric Taxonomy

All names live in `AgentGuardrailConventions`. Meter: `InfraGate.AgentGuardrails` (v1.0).

| Instrument | Type | Tags | Emitted by |
|---|---|---|---|
| `infragate.agentguardrails.tool_call.blocked` | `Counter<long>` | `agent.name`, `tool.name`, `guardrail.reason` | `ToolCallGuardrail` middleware (both agents) |
| `infragate.agentguardrails.decision` | `Counter<long>` | `guardrail.outcome` (`accepted`\|`rejected`), `guardrail.reason` | `ValidateExecutor` (Planner) |

`guardrail.reason` values: `tool_not_allowed` (tool layer); `invalid_operation`, `invalid_arguments`, `dedupe_in_batch` (decision layer).

**Hallucination rate** (decision layer) = `rejected{reason∈{invalid_operation,invalid_arguments}}` / `(accepted+rejected)`; `dedupe_in_batch` is an operational drop, tagged distinctly so dashboards exclude it from the numerator.

## Wiring

Both the Observer and Planner register the guardrails in their `Program.cs`:

1. `services.AddAgentGuardrails()` registers `AgentGuardrailMetrics`.
2. An `AgentGuardrailPolicy` singleton is constructed from the service's read-only tool-name conventions.
3. `AgentGuardrailConventions.MeterName` is added to `AddInfraGateTelemetry` so the guardrail meter exports through the existing Serilog span bridge / opt-in OTLP.
4. `ToolCallingAgentFactory` automatically composes the guardrail into the shared agent builder chain when both `AgentGuardrailPolicy` and `AgentGuardrailMetrics` are provided.

## Design Decisions

See [ADR-0027](../../docs/adr/0027-framework-tool-call-guardrail-and-hallucination-metrics.md) for the full context: two-layer distinction (agent vs. workflow guardrail), deterministic-only (no LLM judge, refs ADR-0012), deep-module rationale, block-and-continue default, and the McpGateway as the ultimate runtime control.

## Verification

- Unit tests: `dotnet test tests/InfraGate.AgentGuardrails.Tests/InfraGate.AgentGuardrails.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
