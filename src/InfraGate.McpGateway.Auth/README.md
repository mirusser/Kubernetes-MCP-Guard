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

## Configuration

- `INFRA_GATE_OAUTH_AUTHORITY`: required OAuth issuer URL for JWT validation and discovery challenge metadata.
- `INFRA_GATE_OAUTH_METADATA_ADDRESS`: optional internal OIDC discovery URL; useful when the public issuer URL differs from the gateway's network path to the issuer.
- `INFRA_GATE_OAUTH_RESOURCE`: expected JWT audience/resource. Defaults to `http://127.0.0.1:3001/mcp`.
- `INFRA_GATE_OAUTH_SCOPE`: required scope. Defaults to `mcp:tools`.
- `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA`: controls JWT metadata HTTPS requirement.
- `INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID`: public OAuth client id used by the browser approval UI.
- `INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT`: optional browser-visible authorization endpoint override.
- `INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT`: optional gateway-visible token endpoint override.
- `INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH`: optional approval UI OAuth callback path.

## Verification

- Main tests: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Dev issuer interoperability tests: `dotnet test tests/InfraGate.DevIssuer.Tests/InfraGate.DevIssuer.Tests.csproj`
