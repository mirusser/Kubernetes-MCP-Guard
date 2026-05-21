# GitHub Issue: `McpClientOptions` has no way to inject `_meta` into the `initialize` request

> **Target repo:** https://github.com/modelcontextprotocol/csharp-sdk
> **Labels to apply:** `enhancement`, `ModelContextProtocol`

---

## Title

`McpClientOptions`: no way to set `_meta` on the `initialize` request — `InitializeRequestParams.Meta` is never populated

---

## Summary

`InitializeRequestParams` correctly inherits `Meta` (`JsonObject?`, `[JsonPropertyName("_meta")]`) from `RequestParams`, in full alignment with the MCP 2025-11-25 spec. However, `McpClientImpl.ConnectAsync` constructs `InitializeRequestParams` without ever reading from `McpClientOptions`, so there is no caller-accessible path to populate `Meta`. The type supports it; the public API does not expose it.

---

## Package and version

- **Package:** `ModelContextProtocol`
- **Version:** `1.3.0`
- **Target framework:** `.NET 10.0`

---

## Spec alignment

The MCP 2025-11-25 schema defines:

```typescript
export interface RequestParams {
  _meta?: {
    progressToken?: ProgressToken;
    [key: string]: unknown;
  };
}

export interface InitializeRequestParams extends RequestParams {
  protocolVersion: string;
  capabilities: ClientCapabilities;
  clientInfo: Implementation;
}
```

`InitializeRequestParams` inherits `_meta` from `RequestParams`. The C# SDK correctly models this:

```csharp
// RequestParams (SDK)
[JsonPropertyName("_meta")]
public JsonObject? Meta { get; set; }

// InitializeRequestParams inherits Meta from RequestParams
```

The type is spec-compliant. The gap is that no caller can reach it.

---

## Root cause

`McpClientImpl.ConnectAsync` (~line 546) hard-codes the params with no extension point:

```csharp
var initializeResponse = await SendRequestAsync(
    RequestMethods.Initialize,
    new InitializeRequestParams
    {
        ProtocolVersion = requestProtocol,
        Capabilities = _options.Capabilities ?? new ClientCapabilities(),
        ClientInfo = _options.ClientInfo ?? DefaultImplementation,
        // Meta is never set — McpClientOptions has no InitializeMeta property
    },
    ...);
```

`McpClientOptions` has no property that feeds into `InitializeRequestParams.Meta`.

---

## Impact

Any use case that requires passing bootstrap data or auth credentials in `initialize._meta` is blocked through the normal `McpClient.CreateAsync` path. The spec and the SDK type both support it; only the public API is missing the hook.

Concrete example: a downstream MCP server that validates gateway identity at startup needs the connecting client to include an auth token in `initialize._meta`. Because `McpClientOptions` provides no way to set it, a workaround using a custom `IClientTransport` wrapper that writes the token to the subprocess stdin before the MCP handshake was required — significant complexity that a single property on `McpClientOptions` would eliminate.

---

## Proposed fix

Add an `InitializeMeta` property to `McpClientOptions` and thread it through `ConnectAsync`:

```csharp
// McpClientOptions — new property:
public JsonObject? InitializeMeta { get; init; }

// McpClientImpl.ConnectAsync — use it:
new InitializeRequestParams
{
    ProtocolVersion = requestProtocol,
    Capabilities = _options.Capabilities ?? new ClientCapabilities(),
    ClientInfo = _options.ClientInfo ?? DefaultImplementation,
    Meta = _options.InitializeMeta,
}
```

The fix is minimal: `InitializeRequestParams.Meta` already accepts `JsonObject?` — there is nothing to change in the protocol layer, only in the options-to-params plumbing.

---

## Workaround

Use a custom `IClientTransport` wrapper to send out-of-band data before the MCP handshake (e.g. write a bootstrap line to the subprocess stdin for stdio transports). This is non-trivial, transport-specific, and couples the auth mechanism to the transport layer in ways the SDK was designed to abstract.

---

## Related

- **#959** — *"`_meta` field not serialized in HTTP transport - blocks ChatGPT Apps SDK"* (closed): A prior `_meta` serialization bug affecting server-side tool metadata in HTTP responses. Different layer (serialization of outbound tool metadata vs. client-side injection into initialize params), but confirms the maintainers are actively engaged with `_meta` across the SDK surface.

- **#1592** — *"RunSessionHandler silent hang"* (open): The workaround that made this gap impactful — a custom stdio transport bootstrap was needed precisely because `_meta` on initialize was unreachable through `McpClientOptions`.
