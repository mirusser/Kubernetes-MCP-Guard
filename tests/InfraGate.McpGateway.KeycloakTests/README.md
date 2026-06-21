# InfraGate.McpGateway.KeycloakTests

`InfraGate.McpGateway.KeycloakTests` covers the primary local Keycloak path against a Testcontainers Keycloak container.

## What It Covers

- `KeycloakIntegrationTests.cs`: OIDC discovery, anonymous loopback DCR policy, `mcp-client` authorization-code + PKCE coverage, wrong-verifier rejection, valid-token access, gateway token introspection with the dedicated Keycloak client, wrong-audience rejection, missing-scope rejection, token claim shape, DPoP token acquisition and validation for controlled clients, and the approval browser OAuth callback/cookie path with a real Keycloak-issued token backchannel.
- `KeycloakRealmFileTests.cs`: fast deploy/test realm alignment coverage that does not start Docker.

## Introspection and Revocation

The integration suite proves that a real Keycloak-issued access token is accepted when gateway introspection is enabled. It does **not** prove that a revoked or session-invalidated token is rejected, because Keycloak's default self-contained access tokens remain introspectable as `active` until expiry unless the realm is explicitly configured to check session state at introspection time. Inactive/revoked introspection behavior is covered by `HttpTokenIntrospectionClientTests` in the main gateway test project using a fake introspection endpoint.

## Running Tests

- Keycloak integration: `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"`
- List Keycloak tests without starting Docker: `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --list-tests --filter "Category=Keycloak"`

These tests require Docker. The shared test realm at `tests/TestData/keycloak/infra-gate-realm.json` is loaded as the Keycloak realm config and should stay aligned with `deploy/keycloak/infra-gate-realm.json` for clients, scopes, and DCR policy.
