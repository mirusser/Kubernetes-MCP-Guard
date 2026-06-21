# InfraGate.McpGateway.Auth

`InfraGate.McpGateway.Auth` contains the authentication and authorization layer used by the HTTP MCP gateway. It supports OAuth JWT bearer tokens for MCP calls, browser cookie auth for approval pages, and MCP protected-resource metadata.

**Owns:** OAuth JWT bearer auth, browser cookie auth, MCP protected-resource metadata, audit identity resolution

## Runtime Flow

- `GatewayAuthOptions.cs` binds authentication settings from the `InfraGate:Auth` configuration section.
- `GatewayAuthentication.cs` registers the policy scheme, JWT bearer auth, approval UI cookie/OAuth auth, MCP protected-resource metadata, 403 step-up challenges, scope-to-tool authorization, token activity validation, and authorization policies.
- `GatewayAuditIdentityResolver.cs` maps the authenticated principal to audit-safe subject and authentication-type values.
- `GatewayAuthConventions.cs` holds external strings such as schemes, env vars, claims, and metadata names.

## Important Contracts

- `InfraGate__Auth__OAuthAuthority` is required.
- OAuth tokens must contain the configured audience/resource and required scope. Tokens whose `exp` minus `iat`/`nbf` exceeds `InfraGate__Auth__MaxAcceptedAccessTokenLifetimeSeconds` are rejected; production requires this to be 300 seconds or less.
- When `InfraGate__Auth__TokenIntrospectionEnabled=true`, the gateway validates every otherwise-valid JWT against the issuer introspection endpoint. Only `active: true` is accepted; inactive, malformed, or failed introspection responses fail closed with `401 Unauthorized`.
- Positive introspection results are cached for at most `InfraGate__Auth__TokenIntrospectionCacheSeconds`, capped by the token `exp` claim and any introspection `exp` response. Raw bearer tokens are not used as cache keys.
- When `InfraGate__Auth__RequireDPoP=true`, Gateway API calls must use `Authorization: DPoP <token>` plus a valid `DPoP` proof header bound to the access token, request method, and request URI.
- Human MCP clients can use `mcp:tools.read` for read-only inspection or `mcp:tools.write` for full mutation access. The legacy `mcp:tools` scope remains for backward compatibility.
- Planner clients use the propose scope, Executor clients use the execute scope, and Observer/Planner read paths use the read-only scope.
- Valid OAuth tokens that lack the required scope return `403 Forbidden` with a `WWW-Authenticate` Bearer challenge containing `error="insufficient_scope"`, the required `scope`, and `resource_metadata`.
- Approval UI browser sessions use OAuth authorization-code + PKCE and sign into a gateway cookie. Raw OAuth tokens are not persisted into the approval cookie. Approval UI logout clears only that gateway cookie; revoke or invalidate access tokens at the IdP.
- OAuth identities are normalized for guardrail audit entries.
- Do not move auth env var names or scheme names without updating gateway setup and tests.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md). See [docs/production-oidc.md](../../docs/production-oidc.md) for production OIDC provider setup.

## Verification

- Main tests: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Keycloak integration tests: `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"`
