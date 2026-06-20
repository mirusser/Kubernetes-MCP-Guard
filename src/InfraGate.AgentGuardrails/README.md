# InfraGate.AgentGuardrails

`InfraGate.AgentGuardrails` is a shared guardrail module that owns the guardrail vocabulary, the tool-call guardrail middleware, the model-visible content guard, and the consolidated guardrail metric used by both the Observer and Planner agents. It is a dedicated deep seam — one `UseToolCallGuardrail(...)` call guards every agent; one `AgentGuardrailMetrics` names every guardrail outcome.

**Owns:** agent-layer tool-call guardrail, guardrail metric taxonomy, model-visible content guard seam

## Module Surface

- `AgentGuardrailConventions` — `const`/`static` names: meter name, instrument names, tag keys, reason values, placeholders.
- `AgentGuardrailPolicy` — `record` holding `IReadOnlySet<string> AllowedToolNames`.
- `AgentGuardrailMetrics` — instance type over the module's static `Meter`; methods `RecordToolBlocked(agentName, toolName, reason)`, `RecordDecision(outcome, reason)`, `RecordModelVisibleDecision(action, source, agentName, evaluationDurationMs)`, and `RecordModelVisibleDegraded(source, agentName)`. DI singleton.
- `GuardrailDecisionOutcome` — `Accepted | Rejected` enum.
- `ToolCallGuardrailExtensions.UseToolCallGuardrail(this AIAgentBuilder, policy, metrics, agentName)` — framework function-invocation middleware: allow → `next`; deny → record `tool_call.blocked` + return blocked result, skip `next`.
- `AgentGuardrailServiceCollectionExtensions` — `AddAgentGuardrails(this IServiceCollection)` registers `AgentGuardrailMetrics`; `AddAllowAllModelVisibleContentGuard(this IServiceCollection)` registers passthrough guard; `AddModelVisibleContentGuard(this IServiceCollection, options)` composes the AGT deterministic adapter inside `CompositeModelVisibleContentGuard`.

## Model-Visible Content Guard

The model-visible content guard (`IModelVisibleContentGuard`) inspects text before LLM ingestion. Gateway read-only tool output is already shaped as a `model_visible_tool_result` envelope; guards must treat any `untrusted.payload` value in that envelope as Kubernetes observation data, not as instructions. When a guard redacts, quarantines, or blocks an enveloped tool result, the agent middleware preserves trusted envelope metadata and replaces only `untrusted.payload`. Each guard returns a `ModelVisibleContentDecision` with one of four actions:

| Action | Effect |
|---|---|
| `Allow` | Original text passes through to the LLM unchanged. |
| `Redact` | Harmful patterns are replaced with safe placeholder text. |
| `Quarantine` | Suspicious content is replaced with a bounded placeholder; original content is not sent to the LLM. A SHA-256 digest is persisted for forensic audit. |
| `BlockModelIngestion` | Model-visible content is replaced with a bounded blocked placeholder before it can influence the LLM. A SHA-256 digest is persisted for forensic audit. |

Types:

- `IModelVisibleContentGuard` — evaluate content and return a decision.
- `IModelVisibleContentAudit` — persist decisions for blocked/quarantined content (forensic audit).
- `ModelVisibleContent` — the input content record (`Text`, `Source`, `AgentName`, `CorrelationId`, `ToolName`).
- `ModelVisibleContentDecision` — the guard decision (`Action`, `Text`, `Categories`, `Reason`, `Digest`).
- `ModelVisibleContentAction` — `Allow`, `Redact`, `Quarantine`, `BlockModelIngestion`.
- `ModelVisibleContentSource` — `ObserverSnapshot`, `PlannerAnomaly`, `AgentToolResult`.
- `ModelVisibleContentOptions` — configuration (`Enabled`, `MaximumInputCharacters`, `UnavailableBehavior`, etc.).
- `ModelVisibleContentUnavailableBehavior` — `FailClosed`, `DeterministicOnly`.
- `CompositeModelVisibleContentGuard` — chains multiple guards; strongest action wins; enforces `MaximumInputCharacters` size check.
- `AllowAllModelVisibleContentGuard` — passthrough guard for development/testing.
- `ModelVisibleContentGuardExtensions` — agent builder middleware that evaluates tool results and preserves trusted envelope metadata when replacing untrusted payloads.

Configuration section: `InfraGate:AgentGuardrails:ModelVisibleContent`. See [docs/configuration.md](../../docs/configuration.md).

The guards must be composable, non-throwing, and non-blocking. Guard failures are isolated — an audit write failure records a degraded metric but never suppresses the guard's decision. The `CompositeModelVisibleContentGuard` enforces a `MaximumInputCharacters` bound before adaptive evaluation, raising `Quarantine` for oversized input.

See [ADR-0027](../../docs/adr/0027-framework-tool-call-guardrail-and-hallucination-metrics.md) for the tool-call guardrail design.

## Guardrail Metric Taxonomy

All names live in `AgentGuardrailConventions`. Meter: `InfraGate.AgentGuardrails` (v1.0).

| Instrument | Type | Tags | Emitted by |
|---|---|---|---|
| `infragate.agentguardrails.tool_call.blocked` | `Counter<long>` | `agent.name`, `tool.name`, `guardrail.reason` | `ToolCallGuardrail` middleware (both agents) |
| `infragate.agentguardrails.decision` | `Counter<long>` | `guardrail.outcome` (`accepted`\|`rejected`), `guardrail.reason` | `ValidateExecutor` (Planner) |
| `infragate.agentguardrails.model_visible.decision` | `Counter<long>` | `agent.name`, `model_visible.source`, `model_visible.action` | `CompositeModelVisibleContentGuard` |
| `infragate.agentguardrails.model_visible.degraded` | `Counter<long>` | `agent.name`, `model_visible.source` | `CompositeModelVisibleContentGuard` (audit-persistence failure) |
| `infragate.agentguardrails.model_visible.evaluation_duration_ms` | `Histogram<double>` | — | `CompositeModelVisibleContentGuard` |

`guardrail.reason` values: `tool_not_allowed` (tool layer); `invalid_operation`, `invalid_arguments`, `dedupe_in_batch` (decision layer); `exceeded_maximum_input_characters` (content guard size check).

**Hallucination rate** (decision layer) = `rejected{reason∈{invalid_operation,invalid_arguments}}` / `(accepted+rejected)`; `dedupe_in_batch` is an operational drop, tagged distinctly so dashboards exclude it from the numerator.

## Wiring

Both the Observer and Planner register the guardrails in their `Program.cs`:

1. `services.AddAgentGuardrails()` registers `AgentGuardrailMetrics`.
2. An `AgentGuardrailPolicy` singleton is constructed from the service's read-only tool-name conventions.
3. `AgentGuardrailConventions.MeterName` is added to `AddInfraGateTelemetry` so the guardrail meter exports through the existing Serilog span bridge / opt-in OTLP.
4. `ToolCallingAgentFactory` automatically composes the guardrail into the shared agent builder chain when both `AgentGuardrailPolicy` and `AgentGuardrailMetrics` are provided.
5. `services.AddModelVisibleContentGuard(options)` composes the model-visible content guard from configuration.

## Design Decisions

See [ADR-0027](../../docs/adr/0027-framework-tool-call-guardrail-and-hallucination-metrics.md) for the full context: two-layer distinction (agent vs. workflow guardrail), deterministic-only (no LLM judge, refs ADR-0012), deep-module rationale, block-and-continue default, and the McpGateway as the ultimate runtime control.

## Verification

- Unit tests: `dotnet test tests/InfraGate.AgentGuardrails.Tests/InfraGate.AgentGuardrails.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
