# Codex/OAuth Login For InfraGate MCP Gateway

## Summary

Add MCP OAuth login support to the HTTP gateway as an OAuth **resource server** that validates JWT access tokens from a configured external OIDC/OAuth issuer, while preserving the existing static bearer token for local demos. OAuth authenticates access to `/mcp`; Kubernetes mutation safety remains unchanged and still depends on MCP elicitation for `apply_approved_plan`.

Reference baseline:
- MCP Authorization 2025-11-25: protected resource metadata, `WWW-Authenticate`, RFC 8707 resource indicators, token validation: https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization
- MCP C# auth pattern: `AddJwtBearer`, `AddMcp`, `MapMcp().RequireAuthorization()`: https://modelcontextprotocol.io/docs/tutorials/security/authorization
- Codex MCP config supports `oauth_resource` and `scopes`: https://developers.openai.com/codex/config-reference
- ASP.NET Core JWT bearer validation guidance: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication

## Key Changes

- Replace the current gateway bearer-only middleware with ASP.NET Core authentication:
  - Add `Microsoft.AspNetCore.Authentication.JwtBearer` to `src/InfraGate.McpGateway`.
  - Configure a policy/default auth scheme that accepts either:
    - the existing static demo bearer token, when `INFRA_GATE_GATEWAY_BEARER_TOKEN` is set;
    - a JWT access token validated against the configured OAuth issuer.
  - Set MCP auth as the default challenge scheme so unauthenticated `/mcp` requests return a `WWW-Authenticate: Bearer ... resource_metadata="..."` challenge.

- Extend `McpGatewayOptions` with OAuth resource-server settings:
  - `INFRA_GATE_OAUTH_AUTHORITY`: enables OAuth when set; used as issuer/authorization server URL.
  - `INFRA_GATE_OAUTH_RESOURCE`: OAuth resource/audience; default `http://127.0.0.1:3001/mcp`.
  - `INFRA_GATE_OAUTH_SCOPE`: required single MCP scope; default `mcp:tools`.
  - `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA`: default `true`; docs may set `false` for localhost-only issuer demos.
  - Existing bearer token remains required only when OAuth is disabled; when OAuth is enabled, the static bearer token is optional but accepted if configured.

- Add MCP protected resource metadata using `ModelContextProtocol.AspNetCore.Authentication`:
  - Serve protected-resource metadata at `/.well-known/oauth-protected-resource`.
  - Metadata contains `resource`, `authorization_servers`, and `scopes_supported`.
  - Do not implement a local authorization server in v1; the configured issuer must expose OAuth/OIDC authorization-server metadata and handle login, PKCE, token issuance, and optional dynamic client registration.

- Validate OAuth tokens at the gateway boundary:
  - Validate signature, issuer, expiration, and audience/resource.
  - Require the configured single scope from `scope` or `scp`.
  - Return `401` for missing, expired, malformed, wrong-issuer, or wrong-audience tokens.
  - Return `403` for valid tokens missing the required scope.
  - Disable inbound claim remapping so audit code sees original claim names.

- Preserve the Kubernetes approval boundary:
  - Keep `DownstreamMcpClient` elicitation forwarding unchanged.
  - Keep `apply_approved_plan` requiring server-side MCP elicitation approval even for OAuth-authenticated users.
  - Clients without elicitation must still receive a clear refusal from the existing approval path, never a silent apply.

- Preserve and enrich audit behavior:
  - Add authenticated subject fields to `GuardrailAuditEvent`, for example `Subject` and `AuthenticationType`.
  - Populate subject from OAuth claims in this order: `preferred_username`, `email`, `sub`, `client_id`; use `local-bearer-demo` for the static bearer path.
  - Do not log tokens, authorization headers, auth codes, refresh tokens, or full claims.

- Update docs:
  - Replace the README “Future: Codex/OAuth login” section with active setup instructions.
  - Include a Codex example using `url = "http://127.0.0.1:3001/mcp"`, `oauth_resource = "http://127.0.0.1:3001/mcp"`, and `scopes = ["mcp:tools"]`.
  - Document `codex mcp login <server>` and restate that `apply_approved_plan` still requires an elicitation-capable client.
  - Keep the existing static bearer-token demo path documented.

## Test Plan

- Gateway auth/discovery tests:
  - Missing token on `/mcp` returns `401` with `WWW-Authenticate` containing `resource_metadata`.
  - `GET /.well-known/oauth-protected-resource` returns JSON with the configured resource, authorization server, and scope, without requiring auth.
  - Existing static bearer token still allows `/mcp`.
  - Wrong static bearer token is rejected.

- OAuth token tests:
  - Valid JWT from a fake test issuer with matching issuer, audience/resource, expiration, and `mcp:tools` scope is accepted.
  - Wrong issuer, wrong audience, expired token, malformed token, and missing token return `401`.
  - Valid token missing `mcp:tools` returns `403`.

- Audit tests:
  - Guardrail request/response audit events include authenticated OAuth subject when available.
  - Static bearer audit events include `local-bearer-demo`.
  - Audit JSON does not include raw authorization header or token content.

- Approval tests:
  - `apply_approved_plan` with no successful elicitation still refuses clearly.
  - Existing server integration path still applies only after exact pending plan approval.

- Final verification:
  - `dotnet build InfraGate.slnx`
  - `dotnet test InfraGate.slnx`

## Assumptions And Defaults

- OAuth v1 is resource-server-only: no local issuer, no token endpoint, no authorization endpoint, no dynamic client registration endpoint in this gateway.
- JWT access tokens are supported; opaque token introspection is out of scope for v1.
- One OAuth scope, `mcp:tools`, gates access to all MCP tools; Kubernetes mutation risk is controlled by the existing MCP elicitation approval flow.
- Static bearer compatibility remains for local demos and is not advertised as OAuth.
- OAuth issuer setup, users, clients, consent, PKCE, and DCR policies are external to this repo and documented as prerequisites.
