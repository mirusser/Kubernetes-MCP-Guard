# 27. Framework Tool-Call Guardrail and Hallucination Metrics

Date: 2026-05-30

## Status

Accepted

## Context

Roadmap §5 (*Enforcing Framework-Level Guardrails*) has two layers that, read literally, sound like one feature:

1. *Implement the framework's Interceptors or Filters at the client level.* → an **agent-layer** control: framework middleware on the agent's tool calls.
2. *Before the Planner calls `propose_plan`, run a lightweight, deterministic validation step … to ensure the plan matches the `restart_deployment` / `scale_deployment` whitelist.* → a **workflow-layer** control, on the deterministic pipeline that calls `propose_plan`.

The deterministic validation in bullet 2 already existed. `ValidateExecutor` in the Planner workflow already rejects disallowed operation types, runs per-operation argument normalization, drops invalid decisions before `propose_plan` is ever called, and increments counters for rejection events. However the rejection counters (`infragate.planner.decision.invalid_operation`, `infragate.planner.decision.invalid_arguments`) were bespoke Planner-local meters with no connection to the agent layer, and the agent layer had no invocation-time guardrail at all — tool-call filtering happened only at toolset-selection time via `ReadOnlyHint`.

## Decision

### 1. Two-layer distinction (agent vs. workflow)

The tool-call guardrail and the deterministic validation gate live at two different layers and must not be conflated:

- **Agent layer:** framework function-invocation middleware (`AIAgentBuilder.Use(...)`) that enforces an explicit, caller-declared tool-name allow-list at invocation time. Fails closed: a disallowed call is not executed; the agent receives a blocked result. This is the home for the tool-level guardrail metric.
- **Workflow layer:** `ValidateExecutor` remains the deterministic decision gate. It checks the operation-type whitelist, normalizes arguments, deduplicates in-batch keys, and drops invalid decisions before `ProposeExecutor` calls `propose_plan`. Its metric emission is re-pointed to the shared guardrail vocabulary.

`propose_plan` is invoked deterministically by `ProposeExecutor` (`mcpClient.CallToolAsync(...)`), not chosen by the LLM. The Planner agent only sees read-only inspection tools. Therefore the two layers do not overlap.

### 2. Deterministic only; no LLM judge

Consistent with ADR-0012 (*hybrid-severity-llm-proposes-rules-win* — rules win) and the repo's simplicity-first principle. A secondary LLM judge would add token cost, latency, and another hallucination surface to a control whose job is to reduce hallucination risk. Deferred unless the rejection metrics later prove a need.

### 3. New `InfraGate.AgentGuardrails` module

Two real consumers (Observer and Planner) flow through the shared `ToolCallingAgentFactory`. The guardrail concept — interceptor, outcome vocabulary, and metric — is distinct from agent construction (`InfraGate.AgentLlm`) and from observability transport (`InfraGate.Observability`). A dedicated module concentrates that complexity: one `UseToolCallGuardrail(...)` call guards every agent; one `AgentGuardrailMetrics` names every guardrail outcome; all guardrail vocabulary lives in one place.

Module surface:
- `AgentGuardrailConventions` — `const`/`static` names: meter, instrument names, tag keys, reason values.
- `AgentGuardrailPolicy` — `record` holding `IReadOnlySet<string> AllowedToolNames`.
- `AgentGuardrailMetrics` — instance type over the module's static `Meter`; methods `RecordToolBlocked(agentName, toolName, reason)` and `RecordDecision(outcome, reason)`. DI singleton.
- `ToolCallGuardrailExtensions.UseToolCallGuardrail(this AIAgentBuilder, ...)` — function-invocation middleware: allow → `next`; deny → record + blocked result, skip `next`.
- `AgentGuardrailServiceCollectionExtensions.AddAgentGuardrails(this IServiceCollection)` — registers `AgentGuardrailMetrics`.

### 4. Tool-call guardrail = framework function-invocation middleware

The mechanism is the decorator/`.Use()` pattern (agent-framework ADR 0007, *Agent Filtering Middleware*). The `AIAgentBuilder.Use(Func<..., ValueTask<object?>>)` overload is present in `Microsoft.Agents.AI` 1.8.0 (the version already in use). Our agent is a `ChatClientAgent`, which the framework requires for function-invocation middleware.

The guardrail enforces an explicit, caller-declared allow-list of tool names, not derived from the passed toolset, so a future toolset-filtering regression cannot silently widen it. It fails closed: a disallowed call returns a blocked-result string without calling `next()`; the agent continues (no `Terminate` set) so it can still finish with allowed tools.

The Observer's allow-set is `ObserverConventions.ToolNames` (8 read-only tool names). The Planner's allow-set is the read-only subset of `PlannerConventions.ToolNames` (the 8 `get_*`/`describe_*` names), excluding `propose_plan` (deterministic, never an agent tool).

### 5. Metric consolidation

Two shallow counters (`infragate.planner.decision.invalid_operation`, `infragate.planner.decision.invalid_arguments`) become one reason-tagged guardrail counter. The reason dimension concentrates the "rejection" concept that was previously smeared across two instruments.

Guardrail metric taxonomy:

| Instrument | Type | Tags | Emitted by |
|---|---|---|---|
| `infragate.agentguardrails.tool_call.blocked` | `Counter<long>` | `agent.name`, `tool.name`, `guardrail.reason` | `ToolCallGuardrail` middleware (both agents) |
| `infragate.agentguardrails.decision` | `Counter<long>` | `guardrail.outcome` (`accepted`\|`rejected`), `guardrail.reason` | `ValidateExecutor` (Planner) |

`guardrail.reason` values: `tool_not_allowed` (tool layer); `invalid_operation`, `invalid_arguments`, `dedupe_in_batch` (decision layer).

**Hallucination rate** (decision layer) = `rejected{reason∈{invalid_operation,invalid_arguments}}` / `(accepted+rejected)`; `dedupe_in_batch` is an operational drop, tagged distinctly so dashboards exclude it from the numerator.

### 6. Defense-in-depth, not the primary control

The McpGateway acts as the ultimate runtime guardrail (per the roadmap). §3 (ADR-0024) already filters the toolset by `ReadOnlyHint` at selection time. The tool-call guardrail is a second, invocation-time assertion of the same invariant and the natural home for the tool-level hallucination metric. The plan states this plainly rather than overselling a redundant control.

### 7. Default: block-and-continue

A blocked tool call does not set `context.Terminate` — the agent can still finish with allowed tools. This prevents a single guardrail block from aborting a legitimate multi-tool inspection run. The metric makes any block immediately visible.

## Consequences

- Both Observer and Planner agents run with the guardrail active via the shared `ToolCallingAgentFactory` seam. A disallowed tool call is blocked and metered.
- Decision-layer rejections (invalid operation, invalid arguments, in-batch dedupe) and tool-layer blocks all feed the `InfraGate.AgentGuardrails` meter. Hallucination rate is computable from the export.
- The two bespoke `invalid_*` Planner counters are removed. No duplicate counters remain.
- The `InfraGate.AgentGuardrails` meter is registered in both hosts' `AddInfraGateTelemetry` MeterNames, so it exports through the existing Serilog span bridge / opt-in OTLP.
- The module is a dedicated deep seam: one `UseToolCallGuardrail(...)` call guards every agent; one vocabulary names every outcome. No guardrail semantics scatter across projects.

## References

- Framework ADR: `docs/decisions/0007-agent-filtering-middleware.md` in microsoft/agent-framework
- ADR-0012: Hybrid severity — LLM proposes, rules win
- ADR-0022: Hidden agent seam vs BindAsExecutor
- ADR-0024: Agent MCP scope catalog (ReadOnlyHint filtering)
- ADR-0026: OpenTelemetry agent observability and Serilog bridge
