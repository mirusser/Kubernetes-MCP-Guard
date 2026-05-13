# Implementation Plan: Test Coverage Remediation for Commit `bbd6268`..`HEAD`

## Overview

The commit range introduces logging, exception-handling resilience, and infrastructure changes across the McpServer and McpGateway projects. Sonar reports ~60% coverage on new code. After analysis, I've identified **4 specific coverage gaps** in McpServer/McpGateway unit tests — 1 entire new class plus 3 untested error-handling branches within modified methods.

### Separately addressed coverage (not in scope)

The Keycloak parity and PKCE auth-code work (`deploy/keycloak/infra-gate-realm.json`, `KeycloakIntegrationTests.cs`, realm normalization, DCR policy, smoke script, docs) added **11 integration tests** to `InfraGate.McpGateway.KeycloakTests` (up from 3) and updated the `SafetyE2EFixture` for Keycloak `26.6.1`. These cover real OIDC identity paths — DCR, PKCE, token claim validation, wrong-verifier rejection, and browser approval backchannel — through a Testcontainers Keycloak container. No new production C# classes were added in that work, so Sonar coverage for new production code is unaffected.

## Architecture Decisions

- **Add tests to existing test projects only** — follow the existing `tests/<Project>.Tests/UnitTests/` layout
- **Use existing test infrastructure** (`TestKubernetesApi`, temp directories, `NullLogger`) — no new test utilities needed
- **For plan store failure injection**: use a temp *file* as `ApprovalRoot` instead of a directory — `EnsureDirectories()` will throw `IOException` when trying to create subdirectories inside a file, which is reliable across platforms
- **StreamWriterLogger tests**: use temp file paths, write and read back to verify formatting and locking
- **Keycloak tests are out of scope**: the `KeycloakIntegrationTests` suite covers OIDC integration paths (DCR, PKCE, token claims, approval backchannel) through a Testcontainers container. The `SafetyE2EFixture` image bump (`26.2` → `26.6.1`) and client-ID switch (`mcp-client` → `mcp-smoke-client`) are infrastructure-only changes with existing E2E guard coverage. No new unit-test coverage is needed for either.

## Task List

### Phase 1: Foundation — `StreamWriterLoggerProvider` (highest impact, entirely new code)

- [x] **Task 1**: Add `StreamWriterLoggerProviderTests.cs` to `tests/InfraGate.McpServer.Tests/UnitTests/`
  
   **Acceptance criteria:**
   - [x] `CreateLogger_ReturnsStreamWriterLogger` — `StreamWriterLoggerProvider(string path)` creates a logger, returns non-null `ILogger`
   - [x] `Constructor_CreatesDirectory_WhenDirectoryDoesNotExist` — given a path with a non-existent parent directory, `Directory.CreateDirectory` is called
   - [x] `Log_WritesTimestampAndLevelAndCategoryAndMessage` — logging at `Information` level writes a line with `[timestamp] [Information] category: message`
   - [x] `Log_WritesExceptionToString_WhenExceptionNotNull` — logging with an exception appends `exception.ToString()` on a second line
   - [x] `Log_DoesNotAppendExceptionLine_WhenExceptionNull` — logging without an exception writes exactly one line
   - [x] `Log_IsThreadSafe` — concurrent logging from multiple threads produces all lines without corruption
   - [x] `Log_RespectsIsEnabled_WhenLogLevelNone` — `LogLevel.None` produces no output
   - [x] `BeginScope_ReturnsNull` — `ILogger.BeginScope` returns `null`
   - [x] `Dispose_DisposesStreamWriter` — calling `Dispose()` on the provider disposes the underlying writer
   - [x] `IsEnabled_ReturnsTrueForAllLevelsExceptNone` — all log levels except `LogLevel.None` return `true`
   
   **Added:** `Dispose_FlushesAndClosesWriter`, `IsEnabled_ReturnsFalse_WhenLogLevelNone`

   **Verification:**
   - [x] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter "FullyQualifiedName~StreamWriterLoggerProviderTests"` passes (16 tests, 0 failures)
   - [x] All 228 server tests still pass

  **Dependencies:** None

  **Files likely touched:**
  - `tests/InfraGate.McpServer.Tests/UnitTests/StreamWriterLoggerProviderTests.cs` (new)

  **Estimated scope:** Medium (1 new file)

---

### Phase 2: K8sManager error-handling branches

- [x] **Task 2**: Test audit-write failure doesn't mask dry-run refusal in `CreateDryRunPlanAsync`
  
   **Acceptance criteria:**
   - [x] `RequestApplyManifestAsync_WhenAuditWriteFails_StillReturnsDryRunRefusal` — when dry-run fails AND the audit store is unavailable (approval root is a temp file), the method still returns the dry-run refusal message (not the audit error)
   - [x] The test verifies that no `PlanId:` appears in the output and no plan file is created

   **Verification:**
   - [x] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter "FullyQualifiedName~RequestApplyManifestAsync_WhenAuditWriteFails"` passes

  **Dependencies:** None (adds to `K8sManagerRequestTests.cs`)

  **Files likely touched:**
  - `tests/InfraGate.McpServer.Tests/UnitTests/K8sManagerRequestTests.cs`

  **Estimated scope:** Small (1-2 files, 1 new test method)

---

- [x] **Task 3**: Test plan store failure in `CreateAndFormatPlanAsync`
  
   **Acceptance criteria:**
   - [x] `RequestApplyManifestAsync_WhenPlanStoreUnavailable_ReturnsStoreErrorMessage` — when `CreatePlanAsync` throws (approach: use a temp file path as approval root so `EnsureDirectories` fails), the response contains `"Failed to create approval plan:"` and the exception message

   **Verification:**
   - [x] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter "FullyQualifiedName~WhenPlanStoreUnavailable"` passes

  **Dependencies:** None (adds to `K8sManagerRequestTests.cs`)

  **Files likely touched:**
  - `tests/InfraGate.McpServer.Tests/UnitTests/K8sManagerRequestTests.cs`

  **Estimated scope:** Small (1-2 files, 1 new test method)

---

### Phase 3: Gateway — `DownstreamMcpClient.CreateTransportOptions`

- [x] **Task 4**: Test environment variable forwarding in `CreateTransportOptions`
  
   **Acceptance criteria:**
   - [x] `CreateTransportOptions_ForwardsAllEnvironmentVariables` — the returned `StdioClientTransportOptions` contains all current process environment variables (non-null values)
   - [x] `CreateTransportOptions_UsesAssemblyArguments_WhenDownstreamAssemblySet` — when `options.DownstreamAssembly` is set, `Arguments` contains only the assembly path
   - [x] `CreateTransportOptions_UsesRunProjectArguments_WhenDownstreamAssemblyNotSet` — when `options.DownstreamAssembly` is whitespace, `Arguments` uses `run --project` format
   - [x] `CreateTransportOptions_SetsWorkingDirectory` — `WorkingDirectory` matches `options.WorkingDirectory`
   - [x] `CreateTransportOptions_SetsShutdownTimeout` — `ShutdownTimeout` is 10 seconds
   - [x] `CreateTransportOptions_SetsNameAndCommand` — `Name` and `Command` match conventions

   **Verification:**
   - [x] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "FullyQualifiedName~DownstreamMcpClientTests"` passes (7 tests, 0 failures)
   - [x] All 145 gateway tests still pass

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
- [x] All 4 task acceptance criteria met
- [x] `dotnet test` on both test projects passes with 0 failures
- [x] New test count: 25 additional test methods (16 StreamWriterLogger, 7 DownstreamMcpClient, 2 K8sManager) across 3 test files

---

**Total new test methods:** 25 across 3 test files (2 new, 1 modified)  
**Actual results:** InfraGate.McpServer.Tests: 228 passed (was 210, +18). InfraGate.McpGateway.Tests: 145 passed (was 138, +7). Build: 0 warnings, 0 errors.  
**Estimated coverage improvement:** The 4 untested areas account for roughly 30-40% of the ~2000 new/changed lines of C# code (most changed lines are logging additions to existing covered paths). Addressing these should bring new-code coverage from ~60% to ~80%+.

**Repo-level note:** The Keycloak parity work added 11 integration tests (up from 3) in `KeycloakIntegrationTests.cs` and kept the existing 14 Safety E2E tests passing via the `SafetyE2EFixture` update. These integration/E2E tests do not affect Sonar new-code coverage because no production C# classes were added or changed in that work.
