# InfraGate.Observability

`InfraGate.Observability` owns the full telemetry pipeline for the InfraGate agent services (Observer and Planner). It provides structured Serilog logging **and** an OpenTelemetry tracing/metrics pipeline with a Serilog span bridge.

## Registration

Call both methods in host `Program.cs`:

```csharp
// 1. Serilog structured logging (console + optional file sink)
builder.AddInfraGateObservability(opt =>
{
    opt.WriteToConsole = true;
    opt.FilePath = "/logs/observer.json";  // optional
});

// 2. OTel TracerProvider + MeterProvider + Serilog bridge
builder.AddInfraGateTelemetry(opt =>
{
    opt.ServiceName = "infragate-observer";
    opt.ServiceVersion = "1.0.0";
    opt.MeterNames = [ObserverMetrics.MeterName];
});
```

## Contents

### Serilog pipeline (`AddInfraGateObservability`)

- `ObservabilityOptions` — `WriteToConsole`, `ConsoleToStandardError`, `FilePath`.
- `ObservabilityExtensions.AddInfraGateObservability` — wires Console sink (text) and optional File sink (compact JSON), with `TraceContextEnricher` to stamp `TraceId`/`SpanId` on every log line.
- `TraceContextEnricher` — `ILogEventEnricher` that adds `TraceId` and `SpanId` from `Activity.Current` when an OTel span is active.

### Telemetry pipeline (`AddInfraGateTelemetry`)

- `TelemetryOptions` — `ServiceName`, `ServiceVersion`, `MeterNames` (custom meters to register), `OtlpEndpoint` (optional override; env var `OTEL_EXPORTER_OTLP_ENDPOINT` is also read).
- `TelemetryExtensions.AddInfraGateTelemetry` — registers:
  - `TracerProvider`: framework sources (`Experimental.Microsoft.Agents.AI`, `Experimental.Microsoft.Extensions.AI`, `Microsoft.Agents.AI.Workflows`) + HttpClient instrumentation + `SerilogSpanProcessor`.
  - `MeterProvider`: same sources + Runtime instrumentation + caller `MeterNames`.
  - OTLP exporter (both tracer and meter) when `OtlpEndpoint` / `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- `SerilogSpanProcessor` — `BaseProcessor<Activity>` that converts each completed framework span into a structured Serilog log event (span name, operation, model, agent, token counts, duration, status, trace/span IDs). Runs at Debug level; filters to the three framework sources.
- `TelemetryConventions` — `internal` constants for source names and env-var keys; no magic strings in production code.

## Environment variables

| Variable | Effect |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Enables OTLP export (e.g. `http://localhost:4317`). If unset, only the Serilog bridge is active. |
| `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` | Set to `true` to include prompt/response content in spans. **Off by default** — do not enable in production without a data-handling review. |

## Boundaries

Depends on Serilog (`Serilog.Extensions.Hosting`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`, `Serilog.Formatting.Compact`) and OpenTelemetry (`OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`). Consumed by `InfraGate.Observer` and `InfraGate.Planner`. See ADR-0026 for design rationale.
