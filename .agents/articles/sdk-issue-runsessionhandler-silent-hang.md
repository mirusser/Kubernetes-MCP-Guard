# GitHub Issue: `RunSessionHandler` silently hangs all MCP sessions when `server.RunAsync` is not called

> **Target repo:** https://github.com/modelcontextprotocol/csharp-sdk
> **Labels to apply:** `bug`, `documentation`, `ModelContextProtocol.AspNetCore`

---

## Title

`RunSessionHandler` (MCPEXP002): omitting `server.RunAsync(ct)` silently hangs all MCP requests indefinitely with no diagnostic

---

## Summary

`HttpServerTransportOptions.RunSessionHandler` is the experimental hook for running custom logic around MCP session lifetime. If an implementation of this handler does not call `server.RunAsync(ct)`, every subsequent MCP request (including `initialize`) silently hangs forever — the server returns `200 OK text/event-stream` SSE headers but never writes a single byte to the response body. There is no exception, no log warning, no runtime assertion, and no documentation that makes this contract explicit.

This is a sharp edge in an experimental API that is very easy to trip over. The failure mode is indistinguishable from a network issue and extremely difficult to diagnose.

---

## Package and version

- **Package:** `ModelContextProtocol.AspNetCore`
- **Version:** `1.3.0`
- **Target framework:** `.NET 10.0`

---

## Minimal reproduction

```csharp
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
#pragma warning disable MCPEXP002
        options.RunSessionHandler = async (httpContext, server, ct) =>
        {
            // --- Bug: this handler does NOT call server.RunAsync(ct) ---
            // Perhaps it was meant to run some setup logic before the session:
            await DoSomeSetupAsync(httpContext, server);

            // The intent was to "keep the session alive while the client is connected".
            // Task.Delay seems like a reasonable way to do that. It isn't.
            await Task.Delay(Timeout.Infinite, ct);
        };
#pragma warning restore MCPEXP002
    });
```

With the handler above:

1. Client sends `POST /mcp` with `initialize`.
2. Server returns `HTTP 200 OK`, `Content-Type: text/event-stream`, `Mcp-Session-Id: <id>`.
3. **No SSE events are ever written** to the response body.
4. The connection stays open until the client times out or disconnects.
5. No exception is thrown anywhere. No warning is logged anywhere.

The handler fires and `Task.Delay(Timeout.Infinite, ct)` keeps `session.ServerRunTask` alive indefinitely, which prevents session disposal. But no code path ever reads from the `Channel<JsonRpcMessage>` or calls `SendMessageAsync` — so `StreamableHttpPostTransport._httpResponseTcs` is never completed, and `HandlePostAsync` hangs on:

```csharp
// StreamableHttpPostTransport.HandlePostAsync (line ~91)
await _httpResponseTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
```

---

## Root cause analysis

### The hidden contract

`CreateSessionAsync` assigns the handler as a fire-and-forget task:

```csharp
// StreamableHttpHandler.CreateSessionAsync (~line 402)
#pragma warning disable MCPEXP002
var runSessionAsync = HttpServerTransportOptions.RunSessionHandler ?? RunSessionAsync;
#pragma warning restore MCPEXP002
session.ServerRunTask = runSessionAsync(context, server, session.SessionClosed);
```

The default fallback `RunSessionAsync` (line ~500) is:

```csharp
internal static Task RunSessionAsync(HttpContext httpContext, McpServer session, CancellationToken requestAborted)
    => session.RunAsync(requestAborted);
```

When a custom handler is provided, the SDK **implicitly transfers this contract** — the handler must call `server.RunAsync(ct)`. Nothing enforces or documents this.

### Why `Task.Delay` seems correct but isn't

The XML docs for `RunSessionHandler` say:

> *"This callback is useful for running logic before a session starts and after it completes."*

"Before a session starts" naturally leads to patterns like:

```csharp
await DoSetupAsync();
await Task.Delay(Timeout.Infinite, ct);  // "keep alive until ct fires"
```

Nothing in the docs clarifies that the "session" does not start unless `server.RunAsync(ct)` is explicitly called inside the handler.

### Why the hang is invisible

`session.ServerRunTask` is never awaited by the request handler path — only by `DisposeAsync`:

```csharp
// StreamableHttpSession.DisposeAsync (~line 136)
await transport.DisposeAsync();
await _disposeCts.CancelAsync();
await ServerRunTask;  // only reached during session teardown
```

So a faulted or incorrectly-behaving `ServerRunTask` is completely silent during normal request processing. The failure surface is the POST response body — which has its headers sent but its body never written.

### The `initialize` response path requires `RunAsync`

The MCP Streamable HTTP transport sends the `initialize` response inside the same POST body as the request. `HandlePostAsync` writes the incoming message to a bounded `Channel<JsonRpcMessage>` and then blocks on a `TaskCompletionSource`:

```
Client POST (initialize)
  → HandlePostAsync writes message to Channel
  → HandlePostAsync awaits _httpResponseTcs
        ← requires McpServer.RunAsync to be reading from Channel
        ← and calling SendMessageAsync with the JsonRpcResponse
  → _httpResponseTcs.TrySetResult(true)  ← this is what unblocks the POST
  → SSE event written to response body
  → Client receives initialize response
```

Without `RunAsync`, the channel is never drained and the TCS is never completed. The POST handler eventually exits only when the client's `CancellationToken` (request abort) fires — leaving the response body empty.

---

## Observed behaviour

```
HTTP 200 OK
Content-Type: text/event-stream
Mcp-Session-Id: <session-id>
Transfer-Encoding: chunked

[connection held open for N seconds, then closed by client timeout]
[zero bytes written to body]
```

Gateway/server logs show:

```
[INF] Request starting HTTP/1.1 POST /mcp — application/json 153
[INF] Executing endpoint 'MCP Streamable HTTP | HTTP: POST /mcp/'
...
[INF] Executed endpoint 'MCP Streamable HTTP | HTTP: POST /mcp/'
[INF] Request finished HTTP/1.1 POST /mcp — 200 null text/event-stream 8209.5ms
```

The 8.2-second duration is the client-side timeout. No error is logged anywhere.

---

## Expected behaviour

One or more of the following should happen:

**Option A — Runtime assertion / exception (preferred):**
If the first message is written to the internal channel but `ServerRunTask` has already completed (without having called `RunAsync`), throw a clear exception or log a critical-level warning, e.g.:

```
InvalidOperationException: RunSessionHandler completed without calling server.RunAsync(ct).
All MCP requests require the session message loop to be running. Either call server.RunAsync(ct)
inside RunSessionHandler, or use ConfigureSessionOptions for pre-session logic that does not
require manual session management.
```

**Option B — Detect task completion before first request is processed:**
After `session.ServerRunTask` completes (other than via cancellation), log a `LogCritical` or `LogError` warning:

```
[CRT] RunSessionHandler task completed without the session message loop being started.
      Session ID: <id>. All subsequent requests for this session will hang indefinitely.
```

**Option C — Make `RunSessionAsync` public (lower barrier):**
Expose the default `RunSessionAsync` implementation as a public static method so that custom handlers can compose with it:

```csharp
// User code:
options.RunSessionHandler = async (ctx, server, ct) =>
{
    // pre-session setup
    await myRegistry.RegisterAsync(ctx, server);
    try
    {
        await HttpServerTransportOptions.RunSessionAsync(ctx, server, ct);  // <- public helper
    }
    finally
    {
        await myRegistry.UnregisterAsync(server.SessionId);
    }
};
```

This removes the need to know that `server.RunAsync(ct)` must be called and reduces the chance of re-introducing the bug.

**Option D — Documentation (minimum):**
Add to the `RunSessionHandler` XML docs and the `MCPEXP002` diagnostic page:

> ⚠️ **Required:** You must call `server.RunAsync(ct)` inside this handler. Failing to do so will cause all MCP requests for the session to hang indefinitely. There is no fallback — the SDK does not call `RunAsync` automatically when a custom handler is provided.

---

## Workaround

Call `server.RunAsync(ct)` explicitly inside the handler. Use `session.SessionClosed` (passed as `ct`) as the cancellation token — not `context.RequestAborted`, which is cancelled when the initiating HTTP request completes:

```csharp
options.RunSessionHandler = async (httpContext, server, ct) =>
{
    // Pre-session setup using httpContext. Note: httpContext may become unusable
    // once server.RunAsync starts and the initiating request completes.
    var myService = httpContext.RequestServices.GetRequiredService<IMyService>();
    myService.OnSessionStarted(server.SessionId);

    try
    {
        // REQUIRED: starts the MCP session message processing loop.
        // ct == session.SessionClosed, cancelled when the session is disposed.
        await server.RunAsync(ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        // Normal shutdown — client disconnected or session was explicitly disposed.
    }
    finally
    {
        myService.OnSessionEnded(server.SessionId);
    }
};
```

---

## Additional context

- The `MCPEXP002` diagnostic message says: *"Consider using ConfigureSessionOptions instead."* This is good advice for many cases, but `ConfigureSessionOptions` does not provide a post-session teardown hook, so `RunSessionHandler` is currently the only way to run cleanup logic after the session ends. Having both the setup-only path (`ConfigureSessionOptions`) and the session-wrapping path (`RunSessionHandler`) is useful — the issue is purely the missing contract enforcement.
- The `ExperimentalAttribute` appropriately signals that this API may change, but the hang-on-misuse failure mode is severe enough to warrant at minimum a runtime guard, even for an experimental API.

---

## Related issues

- **#1382** — *"RunSessionHandler is potentially dangerous"* (closed, led to PR #1383): Added the `[Experimental("MCPEXP002")]` attribute and the "Consider using `ConfigureSessionOptions` instead" diagnostic message. This is the prior art that correctly identified `RunSessionHandler` as a footgun and added the experimental warning — but it did not add runtime enforcement of the `server.RunAsync(ct)` contract. The present issue is the logical follow-on: the warning exists, but the silent hang remains.

- **#1416** — *"Alternative to RunSessionHandler for session tracking"* (open): Asks how to track session lifetime for registry/notification purposes without `RunSessionHandler`. The user in that issue is hitting the same fundamental need (session start/end hooks) from the opposite direction — they want an alternative *because* `RunSessionHandler` is dangerous. A public `RunSessionAsync` helper (Option C above) or a runtime assertion (Option A) would help both issues.

- **Companion feature request** — `McpClientOptions`: no way to inject `_meta` into the `initialize` request params. The use case that surfaced the present bug required a custom transport workaround precisely because `_meta` on `initialize` is not supported. Tracked as a separate issue.

