# Production OIDC Guide

## Warning: Development vs. Production

The setting `InfraGate__Auth__OAuthRequireHttpsMetadata=false` is for local development only. Do not use it in production environments.

Set `InfraGate__Runtime__Environment=Production` for production deployments. In this mode the Gateway and downstream MCP server fail closed when development defaults are present.

## Gateway OIDC Contract

To configure an external OIDC provider, the Gateway expects the following:

- **Issuer**: Access tokens must have an `iss` claim matching `InfraGate__Auth__OAuthAuthority`.
- **JWKS/Discovery**: The issuer must expose `.well-known/openid-configuration` and JWKS metadata for signature validation.
- **JWT validation**: Tokens must have a valid signature, lifetime (`exp` and related lifetime checks), issuer, and audience.
- **Maximum token lifetime**: Access-token lifetime (`exp - iat`, or `exp - nbf` when `iat` is absent) must not exceed `InfraGate__Auth__MaxAcceptedAccessTokenLifetimeSeconds`; Production mode requires 300 seconds or less.
- **Token introspection/revocation**: Production mode requires gateway-side OAuth token introspection. The issuer must expose an introspection endpoint that returns `active: true` only for currently valid, non-revoked tokens.
- **Audience**: Tokens must include an `aud` value matching `InfraGate__Auth__OAuthResource`; trailing slash differences are normalized by the gateway.
- **Scopes**: Tokens must include `mcp:tools` in either the `scope` or `scp` claim, unless `InfraGate__Auth__OAuthScope` is changed.
- **Identity binding**: Tokens must provide `sub` or `client_id`; the gateway uses that identity to bind browser approvals to the requester.

For Keycloak, bind `aud` with an audience mapper on a client scope or client until Keycloak's MCP/resource-indicator support processes RFC 8707 `resource` values as this gateway requires. The gateway remains the enforcement point for issuer, signature, lifetime, audience, and scope.

## Keycloak End-to-End Setup

1. **Realm**: Create or use an existing Keycloak realm.
2. **Client Scope**: Create a client scope named `mcp:tools`.
3. **Audience Mapper**: Add an audience mapper for the scope (or the client) that emits the value of `InfraGate__Auth__OAuthResource` into the token `aud` claim.
4. **MCP Client Registration**: Register the AI/MCP client in Keycloak, allowing it to request the `mcp:tools` scope.
5. **Approval UI Client**: Register an authorization-code + PKCE client for the approval UI. Configure it to be public-client-compatible and set the redirect URI to `${gatewayBaseUrl}/approvals/oauth/callback` (for example, `https://gateway.example.com/approvals/oauth/callback`).
6. **Introspection Client**: Register a dedicated confidential resource-server client for token introspection. Do not reuse the MCP client or approval UI client. Grant only the permissions required by your IdP to introspect access tokens for the gateway protected resource.

The current gateway does not expose a configurable approval OAuth client secret and sets a fixed placeholder secret internally. If your identity provider requires confidential clients, add configurable approval-client-secret support as a follow-up code change before documenting that path as supported.

### Required Gateway Environment Variables

When using Keycloak, set the following environment variables:

```bash
export InfraGate__Auth__OAuthAuthority="https://your-keycloak-domain/realms/your-realm"
export InfraGate__Auth__OAuthResource="https://gateway.example.com/mcp"
export InfraGate__Auth__OAuthScope="mcp:tools"
export InfraGate__Auth__OAuthRequireHttpsMetadata=true
export InfraGate__Auth__TokenIntrospectionEnabled=true
export InfraGate__Auth__TokenIntrospectionClientId="infra-gate-token-introspection"
export InfraGate__Auth__TokenIntrospectionClientSecret="<secret-from-your-secret-manager>"
export InfraGate__Auth__TokenIntrospectionCacheSeconds=30
export InfraGate__Auth__MaxAcceptedAccessTokenLifetimeSeconds=300
export InfraGate__Runtime__Environment=Production
export InfraGate__Approval__BaseUrl="https://gateway.example.com"
export InfraGate__Auth__ApprovalOAuthClientId="infra-gate-approval-ui"
export InfraGate__Auth__ApprovalOAuthCallbackPath="/approvals/oauth/callback"
export InfraGate__Gateway__GuardAuditRoot="/data/guardrails"
export InfraGate__Approval__Root="/data/approvals"
export InfraGate__Kubernetes__AllowedNamespaces__0="mcp-nginx-demo"

# Kubernetes auth: choose exactly one.
export InfraGate__Kubernetes__KubeConfig="/run/kube/infra-gate.config"
# or:
# export InfraGate__Kubernetes__UseInClusterConfig=true

# Keycloak-specific overrides:
export InfraGate__Auth__ApprovalOAuthAuthorizationEndpoint="${InfraGate__Auth__OAuthAuthority}/protocol/openid-connect/auth"
export InfraGate__Auth__ApprovalOAuthTokenEndpoint="${InfraGate__Auth__OAuthAuthority}/protocol/openid-connect/token"
# Optional: omit when OIDC discovery publishes introspection_endpoint.
export InfraGate__Auth__TokenIntrospectionEndpoint="${InfraGate__Auth__OAuthAuthority}/protocol/openid-connect/token/introspect"
```

For Docker host deployments, put the production values in `/etc/infra-gate/production.env` and run the gateway-only Compose file with the release tag:

```bash
TAG=v1.0.0 docker compose --env-file /etc/infra-gate/production.env -f deploy/compose/production.yaml up -d
```

The production Compose path runs only the gateway. TLS may terminate at a host reverse proxy, but the public OAuth/resource/approval URLs in the env file must remain HTTPS and non-loopback.

## Local Keycloak Demo

`deploy/compose/keycloak.yaml` starts a pre-configured Keycloak instance alongside the gateway. Use this to exercise the full OIDC flow locally without needing a remote identity provider.

```bash
docker compose -f deploy/compose/keycloak.yaml up --build
```

Keycloak takes ~30 seconds to start. The gateway waits for it via a health check.

| Endpoint | URL |
|---|---|
| Keycloak admin console | `http://127.0.0.1:3010` (admin / admin) |
| MCP gateway | `http://127.0.0.1:3001` |
| Realm discovery | `http://127.0.0.1:3010/realms/infra-gate/.well-known/openid-configuration` |

**Pre-configured realm** (`deploy/keycloak/infra-gate-realm.json`):

| Item | Value |
|---|---|
| Realm | `infra-gate` |
| MCP client | `mcp-client` (public, authorization-code + PKCE S256) |
| Smoke/test client | `mcp-smoke-client` (public, direct grants for local non-browser token acquisition only) |
| Limited test client | `mcp-client-limited` (valid audience, no `mcp:tools`) |
| Approval UI client | `infra-gate-approval-ui` (public, PKCE) |
| Introspection client | `infra-gate-token-introspection` (confidential, for optional local gateway introspection tests) |
| Access-token lifespan | 300 seconds |
| Introspection endpoint | `http://127.0.0.1:3010/realms/infra-gate/protocol/openid-connect/token/introspect` |
| Demo user | `demo` / `demo` |
| Scope | `mcp:tools` with audience mapper for `http://127.0.0.1:3001/mcp` |

Anonymous OIDC Dynamic Client Registration is enabled only for this local/demo realm. It is constrained to loopback hosts and local scopes; production should use pre-registered or admin-managed clients.

**Acquire a token:**

```bash
curl -s -X POST \
  http://127.0.0.1:3010/realms/infra-gate/protocol/openid-connect/token \
  -d "grant_type=password&client_id=mcp-smoke-client&username=demo&password=demo&scope=mcp:tools" \
  | jq -r .access_token
```

**Call the MCP gateway:**

```bash
TOKEN=$(curl -s -X POST \
  http://127.0.0.1:3010/realms/infra-gate/protocol/openid-connect/token \
  -d "grant_type=password&client_id=mcp-smoke-client&username=demo&password=demo&scope=mcp:tools" \
  | jq -r .access_token)

curl -H "Authorization: Bearer $TOKEN" http://127.0.0.1:3001/mcp
```

The demo Compose runs in `Development` mode because Keycloak's `start-dev` uses HTTP. For a production-like TLS setup, replace `keycloak.yaml` with your real Keycloak and update the env vars to `InfraGate__Runtime__Environment=Production` with HTTPS URLs.

Approval UI logout clears only the gateway's browser cookie. It does not revoke IdP access tokens. To invalidate already-issued bearer tokens, use Keycloak session logout, admin session invalidation, or token/client revocation APIs at the IdP; with gateway introspection enabled, the next uncached request is rejected when Keycloak reports the token inactive. The positive introspection cache is intentionally short and capped by token expiry, so it is the maximum expected revocation propagation delay per gateway instance.

## Production Checklist

Before moving to production, ensure you have:

- [ ] An HTTPS issuer for OAuth.
- [ ] A stable public gateway URL.
- [ ] Strict redirect URIs configured in the OIDC provider.
- [ ] A secure token lifetime policy: access tokens are 300 seconds or shorter.
- [ ] Gateway token introspection is enabled with a dedicated confidential introspection client.
- [ ] IdP session/token revocation procedures are documented for incident response.
- [ ] Strictly scoped Kubernetes RBAC (the Gateway enforces namespace limits, but RBAC is still required).
- [ ] Explicit Kubernetes auth configured with either `InfraGate__Kubernetes__KubeConfig` or `InfraGate__Kubernetes__UseInClusterConfig=true`.
- [ ] Explicit `InfraGate__Kubernetes__AllowedNamespaces__0`, `InfraGate__Approval__Root`, and `InfraGate__Gateway__GuardAuditRoot`.
- [ ] Durable approval and audit paths that are not temp paths, default dev paths, or group/other-writable.
- [ ] Host Docker env file provisioned at `/etc/infra-gate/production.env` when using the remote Compose deployment.
- [ ] No opaque manual bearer values in MCP client configuration.

## Future Support

Entra ID guidance will be added later.
