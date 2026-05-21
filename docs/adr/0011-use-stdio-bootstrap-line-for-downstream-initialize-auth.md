# ADR-0011: Use a Stdio Bootstrap Line for Downstream Initialize Auth

**Date:** 2026-05-21
**Status:** Accepted

---

## Context

InfraGate keeps `InfraGate.McpServer` as a private stdio downstream process. ADR-0008 requires the Gateway Service Identity token to authenticate downstream MCP traffic.

The MCP 2025-11-25 schema allows request `_meta` on `initialize`, but the Microsoft MCP .NET SDK 1.3.0 `McpClient.CreateAsync` path does not expose a way to pass initialize `RequestOptions.Meta`. It does expose `RequestOptions.Meta` for later calls such as `tools/list` and `tools/call`, which InfraGate already uses.

Without an initialize credential path, a downstream server that enforces auth before startup cannot distinguish a real Gateway from an unauthenticated stdio client until after initialization.

## Decision

The Gateway writes one InfraGate-private bootstrap line to the downstream process stdin before handing the stdio streams to `McpClient.CreateAsync`:

```text
io.infragate.downstream.authorization: Bearer <token>
```

The downstream server reads exactly that first line from raw stdin, validates the bearer token, and exits before MCP initialization if validation fails. The server reads byte-by-byte instead of using `Console.In` so it cannot buffer the following JSON-RPC `initialize` message away from the SDK stdio transport.

InfraGate keeps per-request `_meta` auth on `tools/list` and `tools/call`. The bootstrap line covers only the one-time initialize gap; per-request `_meta` remains the actual operation boundary and supports token refresh.

## Consequences

- This is an InfraGate-private stdio convention, not a general MCP auth mechanism.
- The Gateway client credentials remain excluded from the downstream process environment.
- Token values must never be logged; diagnostics may mention only validation status and safe failure reasons.
- When the SDK exposes initialize `_meta`, remove `BootstrapStdioClientTransport`, remove the server bootstrap gate, and pass the same Gateway Service Identity token through the standard initialize metadata path.
