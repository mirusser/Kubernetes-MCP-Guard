# InfraGate.McpGateway.Auth

`InfraGate.McpGateway.Auth` contains the authentication and authorization layer used by the HTTP MCP gateway. It supports OAuth JWT bearer tokens for MCP calls, browser cookie auth for approval pages, and MCP protected-resource metadata.

## Runtime Flow

- `GatewayAuthOptions.cs` reads authentication settings from environment variables.
- `GatewayAuthentication.cs` registers the policy scheme, JWT bearer auth, approval UI cookie/OAuth auth, MCP protected-resource metadata, 403 step-up challenges, and authorization policies.
- `GatewayAuditIdentityResolver.cs` maps the authenticated principal to audit-safe subject and authentication-type values.
- `GatewayAuthConventions.cs` holds external strings such as schemes, env vars, claims, and metadata names.

## Important Contracts

- `INFRA_GATE_OAUTH_AUTHORITY` is required.
- OAuth tokens must contain the configured audience/resource and required scope.
- Valid OAuth tokens that lack the required scope return `403 Forbidden` with a `WWW-Authenticate` Bearer challenge containing `error="insufficient_scope"`, the required `scope`, and `resource_metadata`.
- Approval UI browser sessions use OAuth authorization-code + PKCE and sign into a gateway cookie.
- OAuth identities are normalized for guardrail audit entries.
- Do not move auth env var names or scheme names without updating gateway setup and tests.

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md). See [docs/production-oidc.md](../../docs/production-oidc.md) for production OIDC provider setup.

## Verification

- Main tests: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Dev issuer interoperability tests: `dotnet test tests/InfraGate.DevIssuer.Tests/InfraGate.DevIssuer.Tests.csproj`
