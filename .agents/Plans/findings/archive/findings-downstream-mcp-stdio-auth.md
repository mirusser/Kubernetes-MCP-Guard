# Findings: Downstream MCP stdio Auth — Connection Debugging

## Problem Statement

The MCP gateway (`InfraGate.McpGateway`) cannot establish a connection to the downstream MCP server (`InfraGate.McpServer`).

Symptom: Any MCP session to the gateway hangs indefinitely — the gateway returns `200 OK text/event-stream` but sends zero bytes on the SSE stream.

**STATUS: RESOLVED** — All four root causes identified and fixed. `initialize` completes in ~5ms, `tools/list` returns all 22 downstream tools.

---

## Root Cause Investigation

### Fix 1 — Stream argument ordering (confirmed correct, applied)

`BootstrapStdioClientTransport.ConnectStreamTransportAsync` was calling:
```csharp
new StreamClientTransport(serverOutput, serverInput, ...)
```
`StreamClientTransport(arg1, arg2)` semantics: **arg1 is the write end**, **arg2 is wrapped in StreamReader** (the read end).
The arguments were inverted — the non-readable stream was passed as arg2, causing `ArgumentException: Stream was not readable`.

**Fix:** Swapped to `new StreamClientTransport(serverInput, serverOutput, ...)`.
**Evidence:** Integration test `BootstrapStdioClientTransportTests` confirms correct stream ordering.
All 267 tests pass.

---

### Fix 2 — Silent stderr (applied, but not yet visible)

`DownstreamMcpClient.CreateTransportOptions()` never set `StandardErrorLines` on `StdioClientTransportOptions`. Any crash output from the server subprocess was silently discarded.

**Fix:** Added `StandardErrorLines = line => logger.LogWarning("[downstream-server stderr] {Line}", line)`.

**Also added** diagnostic `LogInformation` calls in `BootstrapStdioClientTransport.ConnectAsync()` for each bootstrap step (process start, bootstrap line write, stream connect).

**Status:** These logs haven't appeared yet — which means `BootstrapStdioClientTransport.ConnectAsync()` is never being called. See Root Cause 3 below.

---

### Root Cause 3 — Gateway never sends `initialize` response (likely the actual blocking issue)

**Observation:** The gateway accepts MCP connections (`200 OK text/event-stream`, `Mcp-Session-Id` header set) but NEVER sends any SSE data. This was confirmed with both `curl -N` and a Python `http.client` test — the socket stays open and empty until the client disconnects.

**Therefore:** `BootstrapStdioClientTransport.ConnectAsync()` is never called because `DownstreamMcpClient.GetClientAsync()` is only called from `ListToolsAsync`/`CallToolAsync`, which are only triggered by `list_tools`/`call_tool` MCP requests. Those requests can only happen AFTER the client receives the `initialize` response. But the gateway never sends `initialize` — so nothing downstream is ever triggered.

**When it started:** The `RunSessionHandler` was added in commit `4d8dd90` ("Approval Notification via MCP Resource Subscriptions", May 19), one day before the user-reported start commit `b93c265` (May 20).

**Hypothesis:** `RunSessionHandler` with `Task.Delay(Timeout.Infinite, ct)` blocks the MCP session from starting. The SDK documentation says the handler is for "running new MCP sessions **manually**" and notes it has "fewer known issues" compared to `ConfigureSessionOptions`. If `McpServer.RunAsync(ct)` must be called manually inside the handler to start the session, then `Task.Delay` never starts the session and the `initialize` response is never sent.

**Supporting evidence:**
- SDK XML docs: "Consider using `ConfigureSessionOptions` instead, which provides access to the HttpContext of the initializing request with **fewer known issues**."
- Gateway logs show `[Executed endpoint ... 200 ... text/event-stream Xms]` where `X` = exactly the curl timeout duration (5s, 10s, 35s). The endpoint ends only when the client disconnects — no data is ever flushed.
- The first request after a fresh container build (12:55:37, 305 bytes, 2.9s) likely also had 0 bytes — 2.9 seconds was the MCP client's own `initialize` timeout before it gave up and disconnected.

---

## Proposed Fix for Root Cause 3

Replace `Task.Delay(Timeout.Infinite, ct)` in the `RunSessionHandler` with `await server.RunAsync(ct)`:

```csharp
transportOptions.RunSessionHandler = async (httpContext, server, ct) =>
{
    var registry = httpContext.RequestServices.GetRequiredService<ISubscriptionRegistry>();
    var id = server.SessionId;
    if (id is not null) registry.RegisterSession(id, new McpServerSessionNotifier(server));
    try
    {
        await server.RunAsync(ct).ConfigureAwait(false);  // ← was Task.Delay(Timeout.Infinite, ct)
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
        if (id is not null) registry.RemoveSession(id);
    }
};
```

`McpServer.RunAsync(CancellationToken)` exists (confirmed via reflection on the SDK assembly).
This starts the actual MCP session processing loop, which will handle `initialize` and subsequent requests.

---

## Side Investigations

### `.mcp-logs/mcp-server.log` has no new entries since May 17
The server subprocess is never spawned (because the gateway never gets past `initialize`). The last healthy log entry from May 17 confirms the server subprocess starts, logs `InfraGate MCP Server started`, handles `initialize`, and then `list_tools`.

### Fix 4 — Keycloak issuer mismatch (root cause found and fixed)

When `BootstrapStdioClientTransport.ConnectAsync()` is called:
- The gateway fetches a token from Keycloak using `INFRA_GATE_DOWNSTREAM_AUTH_METADATA_ADDRESS=http://keycloak:8080/...`
- Keycloak sets the JWT `iss` claim to `http://keycloak:8080/realms/infra-gate`
- But `DownstreamTokenValidator` builds valid issuers from `INFRA_GATE_DOWNSTREAM_AUTH_AUTHORITY=http://127.0.0.1:3010/...`
- Result: `IDX10205: Issuer validation failed` — server exits, gateway gets "connection timed out"

**Fix:** Changed `INFRA_GATE_DOWNSTREAM_AUTH_AUTHORITY` to `http://keycloak:8080/realms/infra-gate` in:
- `deploy/run-profiles.yaml` (all three Docker-based `downstreamAuth.authority` entries: `local-compose`, `smoke-local`, `smoke-release`)
- `deploy/local-oauth/release.env.example`
- `deploy/generated/local-compose.env` and `deploy/generated/smoke-local.env`

**Rule:** For Docker-based profiles, both `downstreamAuth.authority` AND `downstreamAuth.metadataAddress` must use the Docker-internal `keycloak:8080` hostname. The `identityProvider.authority` for inbound client tokens stays on `127.0.0.1:3010` because those tokens are fetched by external clients.

### `DownstreamMcpClient` constructor change
Added `ILoggerFactory loggerFactory` parameter to pass through to `BootstrapStdioClientTransport`.
All 267 tests updated to pass `NullLoggerFactory.Instance` as the 4th argument.

---

## Quirks & Gotchas

1. **`PipeWriter.AsStream().WriteAsync()` does NOT auto-flush** — required explicit `FlushAsync()` in integration tests.

2. **`StreamClientTransport` framing** — uses **newline-delimited JSON** (each message = JSON + `\n`), NOT HTTP `Content-Length` headers. Integration test initially used wrong framing, causing 10s `TaskCanceledException`.

3. **`BootstrapStdioBootstrapGate` reads byte-by-byte** — to avoid `TextReader` read-ahead consuming the first MCP JSON message from stdin. This is correct but fragile to any buffering in the pipeline.

4. **Serilog default minimum level is `Information`** — `Debug` calls in `BootstrapStdioClientTransport` are invisible unless the Serilog config sets a lower minimum. Changed key bootstrap logs to `Information` to make them visible.

5. **Docker chiseled container** — no shell available; logs accessed via `docker logs` or volume-mounted file at `.mcp-logs/`.

6. **`McpClient.CreateAsync` 30s timeout** — when the downstream server exits without sending `initialize` response, the gateway's `McpClient.CreateAsync` hangs for 30 seconds before timing out with "connection timed out after 30000ms".

7. **`RunSessionHandler` requires `server.RunAsync(ct)`** — without calling it, the MCP session never processes any messages.
