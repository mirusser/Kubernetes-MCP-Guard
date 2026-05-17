# Implementation Plan: Implement Structured Logging with Serilog

## Overview
Extract a shared `InfraGate.Observability` module to unify logging using Serilog across the MCP Gateway and MCP Server. This ensures human-readable console output (directed to `stderr` in the server to avoid protocol corruption) and machine-readable structured JSON file output. 
The implementation will strictly follow a Test-Driven Development (TDD) approach, focusing on vertical slicing and tracer bullets.

## Architecture Decisions
- **New Project**: Extract observability configuration to `src/InfraGate.Observability` to keep `RuntimeSafety` lean and provide a clean seam for Serilog dependencies.
- **Sinks Strategy**: Use text format for the Console sink (`stdout` for Gateway, `stderr` for Server) and structured JSON (`CompactJsonFormatter`) for the File sink (Server).
- **Code Standards**: Follow project standards (file-scoped namespaces, sealed classes, explicit types unless obvious, one type per file, `GlobalUsings.cs`, name methods `Method_State_ExpectedResult` in tests).
- **TDD Workflow**: We will write tests against the public interface (`AddInfraGateObservability` extension method) *before* implementing the behavior, following the RED-GREEN-REFACTOR cycle.

## Task List

### Phase 1: Foundation (TDD Loop)

## Task 0: Save Plan to Repository
**Description:** As requested, save this approved plan to `.agents/Plans/2026-05-17-serilog-observability-plan.md`.
**Acceptance criteria:**
- [ ] Plan file is saved in the repository under `.agents/Plans/`.

## Task 1: Setup TDD Foundation
**Description:** Scaffold the projects `src/InfraGate.Observability` and `tests/InfraGate.Observability.Tests` to establish the test environment.
**Acceptance criteria:**
- [ ] Both projects created and added to `InfraGate.slnx`.
- [ ] Dependencies (`Serilog`, etc.) added.

## Task 2: TDD Cycle - Console Sink Configuration
**Description:** Use TDD to implement basic console logging configuration.
- **RED**: Write a test verifying that calling `AddInfraGateObservability` with `WriteToConsole = true` registers a logger factory without throwing.
- **GREEN**: Implement `ObservabilityOptions` and `AddInfraGateObservability` to configure the `Serilog.Sinks.Console` sink.
- **REFACTOR**: Ensure code standards are met.

## Task 3: TDD Cycle - Standard Error Configuration
**Description:** Use TDD to add standard error support for the console sink.
- **RED**: Write a test verifying that calling `AddInfraGateObservability` with `ConsoleToStandardError = true` configures the underlying logger appropriately.
- **GREEN**: Update the extension method to route the console sink to standard error when requested.

## Task 4: TDD Cycle - JSON File Sink Configuration
**Description:** Use TDD to add structured JSON file logging.
- **RED**: Write a test verifying that calling `AddInfraGateObservability` with a valid `FilePath` does not throw and registers the factory.
- **GREEN**: Update the extension method to add the `Serilog.Sinks.File` sink using `CompactJsonFormatter` when a path is provided.

### Checkpoint: Foundation
- [ ] All `InfraGate.Observability.Tests` pass (`dotnet test`).

### Phase 2: Gateway Integration
## Task 5: Integrate Serilog into McpGateway
**Description:** Update `InfraGate.McpGateway` to use the new observability extensions for standard console logging.
**Acceptance criteria:**
- [ ] `InfraGate.McpGateway.csproj` references `InfraGate.Observability`.
- [ ] `Program.cs` replaces `AddConsole` with `AddInfraGateObservability(opt => { opt.WriteToConsole = true; opt.ConsoleToStandardError = false; })`.
**Verification:**
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` passes.

### Phase 3: Server Integration
## Task 6: Integrate Serilog into McpServer
**Description:** Update `InfraGate.McpServer` to use the observability extension, configuring it to write to stderr and file (JSON), and remove the old custom file logger.
**Acceptance criteria:**
- [ ] `InfraGate.McpServer.csproj` references `InfraGate.Observability`.
- [ ] `StreamWriterLoggerProvider.cs` is deleted.
- [ ] `Program.cs` uses `AddInfraGateObservability` mapped to stderr and `mcpOptions.LogPath`.
**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj` passes.
- [ ] Manual test: Verify server still works over stdio via Codex CLI.

### Phase 4: Documentation
## Task 7: Verify and Update Documentation
**Description:** Apply `verify-readme-docs` skill to update relevant READMEs about the new structured logging capabilities.
**Acceptance criteria:**
- [ ] Audit `docs/configuration.md` and `docs/devs-readme.md` for references to `StreamWriterLoggerProvider` or old logging mechanisms.
- [ ] Apply minimal doc updates reflecting the transition to JSON file logging and Serilog.

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| Standard output corruption in Server | High | Enforce `ConsoleToStandardError = true` in the Server's configuration block. |