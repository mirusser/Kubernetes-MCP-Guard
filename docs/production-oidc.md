# Production OIDC Guide

## Warning: Development vs. Production

The `DevIssuer` and setting `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false` are for local development only. Do not use them in production environments.

## Gateway OIDC Contract

To configure an external OIDC provider, the Gateway expects the following:

- **Issuer**: Access tokens must have an `iss` claim matching `INFRA_GATE_OAUTH_AUTHORITY`.
- **JWKS/Discovery**: The issuer must expose `.well-known/openid-configuration` and JWKS metadata for signature validation.
- **JWT validation**: Tokens must have a valid signature, lifetime (`exp` and related lifetime checks), issuer, and audience.
- **Audience**: Tokens must include an `aud` value matching `INFRA_GATE_OAUTH_RESOURCE`; trailing slash differences are normalized by the gateway.
- **Scopes**: Tokens must include `mcp:tools` in either the `scope` or `scp` claim, unless `INFRA_GATE_OAUTH_SCOPE` is changed.
- **Identity binding**: Tokens must provide `sub` or `client_id`; the gateway uses that identity to bind browser approvals to the requester.

## Keycloak End-to-End Setup

1. **Realm**: Create or use an existing Keycloak realm.
2. **Client Scope**: Create a client scope named `mcp:tools`.
3. **Audience Mapper**: Add an audience mapper for the scope (or the client) that emits the value of `INFRA_GATE_OAUTH_RESOURCE` into the token `aud` claim.
4. **MCP Client Registration**: Register the AI/MCP client in Keycloak, allowing it to request the `mcp:tools` scope.
5. **Approval UI Client**: Register an authorization-code + PKCE client for the approval UI. Configure it to be public-client-compatible and set the redirect URI to `${gatewayBaseUrl}/approvals/oauth/callback` (for example, `https://gateway.example.com/approvals/oauth/callback`).

The current gateway does not expose a configurable approval OAuth client secret and sets a fixed placeholder secret internally. If your identity provider requires confidential clients, add configurable approval-client-secret support as a follow-up code change before documenting that path as supported.

### Required Gateway Environment Variables

When using Keycloak, set the following environment variables. Note that Keycloak uses different default paths for authorization and token endpoints than the DevIssuer:

```bash
export INFRA_GATE_OAUTH_AUTHORITY="https://your-keycloak-domain/realms/your-realm"
export INFRA_GATE_OAUTH_RESOURCE="https://gateway.example.com/mcp"
export INFRA_GATE_OAUTH_SCOPE="mcp:tools"
export INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=true
export INFRA_GATE_APPROVAL_BASE_URL="https://gateway.example.com"
export INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID="infra-gate-approval-ui"
export INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH="/approvals/oauth/callback"

# Keycloak-specific overrides:
export INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT="${INFRA_GATE_OAUTH_AUTHORITY}/protocol/openid-connect/auth"
export INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT="${INFRA_GATE_OAUTH_AUTHORITY}/protocol/openid-connect/token"
```

## Production Checklist

Before moving to production, ensure you have:

- [ ] An HTTPS issuer for OAuth.
- [ ] A stable public gateway URL.
- [ ] Strict redirect URIs configured in the OIDC provider.
- [ ] A secure token lifetime policy.
- [ ] Strictly scoped Kubernetes RBAC (the Gateway enforces namespace limits, but RBAC is still required).
- [ ] No opaque manual bearer values in MCP client configuration.
- [ ] Disabled `DevIssuer` completely.

## Future Support

Entra ID guidance will be added later.
