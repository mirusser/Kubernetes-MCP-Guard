# Remove DevIssuer And Use Keycloak For Local OAuth

InfraGate removes `InfraGate.DevIssuer` and its local Compose mode, tests, Docker image, and release smoke path. Keycloak is the only supported local/demo **Identity Provider**, while `InfraGate.McpGateway.Auth` remains issuer-neutral and production deployments may use any OIDC provider that issues the gateway resource as a JWT audience with the required scope.

This trades the convenience of an in-memory issuer and its strict RFC 8707 resource-indicator test harness for one production-closer local OAuth path, less duplicate identity-provider behavior, and clearer release artifacts. The repository narrows its current RFC 8707 claim to gateway protected-resource metadata and token audience validation; issuer-side resource-indicator behavior should be revisited when Keycloak supports the needed MCP/RFC 8707 flow cleanly.
