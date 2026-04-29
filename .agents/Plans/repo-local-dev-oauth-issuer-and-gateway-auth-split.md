# Repo-Local Dev OAuth Issuer And Gateway Auth Split

## Summary

Add two projects: `InfraGate.McpGateway.Auth` for gateway authentication/authorization concerns, and `InfraGate.DevIssuer` as a dev-only localhost OAuth/OIDC issuer that lets Codex exercise `codex mcp login` without an external provider. Keep the static bearer demo path and the MCP elicitation approval boundary unchanged.

References used: [Codex MCP docs](https://developers.openai.com/codex/mcp), [Codex config reference](https://developers.openai.com/codex/config-reference), [MCP Authorization 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization).

## Key Changes

- Create `src/InfraGate.McpGateway.Auth` targeting `net10.0`.
  - Move gateway auth setup, static bearer handler, JWT/static bearer scheme selection, MCP protected-resource metadata wiring, scope policy, auth constants, and audit subject resolution into this project.
  - Add a `GatewayAuthOptions` record with bearer token, OAuth authority/resource/scope, HTTPS metadata setting, and `FromEnvironment()`.
  - Keep non-auth gateway options in `InfraGate.McpGateway`; `McpGatewayOptions.FromEnvironment()` delegates auth env parsing to `GatewayAuthOptions`.
  - Gateway `Program.cs` references the auth project and calls `AddGatewayAuthentication(options.Auth)`.
  - `GuardedToolRunner` uses an auth-library resolver for subject/authentication type so OAuth and static bearer audit behavior stays centralized.

- Create `src/InfraGate.DevIssuer` targeting `net10.0` as a dev-only Web project.
  - Default listen URL/issuer: `http://127.0.0.1:3011`.
  - Defaults: resource `http://127.0.0.1:3001/mcp`, scope `mcp:tools`, subject `infra-gate-dev-user`.
  - Config env vars: `INFRA_GATE_DEV_ISSUER_ISSUER`, `INFRA_GATE_DEV_ISSUER_RESOURCE`, `INFRA_GATE_DEV_ISSUER_SCOPE`, `INFRA_GATE_DEV_ISSUER_SUBJECT`; binding still uses normal `ASPNETCORE_URLS`.
  - Use an ephemeral in-memory RSA signing key, authorization-code store, and dynamic-client registry. No persisted keys, refresh tokens, database, passwords, or production login UI.

- Implement the dev issuer endpoints with named conventions, not magic strings.
  - `GET /.well-known/oauth-authorization-server`
  - `GET /.well-known/openid-configuration`
  - `GET /jwks`
  - `POST /register` for lightweight public-client dynamic registration.
  - `GET /authorize` for authorization-code + PKCE, immediately redirecting with `code` and `state` after validating client, redirect URI, resource, scope, and `S256` challenge.
  - `POST /token` validates code, redirect URI, client ID, resource, and PKCE verifier, marks the code used, and returns a JWT access token with `iss`, `aud`, `sub`, `scope`, `client_id`, `preferred_username`, `iat`, `nbf`, `exp`, and `jti`.
  - Invalid authorization/token requests return OAuth-style `400` JSON errors; missing/invalid gateway tokens remain `401`, and missing scope remains `403`.

- Update docs and repo-local guidance.
  - README gains a local Codex OAuth flow: run dev issuer, run gateway with `INFRA_GATE_OAUTH_AUTHORITY=http://127.0.0.1:3011` and `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false`, configure Codex with `url`, `oauth_resource`, and `scopes`, then run `codex mcp login infra-gate`.
  - Keep the static bearer-token demo path documented.
  - Restate that `apply_approved_plan` still requires MCP elicitation and OAuth login does not approve Kubernetes mutations.

## Test Plan

- Gateway/auth split regression:
  - Missing `/mcp` token returns `401` with `resource_metadata`.
  - Protected resource metadata is public and contains resource, authorization server, and scope.
  - Static bearer token still accepts/rejects correctly with OAuth enabled and disabled.
  - Valid JWT is accepted; wrong issuer, wrong audience, expired token, malformed token, and missing token return `401`.
  - Valid JWT missing `mcp:tools` returns `403`.
  - Audit events still include OAuth subject or `local-bearer-demo` without logging credentials.

- Dev issuer tests:
  - Discovery and JWKS endpoints return usable metadata and signing keys.
  - Dynamic registration returns a public client ID.
  - Authorization request with PKCE redirects with `code` and preserves `state`.
  - Token exchange returns a gateway-valid JWT.
  - Wrong PKCE verifier, reused code, wrong resource, missing scope, and invalid redirect URI are rejected.
  - A token issued by `InfraGate.DevIssuer` is accepted by a gateway auth test server.

- Approval safety:
  - Existing `apply_approved_plan` refusal-without-elicitation test remains unchanged.
  - Existing server integration path still applies only after exact pending plan approval.

- Final verification:
  - `dotnet build InfraGate.slnx`
  - `dotnet test InfraGate.slnx`

## Assumptions And Defaults

- The dev issuer is only for localhost development and Codex OAuth-path testing.
- The gateway remains the production resource server; it does not grow a production authorization server.
- Static bearer compatibility remains for demos and is not advertised as OAuth.
- The auth split moves auth concerns only; downstream MCP process management, guardrail scanning, Kubernetes approval, and non-auth gateway hosting stay where they are.
