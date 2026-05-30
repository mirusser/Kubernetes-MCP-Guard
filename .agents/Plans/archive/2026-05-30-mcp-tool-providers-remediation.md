# Implementation Plan: MCP Tool Providers Remediation

## Overview
This plan remediates the gaps found during the verification of the `2026-05-30-agent-framework-point3-mcp-tool-providers` implementation. It addresses the missing `CallTool` scope enforcement, test coverage gaps, code standards violations (e.g. `var` for primitives, NSubstitute mocking, missing `sealed`), architectural flaws (single-implementation interface, JSON round-tripping), and the entirely missed documentation phase.

## Architecture Decisions
- **Retain `IAgentMcpToolset` Interface:** To avoid coupling the agents to the Gateway-specific implementation (`GatewayAgentMcpToolset`), we will keep the interface as an explicit abstraction for MCP connections.
- **Direct object graph traversal:** Remove the JSON serialization round-trip in `ProposeExecutor` by extracting the planId directly from `CallToolResult.Content` or parsing it more natively.
- **Centralize Scope Enforcement:** Update `CallToolAsyncCore` to use `ToolScopeCatalog` directly, unifying the source of truth for both `ListTools` and `CallTool`.

## Task List

### Phase 1: Architecture & Core Logic
- [x] **Task 1: Fix JSON Serialization round-trips**
  - **Description:** Update `ProposeExecutor` to avoid `JsonSerializer.Serialize(callResult)` round-trips when extracting the plan ID.
  - **Acceptance criteria:**
    - `ProposeExecutor` extracts `planId` directly without serializing the full result to a string.
  - **Verification:** Solution builds. `ProposeExecutorTests` pass.
  - **Files likely touched:** `ProposeExecutor.cs`.
  - **Estimated scope:** Small

- [x] **Task 2: Integrate `ToolScopeCatalog` in `CallToolAsyncCore`**
  - **Description:** Route `CallTool` scope enforcement through the new `ToolScopeCatalog` (Task 3 from original plan).
  - **Acceptance criteria:**
    - `GatewayToolDispatcher` or `ToolScopeGuard` uses `ToolScopeCatalog.RequiredScopesFor(toolName)` for `CallTool` enforcement.
  - **Verification:** `GatewayToolDispatcherTests` and `GuardedToolRunnerTests` pass.
  - **Files likely touched:** `GatewayToolDispatcher.cs`, `ToolScopeGuard.cs`.
  - **Estimated scope:** Small

### Checkpoint: Core Logic
- [x] Tests pass, builds clean.

### Phase 2: Code Standards & Test Hygiene
- [x] **Task 3: Apply `code-standards`**
  - **Description:** 
    - Convert `ToolScopeCatalog` sequential `if`s to a `switch` expression.
    - Change `AgentMcpOptions` from `record` to `sealed class`.
    - Fix primitive types in `SnapshotFetcher.cs` (`var text` -> `string text`), `Program.cs` (`var allowedNsResponse` -> `string allowedNsResponse`), and `GatewayAgentMcpToolsetTests.cs`.
  - **Acceptance criteria:**
    - No `var text` or `var result` for strings in `SnapshotFetcher.cs`, `Program.cs`, `ProposeExecutor.cs`, or `GatewayAgentMcpToolsetTests.cs`.
    - `AgentMcpOptions` is `public sealed class` instead of `record`.
    - `ToolScopeCatalog.GetSynthesizedScopes` uses `switch`.
  - **Verification:** Build succeeds.
  - **Files likely touched:** `SnapshotFetcher.cs`, `Program.cs` (Observer/Planner), `ProposeExecutor.cs`, `GatewayAgentMcpToolsetTests.cs`, `AgentMcpOptions.cs`, `ToolScopeCatalog.cs`.
  - **Estimated scope:** Medium

- [x] **Task 4: Fix test naming and remove NSubstitute**
  - **Description:** Rename tests in `GatewayAgentMcpToolsetTests` to include the State segment (`Method_State_ExpectedResult`). Remove `NSubstitute` from `ObservationCycleRunnerTests` and any other updated tests, using real concrete instances, test fakes, or local overrides.
  - **Acceptance criteria:**
    - `GetAgentToolsAsync_ReturnsOnlyReadOnlyHintedTools` -> `GetAgentToolsAsync_WhenX_ReturnsOnlyReadOnlyHintedTools`.
    - `NSubstitute` is not used for `GatewayAgentMcpToolset`.
  - **Verification:** `dotnet test` runs cleanly.
  - **Files likely touched:** `GatewayAgentMcpToolsetTests.cs`, `ObservationCycleRunnerTests.cs`.
  - **Estimated scope:** Medium

### Checkpoint: Code Standards
- [x] Tests pass, codebase matches standards.

### Phase 3: Test Coverage
- [x] **Task 5: Add Gateway `ListTools` and `ReadOnlyHint` tests**
  - **Description:** Add missing tests for `ListToolsAsync` scope filtering and `ToolDefinitionFactory` `ReadOnlyHint` preservation.
  - **Acceptance criteria:**
    - `GatewayToolDispatcherTests` has test cases verifying `mcp:tools.readonly` and `mcp:tools.propose` scopes limit visibility.
    - Test added to verify `CreateForwardedTool` preserves `ReadOnlyHint`.
  - **Verification:** `dotnet test tests/InfraGate.McpGateway.Tests/`
  - **Files likely touched:** `GatewayToolDispatcherTests.cs`, new tests for `ToolDefinitionFactory`.
  - **Estimated scope:** Medium

- [x] **Task 6: Add Planner `propose_plan` security test**
  - **Description:** Add an explicit test asserting the Planner's LLM toolset excludes the `propose_plan` tool.
  - **Acceptance criteria:**
    - `PlannerGatewayIntegrationTests` (or unit test equivalent) fails if `propose_plan` leaks into the LLM-visible tools.
  - **Verification:** `dotnet test tests/InfraGate.Planner.IntegrationTests/`
  - **Files likely touched:** `PlannerGatewayIntegrationTests.cs`.
  - **Estimated scope:** Small

### Checkpoint: Test Coverage
- [x] All new edge cases covered and passing.

### Phase 4: Documentation
- [x] **Task 7: Add AgentMcp README and ADR**
  - **Description:** Create `src/InfraGate.AgentMcp/README.md` and the next sequential ADR `docs/adr/0024-*.md` recording decisions D3+D4 (Scope is source of truth, ReadOnlyHint).
  - **Acceptance criteria:**
    - Both files exist and are accurate.
  - **Verification:** Markdown lint passes.
  - **Files likely touched:** `src/InfraGate.AgentMcp/README.md`, `docs/adr/0024-agent-mcp-scope-catalog.md`.
  - **Estimated scope:** Small

- [x] **Task 8: Update Context, Glossary, and Stale READMEs**
  - **Description:** Add "Agent MCP Toolset" to `CONTEXT.md` and `repo-onboarding` SKILL. Add `InfraGate.AgentMcp` to `AGENTS.md`. Correct the roadmap. Update Observer/Planner/Gateway READMEs to remove stale "hardcoded whitelist" references.
  - **Acceptance criteria:**
    - Glossary term exists.
    - `AGENTS.md` and `SKILL.md` link to the new README.
    - Roadmap correctly reflects framework limitations.
    - `src/InfraGate.Observer/README.md`, `src/InfraGate.Planner/README.md`, and `src/InfraGate.McpGateway/README.md` are accurate.
  - **Verification:** Manual review.
  - **Files likely touched:** `CONTEXT.md`, `AGENTS.md`, `.agents/skills/repo-onboarding/SKILL.md`, `src/InfraGate.Observer/README.md`, `src/InfraGate.Planner/README.md`, `src/InfraGate.McpGateway/README.md`, `.agents/Plans/Roadmap/2026-05-29-agent-framework-migration-roadmap.md`.
  - **Estimated scope:** Medium

### Checkpoint: Complete
- [x] All acceptance criteria met.
- [x] Ready for human review.

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| NSubstitute removal leads to complex integration test setup | Med | Use local test fakes inheriting from the concrete class, or `TestServer` for the MCP endpoint, as done elsewhere. |

## Open Questions
- None.
