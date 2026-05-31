# 26. OpenTelemetry Agent Observability and Serilog Bridge

Date: 2026-05-30

## Status

Accepted

## Context

`InfraGate.Observer` and `InfraGate.Planner` had hand-rolled `System.Diagnostics.Metrics` meters (`ObserverMetrics`, `PlannerMetrics`), but those meters were never connected to any `MeterProvider` or exporter — they were effectively dark. The only LLM token-usage counter (`LlmTokensCounter`) was wired exclusively inside `CreateAnthropicClient`, which is unreachable (Anthropic throws at the call site; OpenRouter is the sole live provider). The running system emitted zero token telemetry on its active path.

The Microsoft Agent Framework 1.8.0 ships built-in OpenTelemetry instrumentation following the GenAI semantic conventions (`gen_ai.client.token.usage`, `gen_ai.client.operation.duration`, `gen_ai.operation.name`, `gen_ai.agent.name`). Wiring this instrumentation requires connecting it to an OTel `TracerProvider`/`MeterProvider` and routing the resulting spans/metrics into the existing Serilog-based observability stack.

ADR-0022 established `ToolCallingAgentFactory` as the shared agent construction seam. This plan uses that seam to add a single `UseOpenTelemetry()` call that auto-wires both the agent and its underlying chat client.

## Decision

### 1. Single deep telemetry seam in `InfraGate.Observability`

`InfraGate.Observability` is extended from a pure Serilog-logging module into the single telemetry pipeline host. A new `AddInfraGateTelemetry(this IHostApplicationBuilder, Action<TelemetryOptions>)` extension method wires:

- An OTel `TracerProvider` registering the three framework source names (`Experimental.Microsoft.Agents.AI`, `Experimental.Microsoft.Extensions.AI`, `Microsoft.Agents.AI.Workflows`) plus HttpClient instrumentation.
- An OTel `MeterProvider` registering the same sources plus Runtime instrumentation and any caller-supplied meter names (e.g. `InfraGate.Observer`, `InfraGate.Planner`).
- A `SerilogSpanProcessor : BaseProcessor<Activity>` that converts each completed framework span into a structured Serilog log event carrying `gen_ai.*` token/latency fields — zero new infrastructure required.
- A `TraceContextEnricher : ILogEventEnricher` that stamps `TraceId`/`SpanId` from `Activity.Current` on every Serilog log line, correlating logs to spans.

### 2. Framework seams, not custom span emission

Agent-level telemetry is enabled by calling `UseOpenTelemetry()` in the shared `ToolCallingAgentFactory` (ADR-0022's seam). In framework 1.8.0 this call wraps the agent with `OpenTelemetryAgent`, which in turn auto-wires `OpenTelemetryChatClient` for the underlying chat client. One call emits both `invoke_agent` and `chat` spans, plus `gen_ai.client.token.usage` and `gen_ai.client.operation.duration` metrics. The return type is widened from `ChatClientAgent` to `AIAgent`; `BindAsExecutor` and `RunAsync` are defined on the `AIAgent` base type so all call sites remain unchanged.

Workflow-level telemetry is enabled by calling `WithOpenTelemetry()` before `.Build()` in both `ObservationCycleRunner.BuildWorkflow` and `BatchProcessor.BuildWorkflow`. This produces `workflow`, `executor.process`, and `message.send` spans on `Microsoft.Agents.AI.Workflows`, nesting agent spans under the executor that triggered them.

### 3. Hybrid export: Serilog bridge by default, OTLP optional

Default deployments need no additional infrastructure: `SerilogSpanProcessor` writes completed spans as structured log events to the existing console/file sinks. The full trace graph appears in the existing log output. When `OTEL_EXPORTER_OTLP_ENDPOINT` is set, the same `TracerProvider`/`MeterProvider` also exports via OTLP to any compatible backend (Aspire dashboard, Jaeger, Collector) with no code change.

### 4. Services distinguished by OTel Resource, not source name

`ToolCallingAgentFactory` is shared, so we do not thread a per-service source name through it. Agent/chat telemetry uses the default framework source `Experimental.Microsoft.Agents.AI`. Services are distinguished by the OTel Resource `service.name` attribute (set per host in each `Program.cs`) and by the `gen_ai.agent.name` span tag (already `observer-{ns}` / `planner-{id}`).

### 5. Sensitive data off by default

The framework reads `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` natively. Prompts, responses, tool arguments, and executor payloads are not captured unless this variable is set. Token counts, latency, model name, and agent name flow by default.

### 6. Dead token counter removed

With `gen_ai.client.token.usage` now flowing from the live OpenRouter path via `OpenTelemetryChatClient`, the hand-rolled `LlmTokensCounter` (wired only in the unreachable `CreateAnthropicClient`) is deleted from both `ObserverMetrics` and `PlannerMetrics`. Token tracking is consolidated in the framework instrumentation.

## Consequences

- The running Observer and Planner now emit `invoke_agent` → `chat` span chains with token usage and latency in their structured logs, correlated by `TraceId`/`SpanId`.
- Workflow executor spans (`executor.process`) nest above agent spans, providing a full `workflow → executor → invoke_agent → chat` trace graph.
- The existing Serilog console/file output is the primary telemetry output; no new infrastructure is required to observe token usage.
- Setting `OTEL_EXPORTER_OTLP_ENDPOINT` enables OTLP export to any compatible backend.
- The dead `LlmTokensCounter` wiring is removed, eliminating a misleading metric definition that never exported.
- Custom business metrics (`CycleCount`, `ToolCalls`, etc. in `ObserverMetrics`; `DecisionInvalidOperation`, etc. in `PlannerMetrics`) are now registered with the `MeterProvider` and will export when OTLP is configured.

## References

- Framework ADR: `docs/decisions/0003-agent-opentelemetry-instrumentation.md` in microsoft/agent-framework
- ADR-0022: Hidden agent seam vs BindAsExecutor
