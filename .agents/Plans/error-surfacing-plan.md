# Implementation Plan: Meaningful Error Surfacing for MCP Tool Failures

## Context

When an MCP tool like `get_k8s_status` fails, the agent sees only `"An error occurred invoking 'get_k8s_status'"` — a generic string produced by the MCP SDK when a tool method throws. No stack trace, no HTTP status, no config hint. The real error is either:

- Logged to the gateway container's stdout (but the user never reads container logs mid-conversation), or
- Completely discarded because the downstream subprocess stderr is not piped back.

The previous session added logging (`LogError`) before re-throwing in `GuardedToolRunner`, but the re-throw is what triggers the MCP SDK's generic catch — so the log entry is there but the tool response to the agent is still useless.

**Goal:** Make tool failures visible *inside the tool response text* so the agent can explain the error to the user, and add appsettings logging config + targeted tests.

---

## Architecture Decisions

- **Return error text, don't re-throw** in `GuardedToolRunner` — the MCP SDK's `IsError=true` path buries messages; returning a descriptive string as the tool result is more useful to the agent.
- **No new abstractions** — extend the existing `FakeDownstream` pattern rather than introducing new test infrastructure.
- **appsettings.json over env vars** — more discoverable and follows .NET conventions; values can still be overridden via env vars.

---

## Task List

### Phase 1: Error Surfacing

#### Task 1 (XS): `GuardedToolRunner` — return error text instead of re-throwing

**File:** `src/InfraGate.McpGateway/GuardedToolRunner.cs`

Change the catch block (currently logs + re-throws) to log + return a formatted error string. The MCP SDK never sees the exception, so the agent gets the actual error in the tool response.

```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    logger.LogError(ex, "Downstream call to '{ToolName}' threw an exception", toolName);
    return $"Tool call failed: {ex.GetType().Name}: {ex.Message}";
}
```

**Acceptance criteria:**
- [ ] When downstream throws, the tool result text contains the exception type and message.
- [ ] The agent can read and relay the error to the user.
- [ ] No unhandled exception reaches the MCP SDK.

---

#### Task 2 (S): Add `appsettings.json` for both services

**Files:**
- `src/InfraGate.McpGateway/appsettings.json` (new)
- `src/InfraGate.McpServer/appsettings.json` (new)

Gateway config — filter framework noise, keep InfraGate namespaces at Debug:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "InfraGate": "Debug"
    }
  }
}
```

Server config — same pattern; the server already routes all logs to stderr via `LogToStandardErrorThreshold`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "k8s": "Warning",
      "InfraGate": "Debug"
    }
  }
}
```

Both files need `<CopyToOutputDirectory>Always</CopyToOutputDirectory>` in their `.csproj`.

**Acceptance criteria:**
- [ ] `docker logs <gateway>` shows InfraGate-namespaced messages at Debug.
- [ ] k8s client and ASP.NET framework logs stay at Warning or above.
- [ ] `.csproj` includes `<Content>` item with copy-always.

---

#### Task 3 (S): Log startup context in downstream server

**File:** `src/InfraGate.McpServer/Program.cs` (or `KubernetesConfigProvider.cs`)

Add a startup log line after DI resolves `IKubernetes` so that container logs show what config path was used and what namespaces are allowed. This makes it obvious when the server starts without a valid kubeconfig.

In `Program.cs`, after `builder.Build()`, resolve and log:
```csharp
var app = builder.Build();
var k8sOptions = app.Services.GetRequiredService<K8SMcpOptions>();
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();
appLogger.LogInformation(
    "InfraGate MCP Server started. KubeConfig={KubeConfig}, AllowedNamespaces={AllowedNamespaces}",
    k8sOptions.KubeConfig ?? "(default)",
    string.Join(",", k8sOptions.AllowedNamespaces));
```

Also add a try/catch in `KubernetesConfigProvider.Create()` around the config factory call so it logs the path and error before throwing:
```csharp
try
{
    return kubeConfigFactory(kubeConfig);
}
catch (Exception ex)
{
    // Logger not available here (pre-DI), so throw with enriched message
    throw new InvalidOperationException(
        $"Failed to load kubeconfig from '{kubeConfig}': {ex.Message}", ex);
}
```

**Acceptance criteria:**
- [ ] Startup log shows `KubeConfig=` and `AllowedNamespaces=` on container start.
- [ ] Failed kubeconfig load throws with the file path in the message (visible to the agent via Task 1's error text).

---

### Phase 2: Tests

#### Task 4 (S): `GuardedToolRunner` — tests for exception and `IsError` paths

**File:** `tests/InfraGate.McpGateway.Tests/UnitTests/GuardedToolRunnerTests.cs`

Extend `FakeDownstream` with an optional `Exception` parameter so it can throw:
```csharp
private sealed class FakeDownstream(string response, Exception? error = null) : IDownstreamMcpClient
{
    public Task<string> CallToolAsync(string toolName, ...)
    {
        ToolName = toolName;
        Arguments = arguments;
        if (error is not null) throw error;
        return Task.FromResult(response);
    }
}
```

Add two tests:
1. `CallAsync_WhenDownstreamThrows_ReturnsErrorTextWithExceptionMessage` — verifies the return value starts with `"Tool call failed:"` and contains the exception message.
2. `CallAsync_WhenDownstreamReturnsIsError_TextIsPassedThrough` — using the existing `IsError` path via the `DownstreamMcpClient` (or a stub that returns error text). Verifies error text flows back unmodified.

**Acceptance criteria:**
- [ ] Both new tests pass.
- [ ] All pre-existing `GuardedToolRunnerTests` still pass.

---

#### Task 5 (S): `K8sManager` observability — tests for K8s API error responses

**File:** `tests/InfraGate.McpServer.Tests/UnitTests/K8sManagerStatusTests.cs` (and/or `K8sManagerObservabilityTests.cs`)

Add tests using `TestKubernetesApi` that return error status codes and verify the formatted error string comes back (not an exception):

1. `GetStatusAsync_WhenKubernetesApiReturns500_ReturnsFormattedError` — api returns 500, result contains `"Status read failed"`.
2. `GetK8sEventsAsync_WhenApiReturnsNotFound_ReturnsFormattedError` — api returns 404, result contains `"Event read failed"`.
3. `GetPodLogsAsync_WhenApiReturnsNotFound_ReturnsFormattedError` — api returns 404.

Pattern is already established in `K8sManagerRequestTests.cs` — use `TestKubernetesApi` with `TestResponse.Json(StatusJson("InternalError", 500), statusCode: 500)`.

**Acceptance criteria:**
- [ ] 3 new tests added and pass.
- [ ] Error text contains the operation prefix and status code.
- [ ] No exception propagates out of the method under test.

---

### Checkpoint: After All Tasks

- [ ] `dotnet test` passes (all units, no integration)
- [ ] Gateway returns a descriptive error string when downstream fails instead of the generic MCP SDK message
- [ ] Container logs show startup context and InfraGate-namespaced debug messages

---

## Files Touched

| File | Change |
|------|--------|
| `src/InfraGate.McpGateway/GuardedToolRunner.cs` | Catch → return error text |
| `src/InfraGate.McpGateway/appsettings.json` | New — log level config |
| `src/InfraGate.McpGateway/InfraGate.McpGateway.csproj` | Add appsettings content item |
| `src/InfraGate.McpServer/appsettings.json` | New — log level config |
| `src/InfraGate.McpServer/InfraGate.McpServer.csproj` | Add appsettings content item |
| `src/InfraGate.McpServer/Program.cs` | Startup log after build |
| `src/InfraGate.McpServer/KubernetesConfigProvider.cs` | Enrich exception message |
| `tests/InfraGate.McpGateway.Tests/UnitTests/GuardedToolRunnerTests.cs` | FakeDownstream + 2 new tests |
| `tests/InfraGate.McpServer.Tests/UnitTests/K8sManagerStatusTests.cs` | 3 new error-path tests |

---

## Verification

1. Run `dotnet test` — all unit tests pass.
2. Start Mode D (`scripts/setup-development-deploy.sh --mode-d` or compose) and call `get_k8s_status` from an MCP client with an invalid namespace — agent should see `"Status read failed: ..."` not the generic error.
3. Run gateway container with a deliberately broken kubeconfig — `docker logs <gateway>` shows the kubeconfig path in the error, agent tool response says `"Tool call failed: InvalidOperationException: Failed to load kubeconfig from '...'"`
4. Check `docker logs <gateway>` shows InfraGate-namespaced debug lines but not k8s or ASP.NET Info noise.
