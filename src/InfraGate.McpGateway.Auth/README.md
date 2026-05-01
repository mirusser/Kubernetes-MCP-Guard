# InfraGate.McpGateway.Auth

`InfraGate.McpGateway.Auth` contains the authentication and authorization layer used by the HTTP MCP gateway. It supports a local static bearer token, OAuth JWT bearer tokens, and MCP protected-resource metadata.

## Runtime Flow

- `GatewayAuthOptions.cs` reads authentication settings from environment variables.
- `GatewayAuthentication.cs` registers the policy scheme, JWT bearer auth, MCP protected-resource metadata, 403 step-up challenges, and the gateway authorization policy.
- `StaticBearerAuthenticationHandler.cs` authenticates the local demo bearer token path.
- `GatewayAuthToken.cs` parses bearer tokens and compares static tokens in constant time.
- `GatewayAuditIdentityResolver.cs` maps the authenticated principal to audit-safe subject and authentication-type values.
- `GatewayAuthConventions.cs` holds external strings such as schemes, env vars, claims, and metadata names.

## Important Contracts

- At least one of static bearer auth or OAuth authority must be configured.
- Static bearer auth is intended for local demos; OAuth is the real resource-server path.
- OAuth tokens must contain the configured audience/resource and required scope.
- Valid OAuth tokens that lack the required scope return `403 Forbidden` with a `WWW-Authenticate` Bearer challenge containing `error="insufficient_scope"`, the required `scope`, and `resource_metadata`.
- Static bearer and OAuth identities are normalized for guardrail audit entries.
- Do not move auth env var names or scheme names without updating gateway setup and tests.

## Configuration

- `INFRA_GATE_GATEWAY_BEARER_TOKEN`: enables static bearer auth.
- `INFRA_GATE_OAUTH_AUTHORITY`: enables OAuth JWT validation and discovery challenge metadata.
- `INFRA_GATE_OAUTH_METADATA_ADDRESS`: optional internal OIDC discovery URL; useful when the public issuer URL differs from the gateway's network path to the issuer.
- `INFRA_GATE_OAUTH_RESOURCE`: expected JWT audience/resource. Defaults to `http://127.0.0.1:3001/mcp`.
- `INFRA_GATE_OAUTH_SCOPE`: required scope. Defaults to `mcp:tools`.
- `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA`: controls JWT metadata HTTPS requirement.

## Verification

- Main tests: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Dev issuer interoperability tests: `dotnet test tests/InfraGate.DevIssuer.Tests/InfraGate.DevIssuer.Tests.csproj`
