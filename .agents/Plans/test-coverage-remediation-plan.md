# Implementation Plan: Test Coverage Remediation for Commit `bbd6268`..`HEAD`

## Overview

The commit range introduces logging, exception-handling resilience, and infrastructure changes across the McpServer and McpGateway projects. Sonar reports ~60% coverage on new code. After analysis, I've identified **4 specific coverage gaps** — 1 entire new class plus 3 untested error-handling branches within modified methods.

## Architecture Decisions

- **Add tests to existing test projects only** — follow the existing `tests/<Project>.Tests/UnitTests/` layout
- **Use existing test infrastructure** (`TestKubernetesApi`, temp directories, `NullLogger`) — no new test utilities needed
- **For plan store failure injection**: use a temp *file* as `ApprovalRoot` instead of a directory — `EnsureDirectories()` will throw `IOException` when trying to create subdirectories inside a file, which is reliable across platforms
- **StreamWriterLogger tests**: use temp file paths, write and read back to verify formatting and locking

## Task List

### Phase 1: Foundation — `StreamWriterLoggerProvider` (highest impact, entirely new code)

- [ ] **Task 1**: Add `StreamWriterLoggerProviderTests.cs` to `tests/InfraGate.McpServer.Tests/UnitTests/`
  
  **Acceptance criteria:**
  - [ ] `CreateLogger_ReturnsStreamWriterLogger` — `StreamWriterLoggerProvider(string path)` creates a logger, returns non-null `ILogger`
  - [ ] `Constructor_CreatesDirectory_WhenDirectoryDoesNotExist` — given a path with a non-existent parent directory, `Directory.CreateDirectory` is called
  - [ ] `Log_WritesTimestampAndLevelAndCategoryAndMessage` — logging at `Information` level writes a line with `[timestamp] [Information] category: message`
  - [ ] `Log_WritesExceptionToString_WhenExceptionNotNull` — logging with an exception appends `exception.ToString()` on a second line
  - [ ] `Log_DoesNotAppendExceptionLine_WhenExceptionNull` — logging without an exception writes exactly one line
  - [ ] `Log_IsThreadSafe` — concurrent logging from multiple threads produces all lines without corruption
  - [ ] `Log_RespectsIsEnabled_WhenLogLevelNone` — `LogLevel.None` produces no output
  - [ ] `BeginScope_ReturnsNull` — `ILogger.BeginScope` returns `null`
  - [ ] `Dispose_DisposesStreamWriter` — calling `Dispose()` on the provider disposes the underlying writer
  - [ ] `IsEnabled_ReturnsTrueForAllLevelsExceptNone` — all log levels except `LogLevel.None` return `true`

  **Verification:**
  - [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter "FullyQualifiedName~StreamWriterLoggerProviderTests"` passes
  - [ ] All 210 existing tests still pass

  **Dependencies:** None

  **Files likely touched:**
  - `tests/InfraGate.McpServer.Tests/UnitTests/StreamWriterLoggerProviderTests.cs` (new)

  **Estimated scope:** Medium (1 new file)

---

### Phase 2: K8sManager error-handling branches

- [ ] **Task 2**: Test audit-write failure doesn't mask dry-run refusal in `CreateDryRunPlanAsync`
  
  **Acceptance criteria:**
  - [ ] `RequestApplyManifestAsync_WhenAuditWriteFails_StillReturnsDryRunRefusal` — when dry-run fails AND the audit store is unavailable (approval root is a temp file), the method still returns the dry-run refusal message (not the audit error)
  - [ ] The test verifies that no `PlanId:` appears in the output and no plan file is created

  **Verification:**
  - [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter "FullyQualifiedName~RequestApplyManifestAsync_WhenAuditWriteFails"` passes

  **Dependencies:** None (adds to `K8sManagerRequestTests.cs`)

  **Files likely touched:**
  - `tests/InfraGate.McpServer.Tests/UnitTests/K8sManagerRequestTests.cs`

  **Estimated scope:** Small (1-2 files, 1 new test method)

---

- [ ] **Task 3**: Test plan store failure in `CreateAndFormatPlanAsync`
  
  **Acceptance criteria:**
  - [ ] `RequestApplyManifestAsync_WhenPlanStoreUnavailable_ReturnsStoreErrorMessage` — when `CreatePlanAsync` throws (approach: use a temp file path as approval root so `EnsureDirectories` fails), the response contains `"Failed to create approval plan:"` and the exception message

  **Verification:**
  - [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter "FullyQualifiedName~WhenPlanStoreUnavailable"` passes

  **Dependencies:** None (adds to `K8sManagerRequestTests.cs`)

  **Files likely touched:**
  - `tests/InfraGate.McpServer.Tests/UnitTests/K8sManagerRequestTests.cs`

  **Estimated scope:** Small (1-2 files, 1 new test method)

---

### Phase 3: Gateway — `DownstreamMcpClient.CreateTransportOptions`

- [ ] **Task 4**: Test environment variable forwarding in `CreateTransportOptions`
  
  **Acceptance criteria:**
  - [ ] `CreateTransportOptions_ForwardsAllEnvironmentVariables` — the returned `StdioClientTransportOptions` contains all current process environment variables (non-null values)
  - [ ] `CreateTransportOptions_UsesAssemblyArguments_WhenDownstreamAssemblySet` — when `options.DownstreamAssembly` is set, `Arguments` contains only the assembly path
  - [ ] `CreateTransportOptions_UsesRunProjectArguments_WhenDownstreamAssemblyNotSet` — when `options.DownstreamAssembly` is whitespace, `Arguments` uses `run --project` format
  - [ ] `CreateTransportOptions_SetsWorkingDirectory` — `WorkingDirectory` matches `options.WorkingDirectory`
  - [ ] `CreateTransportOptions_SetsShutdownTimeout` — `ShutdownTimeout` is 10 seconds

  **Verification:**
  - [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "FullyQualifiedName~DownstreamMcpClientTests"` passes
  - [ ] All 138 existing gateway tests still pass

  **Dependencies:** None

  **Files likely touched:**
  - `tests/InfraGate.McpGateway.Tests/UnitTests/DownstreamMcpClientTests.cs` (new)

  **Estimated scope:** Small (1 new file, ~5 test methods)

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Temp-file-as-approval-root approach is fragile on some platforms | Low | `Directory.CreateDirectory` fails reliably on all platforms when a parent path is a regular file; tests use `Path.GetTempPath()` which is always writable |
| `DownstreamMcpClient` constructor requires `McpGatewayOptions` with valid paths | Low | Can pass minimal `McpGatewayOptions` with defaults or temp paths; the method under test (`CreateTransportOptions`) doesn't call `Path.Exists` on any path |
| Concurrent logging test could be flaky | Med | Use a fixed small number of threads (4-8) writing small fixed messages; assert line count equals expected count; use a short timeout |

## Checkpoint: Complete
- [ ] All 4 task acceptance criteria met
- [ ] `dotnet test` on both test projects passes with 0 failures
- [ ] New test count: ~20 additional test methods

---

**Total estimated new test methods:** ~20 across 3 test files (2 new, 1 modified)  
**Estimated coverage improvement:** The 4 untested areas account for roughly 30-40% of the ~2000 new/changed lines of C# code (most changed lines are logging additions to existing covered paths). Addressing these should bring new-code coverage from ~60% to ~80%+.
