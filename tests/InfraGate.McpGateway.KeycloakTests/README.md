# InfraGate.McpGateway.KeycloakTests

`InfraGate.McpGateway.KeycloakTests` covers real OIDC discovery, JWKS validation, and token endpoint acquisition against a Testcontainers Keycloak container.

## What It Covers

- `KeycloakIntegrationTests.cs`: valid-token access, wrong-audience rejection, and missing-scope rejection through the full JWT bearer authentication path.

## Running Tests

- Keycloak integration: `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"`
- List Keycloak tests without starting Docker: `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --list-tests --filter "Category=Keycloak"`

These tests require Docker. The shared test realm at `tests/TestData/keycloak/infra-gate-realm.json` is loaded as the Keycloak realm config.
