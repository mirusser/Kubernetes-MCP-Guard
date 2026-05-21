# Debugging MCP Gateway ↔ Downstream Server Connection: Four Root Causes, One Silent SDK Gap

## Context

**InfraGate** is a Kubernetes-operations MCP gateway that sits between an MCP client (e.g., Claude Desktop) and a downstream MCP server subprocess. The gateway:

1. Accepts MCP connections from clients via **Streamable HTTP** (the current MCP transport spec).
2. Authenticates clients with Keycloak-issued JWTs.
3. Launches the downstream MCP server as a **stdio subprocess** and connects to it.
4. Forwards tool calls through, wrapping them in an approval/guardrail layer.

The downstream server subprocess is launched with a **bootstrap auth line** — a JWT written to the subprocess's `stdin` before any MCP JSON. The server reads and validates this token, then starts the normal MCP `initialize` handshake.

This article documents a multi-day debugging session that uncovered four distinct root causes preventing the gateway from establishing its downstream connection. Three of them came from our own code; one came from an underdocumented (and arguably broken) SDK behaviour.

---

## The Symptom

After a refactor, every MCP `initialize` request to the gateway resulted in:

```
HTTP/1.1 200 OK
Content-Type: text/event-stream
Mcp-Session-Id: <id>
```

…followed by silence. The TCP connection stayed open. After 5–35 seconds (depending on client timeout), the connection dropped with no data ever written to the SSE body.

The `tools/list` request was never reached. The downstream subprocess was never spawned. The gateway logs showed nothing between "Executing endpoint" and "Executed endpoint" — a gap of exactly however long the client waited before giving up.

---

## Architecture: The SDK's Streamable HTTP Transport

Understanding the fix requires understanding how the .NET MCP SDK's `StreamableHttpServerTransport` works.

### POST = request + response in one SSE stream

Unlike the legacy SSE transport (where requests go to `/message` and responses come back on a long-lived `/sse` GET), Streamable HTTP sends **both the request and its response in the same POST**:

```
POST /mcp
Content-Type: application/json
Accept: application/json, text/event-stream

{ "jsonrpc":"2.0", "id":1, "method":"initialize", "params":{ ... } }

--- response ---

HTTP/1.1 200 OK
Content-Type: text/event-stream

event: message
data: { "jsonrpc":"2.0", "id":1, "result":{ ... } }
```

The POST handler (`StreamableHttpHandler.HandlePostRequestAsync`) does this:

```csharp
// 1. Create or look up the session
var session = await GetOrCreateSessionAsync(context, message);

// 2. Acquire reference (starts session if first request)
await using var _ = await session.AcquireReferenceAsync(context.RequestAborted);

// 3. Set SSE response headers (200 OK, Content-Type: text/event-stream, etc.)
InitializeSseResponse(context);

// 4. Block until the MCP server sends back the response
var wroteResponse = await session.Transport.HandlePostRequestAsync(
    message, context.Response.Body, context.RequestAborted);
```

Step 4 calls into `StreamableHttpPostTransport.HandlePostAsync`, which:

```csharp
// Writes the incoming message to the internal Channel<JsonRpcMessage>
await parentTransport.MessageWriter.WriteAsync(message, cancellationToken);

// Then blocks here until the response is sent
await _httpResponseTcs.Task.WaitAsync(cancellationToken);
```

The `TaskCompletionSource` (`_httpResponseTcs`) is completed in `SendMessageAsync` — specifically when the `JsonRpcResponse` matching the pending request is written to the SSE stream:

```csharp
finally
{
    if ((message is JsonRpcResponse or JsonRpcError) &&
        ((JsonRpcMessageWithId)message).Id == _pendingRequest)
    {
        _finalResponseMessageSent = true;
        _httpResponseTcs.TrySetResult(true);
    }
}
```

**The critical dependency:** `SendMessageAsync` is only called by `McpServer.RunAsync`. If `RunAsync` is not running, the `initialize` response is never sent, the TCS never completes, and the POST handler hangs until the client cancels the request — producing exactly the symptom we saw.

### Session lifecycle and `RunSessionHandler`

When a new session is created, the SDK calls:

```csharp
var runSessionAsync = HttpServerTransportOptions.RunSessionHandler ?? RunSessionAsync;
session.ServerRunTask = runSessionAsync(context, server, session.SessionClosed);
```

`session.ServerRunTask` is **fire-and-forget** — it is never awaited during request handling. The default (`RunSessionAsync`) simply calls:

```csharp
internal static Task RunSessionAsync(HttpContext httpContext, McpServer session, CancellationToken requestAborted)
    => session.RunAsync(requestAborted);
```

If you override `RunSessionHandler`, **you take responsibility for calling `server.RunAsync(ct)`**. The SDK documentation warns: *"The HttpContext parameter … may not be usable after McpServer.RunAsync starts"* and *"Consider using ConfigureSessionOptions instead … with fewer known issues."* It does not say — anywhere in the public docs — that failing to call `RunAsync` silently breaks the entire session.

---

## Root Cause 1: Swapped Stream Arguments

`BootstrapStdioClientTransport` is our custom transport that writes an auth line to the subprocess before starting the MCP handshake. Inside `ConnectAsync`, it creates a `StreamClientTransport` wrapping the process's stdin/stdout:

**Before:**
```csharp
return new StreamClientTransport(serverOutput, serverInput, transportOptions);
//                               ^writing end  ^reading end
```

**The bug:** `StreamClientTransport(arg1, arg2)` semantics are:
- `arg1` = the stream to **write** outgoing messages to (i.e., the server's **stdin**)
- `arg2` = the stream to **read** incoming messages from (i.e., the server's **stdout**)

The arguments were inverted. When the transport tried to create a `StreamReader` over `serverOutput` (the write-only `PipeWriter`-backed stream), it threw:

```
System.ArgumentException: Stream was not readable.
```

This exception was caught by `BootstrapStdioClientTransport`'s own error handling and re-thrown as a connection failure — but since `GetClientAsync` is only called during `tools/list` (not during `initialize`), this error was never visible until the downstream connection was actually attempted. Root Cause 3 (below) was masking this — `tools/list` could never be reached because `initialize` was already broken.

**Fix:**
```csharp
return new StreamClientTransport(serverInput, serverOutput, transportOptions);
//                               ^stdin       ^stdout
```

**How it was found:** Writing an integration test for `BootstrapStdioClientTransport` with real `Pipe`-backed streams immediately reproduced `Stream was not readable`. The test confirmed the fix.

---

## Root Cause 2: Silent Subprocess stderr

`StdioClientTransportOptions` has a `StandardErrorLines` callback for capturing subprocess stderr. It was never set:

```csharp
return new StdioClientTransportOptions
{
    Name = ...,
    Command = ...,
    Arguments = arguments,
    // StandardErrorLines: not set — subprocess stderr silently discarded
};
```

Without this, any crash or validation failure logged by the downstream server subprocess disappeared. When the downstream server rejected the bootstrap JWT (Root Cause 4, below), it logged a detailed error to stderr — which was completely invisible.

**Fix:**
```csharp
StandardErrorLines = line => logger.LogWarning("[downstream-server stderr] {Line}", line)
```

Once this was in place, the actual JWT validation failure became immediately visible in the gateway logs.

---

## Root Cause 3: `RunSessionHandler` That Never Starts the Session (the SDK gap)

This was the primary blocker and the one that matters most for anyone using the .NET MCP SDK.

### What happened

A refactor added subscription tracking to the gateway. To hook into session start and end, `RunSessionHandler` was used:

```csharp
// Before — broken:
transportOptions.RunSessionHandler = async (httpContext, server, ct) =>
{
    var registry = httpContext.RequestServices.GetRequiredService<ISubscriptionRegistry>();
    var id = server.SessionId;
    if (id is not null) registry.RegisterSession(id, new McpServerSessionNotifier(server));
    try
    {
        await Task.Delay(Timeout.Infinite, ct);  // ← This is the bug
    }
    catch (OperationCanceledException) { }
    finally
    {
        if (id is not null) registry.RemoveSession(id);
    }
};
```

`Task.Delay(Timeout.Infinite, ct)` keeps the `ServerRunTask` alive (so the session isn't disposed prematurely) but **never starts the MCP message processing loop**. Without the loop running, nobody reads from the `Channel<JsonRpcMessage>` and nobody calls `SendMessageAsync` — so the `TaskCompletionSource` in `HandlePostAsync` never completes.

The session was created. The SSE headers were sent. The `initialize` message was written to the channel. And there it sat, unread.

### Why the SDK doesn't catch this

`session.ServerRunTask` is assigned synchronously and never awaited by the request handler. Exceptions in `ServerRunTask` are swallowed unless the session is explicitly disposed (which calls `await ServerRunTask` in a try/catch). There is no watchdog that says "the session task exited early" and no error propagated to the POST response.

The SDK XML docs contain this warning:

> *The HttpContext parameter comes from the request that initiated the session (e.g., the initialize request) and **may not be usable after McpServer.RunAsync starts**, since that request will have already completed. Consider using ConfigureSessionOptions instead, which provides access to the HttpContext of the initializing request with fewer known issues.*

This is a hint that `RunSessionHandler` has sharp edges, but it does not say: **if you don't call `server.RunAsync(ct)`, all MCP requests will silently hang forever**. That would be the critical thing to document. It should probably be a runtime assertion or a dedicated exception type, not a silent hang.

### What is missing from the SDK

The SDK could enforce the contract in several ways:

1. **Assert/throw in `HandlePostAsync`** if `ServerRunTask` is already completed with no result or faulted when the first message arrives.
2. **Document the requirement explicitly** in the `RunSessionHandler` summary: *"You MUST call `server.RunAsync(ct)` inside this handler. Failing to do so will cause all MCP requests to hang indefinitely."*
3. **Provide a convenience wrapper** `RunSessionAsync` that the user can call directly in their custom handler (the SDK already has it as an `internal static` — making it `public` would allow composition rather than replacement).
4. **Detect the pattern early**: if `ServerRunTask` completes before the first message is processed, log a critical warning.

### The fix

```csharp
transportOptions.RunSessionHandler = async (httpContext, server, ct) =>
{
    var registry = httpContext.RequestServices.GetRequiredService<ISubscriptionRegistry>();
    var id = server.SessionId;
    if (id is not null) registry.RegisterSession(id, new McpServerSessionNotifier(server));
    try
    {
        await server.RunAsync(ct).ConfigureAwait(false);  // ← starts the message loop
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        logger.LogError(ex, "RunSessionHandler: unexpected exception");
    }
    finally
    {
        if (id is not null) registry.RemoveSession(id);
    }
};
```

`server.RunAsync(ct)` is the session message processing loop. It reads from the internal channel, dispatches handlers, and calls `SendMessageAsync` with responses. It exits when `ct` is cancelled (session disposed) or the channel is completed (transport disposed). The `session.SessionClosed` token — not the HTTP request's `RequestAborted` — is the right token to pass here, because the session must outlive the individual POST request.

### The diagnostic approach that found it

The handler's first lines were pure diagnostics:

```csharp
var handlerLogger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
    .CreateLogger("InfraGate.McpGateway.SessionHandler");
handlerLogger.LogInformation("RunSessionHandler: started (session={SessionId})", server.SessionId);
```

These run synchronously (before the first `await`), so if the handler is invoked at all, this log appears before any async work. When the log appeared after a `--no-cache` rebuild, the handler was confirmed working. The 5ms `initialize` completion time confirmed the full flow.

---

## Root Cause 4: Keycloak JWT Issuer Mismatch

With the session loop fixed, `initialize` worked but `tools/list` returned `-32603`. The stderr forwarding from Root Cause 2 immediately surfaced the error:

```
[downstream-server stderr] Downstream token validation failed:
  IDX10205: Issuer validation failed.
  Issuer: 'http://keycloak:8080/realms/infra-gate'.
  Did not match: validationParameters.ValidIssuers:
  'http://127.0.0.1:3010/realms/infra-gate, http://127.0.0.1:3010/realms/infra-gate/'
```

### The cause

The local Docker Compose setup has two paths to Keycloak:

| Caller | Hostname | Port |
|--------|----------|------|
| Host (MCP clients, browser) | `127.0.0.1` | `3010` |
| Inside Docker (gateway, subprocess) | `keycloak` | `8080` |

When Keycloak issues a JWT, the `iss` (issuer) claim is set to the URL the token endpoint was called from. The gateway fetches its client-credentials token using `INFRA_GATE_DOWNSTREAM_AUTH_METADATA_ADDRESS=http://keycloak:8080/...`, so the issued JWT has:

```json
{ "iss": "http://keycloak:8080/realms/infra-gate" }
```

`DownstreamTokenValidator` builds its `ValidIssuers` from `INFRA_GATE_DOWNSTREAM_AUTH_AUTHORITY`:

```csharp
string issuer = options.Authority.TrimEnd('/');
var validIssuers = DistinctValues(issuer, issuer + "/");
```

`INFRA_GATE_DOWNSTREAM_AUTH_AUTHORITY` was set to `http://127.0.0.1:3010/realms/infra-gate` — the external host URL. Validation failed because `keycloak:8080 ≠ 127.0.0.1:3010`.

### The subtlety

The naming convention of `Authority` vs `MetadataAddress` does not make this obvious:

- `MetadataAddress` is for **JWKS discovery** (key material to verify the signature).
- `Authority` is used as the **expected issuer** in the token.

These two endpoints can be on different DNS names — but if they are, the issued JWTs will carry the issuer from whichever Keycloak URL was used to get the token, which may not match `Authority`. In a real production deployment both would be the same public URL, so this only bites you in local Docker setups.

### The fix

For all Docker-based profiles in `run-profiles.yaml`, both `downstreamAuth.authority` and `downstreamAuth.metadataAddress` must use the Docker-internal hostname:

```yaml
# Before (broken):
downstreamAuth:
  authority: http://127.0.0.1:3010/realms/infra-gate
  metadataAddress: http://keycloak:8080/realms/infra-gate/.well-known/openid-configuration

# After (fixed):
downstreamAuth:
  authority: http://keycloak:8080/realms/infra-gate
  metadataAddress: http://keycloak:8080/realms/infra-gate/.well-known/openid-configuration
```

The `identityProvider.authority` for inbound client tokens stays on `127.0.0.1:3010` — those tokens are fetched by external MCP clients through the host, so their `iss` is the external URL.

---

## What the SDK Does Not Document (Summary)

### 1. `RunSessionHandler` must call `server.RunAsync`

The experimental `RunSessionHandler` (marked `[Experimental("MCPEXP002")]`) takes over session management entirely. If you use it, you must call `server.RunAsync(ct)` inside it. There is no fallback, no assertion, no warning. The failure mode is a silent, indefinite hang on every MCP request.

**Recommended improvement:** The SDK should either throw `InvalidOperationException` when the first message arrives and `ServerRunTask` is already completed, or document this as a P0 requirement in the method's XML docs.

### 2. `StandardErrorLines` must be set explicitly

`StdioClientTransportOptions.StandardErrorLines` defaults to `null`, which silently drops all subprocess stderr. For any production use, this means server crashes and validation failures are invisible.

**Recommended practice:** Always set this callback, even if only to `line => logger.LogWarning(line)`.

### 3. `StreamClientTransport(stream1, stream2)` argument order is not obvious from the name

The constructor signature `StreamClientTransport(Stream writeStream, Stream readStream, ...)` follows the pattern *"first write, then read"* — which is the reverse of the intuitive mental model of a bidirectional pipe (where you think in terms of the remote endpoint: "their input = my output, their output = my input"). The parameter names in the SDK source are `serverInput` / `serverOutput` viewed **from the server's perspective** — not the client's. This is worth noting explicitly in any custom transport wrapper.

### 4. The `initialize` response goes on the POST body, not a GET SSE stream

If you are debugging a new Streamable HTTP transport integration and see `200 OK text/event-stream` with no data, the first thing to check is **not** the GET `/mcp` stream — it is the session message loop. The `initialize` response is written directly to the POST response body as an SSE event. No GET request is needed for initialization.

---

## Debugging Timeline and Techniques

### How `Task.Delay` was hiding everything

Because `Task.Delay(Timeout.Infinite, ct)` is not `server.RunAsync`, no downstream calls were ever made. The sequence of interest:

1. MCP client sends `initialize` POST → gateway creates session, fires `RunSessionHandler` task
2. `HandlePostAsync` writes `initialize` to the channel → waits on `_httpResponseTcs`
3. `RunAsync` is never called → nobody reads the channel → nobody calls `SendMessageAsync`
4. `_httpResponseTcs` never completes → `HandlePostAsync` hangs
5. Client times out → `context.RequestAborted` fires → `WaitAsync(cancellationToken)` throws OCE
6. POST handler returns with 200 headers already sent, zero body bytes written
7. `BootstrapStdioClientTransport.ConnectAsync()` is never called → its bugs are invisible

This meant Root Causes 1 and 4 were completely masked by Root Cause 3 throughout most of the investigation.

### The `--no-cache` rebuild requirement

Docker's layer cache uses file content hashes for `COPY` instructions. When uncommitted working-tree changes were the intended fix but the committed code was different, the regular `docker compose build` reused all cache layers (the committed code matched the previous build, and Docker has no concept of "staged but not committed"). Only `--no-cache` forces all layers to rebuild from the working tree.

**Key lesson:** When debugging a Docker container that should contain a code fix — always verify the image was built **after** the fix was written, and use `docker image inspect <id> --format '{{.Created}}'` to check the build timestamp against the file modification time.

### Diagnostic logging in `RunSessionHandler`

The synchronous portion of an `async` lambda runs on the caller's thread before the first `await`. This means log statements before the first `await` are **guaranteed to execute synchronously** when the handler is invoked:

```csharp
session.ServerRunTask = runSessionAsync(context, server, session.SessionClosed);
//                      ^starts here, runs synchronously until first await
```

Placing `LogInformation` calls before `await server.RunAsync(ct)` confirmed whether the handler was being called at all, whether the DI container was working, and whether the `SessionId` was populated — without needing to rebuild or add breakpoints.

### The `StandardErrorLines` bridge as a diagnostic tool

Once set, subprocess stderr appears directly in the parent process (gateway) log stream with timestamps and level formatting. This is infinitely more useful than trying to read a log file from a container that cannot be shelled into (chiseled images have no shell). The pattern:

```csharp
StandardErrorLines = line => logger.LogWarning("[downstream-server stderr] {Line}", line)
```

…produced this output when Root Cause 4 hit:

```
[15:10:17 WRN] [downstream-server stderr] [15:10:17 WRN] Downstream token validation failed:
  IDX10205: Issuer validation failed. Issuer: 'http://keycloak:8080/...'
```

A complete JWT validation error with stack context, visible in `docker logs`, from a subprocess inside a shell-less container. This saved hours.

---

## End State

After all four fixes, the full flow works:

```
curl → POST /mcp (initialize)
  → gateway: RunSessionHandler started, server.RunAsync running
  → gateway: initialize processed, response sent in 5ms
  → 200 OK SSE body: { "result": { "protocolVersion": "2025-03-26", ... } }

curl → POST /mcp (tools/list)
  → gateway: DownstreamMcpClient.GetClientAsync()
  → BootstrapStdioClientTransport.ConnectAsync()
      → process start: dotnet InfraGate.McpServer.dll (logged at Info)
      → bootstrap line written to stdin
      → downstream server: token validated (keycloak:8080 issuer matches)
      → downstream server: initialize handshake complete
  → gateway: tools/list forwarded, 22 tools returned
  → 200 OK SSE body: { "result": { "tools": [...] } }
```

`initialize` round-trip: ~5ms. `tools/list` (first call, cold subprocess start): ~230ms. Subsequent calls (subprocess cached): <10ms.

---

## Appendix: Files Changed

| File | Change |
|------|--------|
| `src/InfraGate.McpGateway/BootstrapStdioClientTransport.cs` | Swapped `serverInput`/`serverOutput` args; added diagnostic `LogInformation` at each bootstrap step |
| `src/InfraGate.McpGateway/DownstreamMcpClient.cs` | Added `StandardErrorLines` callback; passed `ILoggerFactory` to `BootstrapStdioClientTransport` |
| `src/InfraGate.McpGateway/Program.cs` | Changed `Task.Delay(Timeout.Infinite, ct)` → `server.RunAsync(ct)` in `RunSessionHandler`; added session lifecycle logging |
| `deploy/run-profiles.yaml` | Changed `downstreamAuth.authority` from `127.0.0.1:3010` → `keycloak:8080` for `local-compose`, `smoke-local`, `smoke-release` profiles |
| `deploy/local-oauth/release.env.example` | Same authority fix |
| `deploy/generated/local-compose.env` | Same authority fix (regenerated value) |
| `deploy/generated/smoke-local.env` | Same authority fix (regenerated value) |
| `tests/InfraGate.McpGateway.Tests/IntegrationTests/BootstrapStdioClientTransportTests.cs` | New integration test confirming correct stream ordering via real `Pipe`-backed streams |
