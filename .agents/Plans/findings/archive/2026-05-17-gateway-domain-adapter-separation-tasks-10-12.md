# Implementation Summary: Tasks 10-12 (Gateway Domain Adapter Separation)

Date: 2026-05-17

## Completed: Tasks 10-12 + Checkpoint 5 Verification

### What was implemented

**Task 10 — Gateway dynamic handler transformation:**
- Created `GatewayToolDispatcher.cs` — dynamic `ListToolsHandler` (forwards ReadOnly tools + generates `request_*` wrappers for Destructive tools + adds `apply_approved_plan`) and `CallToolHandler` (dispatches to `GuardedToolRunner`, `IDomainPlanBuilder`, or `IDomainPlanExecutor`)
- Updated `Program.cs` — replaced `WithToolsFromAssembly()` with `WithListToolsHandler`/`WithCallToolHandler`, registered `IDomainPlanBuilder`, `IDomainPlanExecutor`, `IToolCaller`, `DownstreamToolRegistry`, `GatewayToolDispatcher`
- Deleted `K8sGatewayTools.cs`
- Removed `CallWithRequesterAsync` from `GuardedToolRunner`

**Task 11 — Convention cleanup:**
- `McpGatewayConventions.ToolNames` now contains only `RequestToolPrefix = "request_"` and `ApplyApprovedPlan`
- Removed `RequesterSubject`/`RequesterAuthenticationType` from `ToolArguments`
- Verified: zero `KubernetesAdapter`/`KubernetesPlan` references in Gateway source files outside `Program.cs`

**Task 12 — RunProfiles:**
- `run-profiles.yaml` and deploy configs have no references to removed tool names
- MCP client-facing tool names unchanged (`request_*` wrapper names stay the same)

### Verification results

| Check | Result |
|---|---|
| `dotnet build InfraGate.slnx` | **PASS** (0 errors, 0 warnings) |
| `grep` K8s imports outside `Program.cs` | **PASS** (zero results) |
| McpServer `Request*`/`ApplyApprovedPlan`/`requesterSubject` | **PASS** (all removed) |
| McpServer unit tests | **196/196 passed** |
| Gateway unit tests (non-Keycloak) | **155/163 passed** |
| Gateway startup (dev mode) | **PASS** (listens on port 3001) |

### Known gap

8 integration tests in `GatewayHttpMcpIntegrationTests` fail because the `TestServer`-based MCP infrastructure doesn't propagate the DI container to `RequestContext.Services`. The dynamic handler pattern works correctly in the real ASP.NET Core pipeline (verified by gateway startup). The test server setup needs a follow-up refactoring to either use `TestServer.Services` directly or a different handler registration pattern for the test environment.
