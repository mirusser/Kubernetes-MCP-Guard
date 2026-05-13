# InfraGate.DevIssuer

`InfraGate.DevIssuer` is a deprecated localhost-only OAuth/OIDC-style issuer for development fallback. Keycloak Mode D is the primary local/test OAuth path; DevIssuer remains so compatibility tests and old local flows can still exercise the gateway without an external identity provider.

## Runtime Flow

- `Program.cs` configures the web app and maps issuer endpoints.
- `DevIssuerApplication*.cs` maps discovery, JWKS, dynamic client registration, authorization, and token endpoints.
- `DevIssuerStore.cs` keeps registered clients and authorization codes in memory.
- `DevIssuerStore.cs` pre-registers the local InfraGate approval UI client and keeps dynamic clients and authorization codes in memory.
- `DevIssuerSigningKey.cs` creates an ephemeral RSA signing key and JWKS response.
- `DevIssuerConventions.cs` holds endpoint paths, OAuth constants, JSON keys, claims, errors, and env var names.

## Important Contracts

- This issuer is for localhost fallback development only. Registrations, authorization codes, and signing keys are in memory and reset on restart.
- Redirect URIs must be loopback `http` URIs.
- Authorization code flow requires PKCE `S256`.
- Tokens are JWT access tokens signed with the current ephemeral RSA key.
- The local approval UI client id and redirect URI are pre-registered so browser approvals work without dynamic registration.
- The token audience/resource and scope should match gateway OAuth configuration.
- The authorization request must include the resource; the token request may omit it because the authorization code is already resource-bound. If a token request includes an explicitly wrong resource, it is rejected.
- This behavior is intentionally scoped to deprecated local OAuth compatibility testing for the gateway path described in [MCP-compliance.md](../../docs/MCP-compliance.md).

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md). DevIssuer is deprecated, development-only, and must not be used as a production identity provider.

## Verification

- Main tests: `dotnet test tests/InfraGate.DevIssuer.Tests/InfraGate.DevIssuer.Tests.csproj`
- Related gateway auth tests: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
