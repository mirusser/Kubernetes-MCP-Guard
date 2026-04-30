# InfraGate.DevIssuer

`InfraGate.DevIssuer` is a localhost-only OAuth/OIDC-style issuer for development. It lets Codex or other MCP clients exercise the gateway OAuth flow without relying on an external identity provider.

## Runtime Flow

- `Program.cs` configures the web app and maps issuer endpoints.
- `DevIssuerApplication*.cs` maps discovery, JWKS, dynamic client registration, authorization, and token endpoints.
- `DevIssuerStore.cs` keeps registered clients and authorization codes in memory.
- `DevIssuerSigningKey.cs` creates an ephemeral RSA signing key and JWKS response.
- `DevIssuerConventions.cs` holds endpoint paths, OAuth constants, JSON keys, claims, errors, and env var names.

## Important Contracts

- This issuer is for localhost development only. Registrations, authorization codes, and signing keys are in memory and reset on restart.
- Redirect URIs must be loopback `http` URIs.
- Authorization code flow requires PKCE `S256`.
- Tokens are JWT access tokens signed with the current ephemeral RSA key.
- The token audience/resource and scope should match gateway OAuth configuration.
- The authorization request must include the resource; the token request may omit it because the authorization code is already resource-bound. If a token request includes an explicitly wrong resource, it is rejected.
- This behavior is intentionally scoped to local OAuth compatibility testing for the gateway path described in [MCP-COMPLIANCE.md](../../docs/MCP-COMPLIANCE.md).

## Configuration

- `INFRA_GATE_DEV_ISSUER_ISSUER`: issuer URL. Defaults to `http://127.0.0.1:3011`.
- `INFRA_GATE_DEV_ISSUER_RESOURCE`: token audience/resource. Defaults to `http://127.0.0.1:3001/mcp`.
- `INFRA_GATE_DEV_ISSUER_SCOPE`: issued scope. Defaults to `mcp:tools`.
- `INFRA_GATE_DEV_ISSUER_SUBJECT`: subject claim. Defaults to `infra-gate-dev-user`.
- `ASPNETCORE_URLS`: optional HTTP binding. Keep it aligned with the issuer URL clients use.

## Verification

- Main tests: `dotnet test tests/InfraGate.DevIssuer.Tests/InfraGate.DevIssuer.Tests.csproj`
- Related gateway auth tests: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
