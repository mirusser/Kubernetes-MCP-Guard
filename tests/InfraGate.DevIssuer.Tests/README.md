# InfraGate.DevIssuer.Tests

`InfraGate.DevIssuer.Tests` covers the localhost development issuer and its compatibility with the gateway OAuth resource-server validation.

## What It Covers

- Discovery and JWKS metadata shape.
- Dynamic client registration with loopback redirect URI validation.
- Authorization-code flow with PKCE `S256`.
- JWT access tokens accepted by gateway auth validation.
- Rejection of reused authorization codes, wrong PKCE verifiers, wrong resources, and missing required scopes.
- Token exchange compatibility when the authorization code is already resource-bound and the token request omits `resource`.

## Running Tests

- Dev issuer suite: `dotnet test tests/InfraGate.DevIssuer.Tests/InfraGate.DevIssuer.Tests.csproj`
- Full solution: `dotnet test InfraGate.slnx`

Tests use in-memory ASP.NET hosts and do not require starting the dev issuer as a separate process.
