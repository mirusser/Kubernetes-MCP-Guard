# Implementation Plan: File Logger for InfraGate.McpServer

## Context

The MCP server runs as a child process of the gateway via `StdioClientTransport`. The MCP SDK uses stdin/stdout for the protocol and silently discards the child's stderr. All `logger.LogError(...)` calls (e.g. in `GetStatusAsync`, `GetEventsAsync`) do emit to stderr, but nobody reads that pipe. The fix is a StreamWriter-based file logger, opt-in via env var `K8S_MCP_LOG_PATH`, with zero new NuGet dependencies.

---

## Architecture Decisions

- **Opt-in via env var**: No `K8S_MCP_LOG_PATH` set → no file created, no change in behaviour. Set it to a path (e.g. `/tmp/mcp-server.log`) → logs written there.
- **StreamWriter only**: `AutoFlush = true`, append mode, no buffering. No Serilog or third-party packages.
- **Provider + logger in one file**: They are tightly coupled implementation details — acceptable per code-standards.
- **LogPath read from `K8SMcpOptions`**: Consistent with the existing `K8S_MCP_*` env var pattern.
- **Directory auto-created**: If the path is `/data/logs/mcp-server.log` and `/data/logs/` doesn't exist, create it. This is a realistic failure mode, not speculative.

---

## Tasks

### Task 1: Add `LogPath` to `K8SMcpOptions`

**File:** `src/InfraGate.McpServer/K8SMcpOptions.cs`

Add a `LogPath` property read from the `K8S_MCP_LOG_PATH` environment variable inside `FromEnvironment()`. No validation needed — an invalid path will surface as an exception at startup when the `StreamWriter` is created.

**Acceptance criteria:**
- [ ] `K8SMcpOptions` exposes `string? LogPath { get; init; }`
- [ ] `FromEnvironment()` populates it from `Environment.GetEnvironmentVariable("K8S_MCP_LOG_PATH")`
- [ ] Env var name is a `private const string` in `K8SMcpOptions`, not an inline literal

---

### Task 2: Create `StreamWriterLoggerProvider.cs`

**File:** `src/InfraGate.McpServer/StreamWriterLoggerProvider.cs` *(new)*

Two `internal sealed` types in one file:

**`StreamWriterLoggerProvider`** — implements `ILoggerProvider`:
- Constructor takes `string path`; creates parent directory if missing, opens `StreamWriter` in append mode with `AutoFlush = true`
- `CreateLogger(string categoryName)` returns a `StreamWriterLogger` sharing the same writer and a `Lock`
- `Dispose()` disposes the writer

**`StreamWriterLogger`** — implements `ILogger`:
- `IsEnabled(LogLevel)` → `logLevel != LogLevel.None`
- `BeginScope<TState>` → returns `null`
- `Log<TState>` formats: `[{utcNow:O}] [{logLevel,-11}] {categoryName}: {message}`, appends exception string on next line if present, writes under `lock`

Code-standards notes:
- File-scoped namespace
- No `_` prefix on fields
- `Lock` (.NET 9+, fine for net10.0)

**Acceptance criteria:**
- [ ] File compiles, no analyzer warnings
- [ ] Each log entry is one line (plus optional exception lines)
- [ ] Concurrent calls are safe (single `Lock` instance shared across logger instances from the same provider)

---

### Task 3: Register the provider in `Program.cs`

**File:** `src/InfraGate.McpServer/Program.cs`

Move `K8SMcpOptions.FromEnvironment()` call to before `builder.Logging` configuration (it's a pure env-read, safe to do early). Then:

```csharp
if (!string.IsNullOrWhiteSpace(mcpOptions.LogPath))
{
    builder.Logging.AddProvider(new StreamWriterLoggerProvider(mcpOptions.LogPath));
}
```

The rest of Program.cs stays as-is; `builder.Services.AddSingleton(mcpOptions)` replaces the current `builder.Services.AddSingleton(options)`.

**Acceptance criteria:**
- [ ] No file logger registered when `K8S_MCP_LOG_PATH` is unset
- [ ] File logger registered before `builder.Build()` so all startup logs are captured
- [ ] `K8SMcpOptions` instance reused (not fetched twice)

---

## Verification

1. **Build**: `dotnet build src/InfraGate.McpServer/InfraGate.McpServer.csproj` — no errors or warnings.
2. **Existing tests**: `dotnet test` — all pass (no behaviour changed for the common case).
3. **Manual smoke test** (local, no Docker needed):
   ```
   K8S_MCP_LOG_PATH=/tmp/mcp-server.log \
   K8S_MCP_ALLOWED_NAMESPACES=default \
   dotnet run --project src/InfraGate.McpServer
   ```
   Then check `/tmp/mcp-server.log` contains the startup log line.
4. **In Docker (mode-d)**: add to `mcp-gateway` env block in `deploy/mode-d/compose.yaml`:
   ```yaml
   K8S_MCP_LOG_PATH: /tmp/mcp-server.log
   ```
   Then `docker exec <container> cat /tmp/mcp-server.log` to see errors from failing tools.

---

## Files Modified

| File | Change |
|------|--------|
| `src/InfraGate.McpServer/K8SMcpOptions.cs` | Add `LogPath` property + env var const |
| `src/InfraGate.McpServer/StreamWriterLoggerProvider.cs` | **New** — provider + logger |
| `src/InfraGate.McpServer/Program.cs` | Register provider conditionally |
| `deploy/mode-d/compose.yaml` | *(optional, for immediate debugging)* Add `K8S_MCP_LOG_PATH` env var |
