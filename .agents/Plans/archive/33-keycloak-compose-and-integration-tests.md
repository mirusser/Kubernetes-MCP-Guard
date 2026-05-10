# Keycloak Compose Stack and Integration Tests

## Summary

The gateway already speaks standard OIDC/Bearer+JWKS and `docs/production-oidc.md` already documents Keycloak configuration. This plan adds a runnable local demo and end-to-end integration tests that exercise Keycloak-issued tokens through the real JWT validation path.

The existing integration tests mock JWT issuance entirely (`CreateJwt` with a fixed symmetric key and `FakeOAuthBackchannel` for metadata discovery). Keycloak tests use a real OIDC discovery document, real JWKS, and a real token endpoint.

---

## Deliverables

### 1. Keycloak realm config — `deploy/keycloak/infra-gate-realm.json`

Minimal Keycloak realm imported at `start-dev` time.

| Item | Value |
|---|---|
| Realm | `infra-gate` |
| Client scope | `mcp:tools` with audience mapper → `http://127.0.0.1:3001/mcp` |
| Client | `mcp-client` — public, direct-access grants on, `mcp:tools` in default scopes |
| Client | `mcp-client-limited` — public, direct-access grants on, no `mcp:tools` (used for scope-rejection test) |
| Client | `infra-gate-approval-ui` — public, auth-code + PKCE, redirect `http://127.0.0.1:3001/*` |
| User | `demo` / `demo` |

Access token lifetime: 300 s. Realm JSON is the single source of truth shared by both the Compose stack and Testcontainers.

### 2. Compose stack — `deploy/compose/keycloak.yaml`

- Keycloak 26.2 in `start-dev --import-realm` mode.
- Volume-mounts `deploy/keycloak/` into `/opt/keycloak/data/import/`.
- Health-check on realm discovery endpoint; `mcp-gateway` waits for `service_healthy`.
- Gateway runs in `INFRA_GATE_ENVIRONMENT=Development` (Keycloak uses HTTP in dev mode).
- Named volumes for approval and guardrail data.

### 3. `deploy/compose/keycloak.env.example`

Documents which variables to override (ports, KUBECONFIG, admin credentials).

### 4. Integration tests — `tests/InfraGate.McpGateway.Tests/IntegrationTests/KeycloakIntegrationTests.cs`

- `Testcontainers.Keycloak` 4.11.0 added to test project csproj.
- Realm JSON linked as `<Content>` item copied to `TestData/` in test output.
- `[Trait("Category", "Keycloak")]` on all tests — opt-in, excluded from default runs.
- Container starts once per class via `IAsyncLifetime`.
- Token acquisition via resource-owner-password grant (no browser flow needed in tests).
- No `PostConfigure<JwtBearerOptions>` override — the real JWT Bearer handler fetches OIDC discovery and JWKS from the container.

| Test | Proves |
|---|---|
| `ValidToken_FromKeycloak_AllowsToolCall` | Real JWKS validation works end-to-end |
| `TokenWithWrongAudience_Rejects` | `aud` claim is enforced (gateway configured with a different resource) |
| `TokenWithoutScope_Rejects` | `mcp:tools` scope is enforced (`mcp-client-limited` has no `mcp:tools` default scope) |

### 5. CI workflow — `.github/workflows/keycloak-tests.yml`

Separate workflow from the main build; triggers on push to `main`/`dev` and on PRs. Testcontainers uses the runner's Docker socket — no explicit `services:` block needed. 10-minute timeout.

```bash
dotnet test tests/InfraGate.McpGateway.Tests/ --filter "Category=Keycloak"
```

### 6. `docs/production-oidc.md`

New "Local Keycloak Demo" section before the production checklist: run command, port table, realm summary, `curl` token-acquisition example.

---

## Files Created / Modified

| File | Action |
|---|---|
| `deploy/keycloak/infra-gate-realm.json` | Created |
| `deploy/compose/keycloak.yaml` | Created |
| `deploy/compose/keycloak.env.example` | Created |
| `tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` | Modified — `Testcontainers.Keycloak` package + `<Content>` realm JSON item |
| `tests/InfraGate.McpGateway.Tests/IntegrationTests/KeycloakIntegrationTests.cs` | Created |
| `.github/workflows/keycloak-tests.yml` | Created |
| `docs/production-oidc.md` | Modified — "Local Keycloak Demo" section added |

---

## Verification

```bash
# Compose demo
docker compose -f deploy/compose/keycloak.yaml up --build
# wait ~30 s for Keycloak health check to pass, then:
curl -s http://127.0.0.1:3010/realms/infra-gate/.well-known/openid-configuration | jq .issuer
curl -s -X POST \
  http://127.0.0.1:3010/realms/infra-gate/protocol/openid-connect/token \
  -d "grant_type=password&client_id=mcp-client&username=demo&password=demo&scope=mcp:tools" \
  | jq -r .access_token

# Integration tests (requires Docker)
dotnet test tests/InfraGate.McpGateway.Tests/ --filter "Category=Keycloak"
```
