# InfraGate.Observability

`InfraGate.Observability` provides shared Serilog-based structured logging configuration for the MCP Gateway and MCP Server.

**Owns:** shared Serilog structured logging configuration

## Contents

- `ObservabilityOptions.cs` defines the `WriteToConsole`, `ConsoleToStandardError`, and `FilePath` configuration flags.
- `ObservabilityExtensions.cs` exposes `AddInfraGateObservability` on `IHostApplicationBuilder`, wiring the Console sink (text format, routed to stdout or stderr) and the File sink (structured JSON via `CompactJsonFormatter`).

## Boundaries

This project depends on Serilog (`Serilog.Extensions.Hosting`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`, `Serilog.Formatting.Compact`). It is a shared leaf module consumed by projects that need structured logging.
