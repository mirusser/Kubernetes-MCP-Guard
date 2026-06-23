# Remediation Plan: F-09 JWT Revocation and Introspection

**Date:** 2026-06-21  
**Finding:** F-09 · No JWT Revocation Mechanism  
**Source:** `.agents/Plans/loose/security-audit.md`  
**Goal:** Close the gap where a compromised OAuth access token remains usable until expiry by adding gateway-side active-token validation, bounded token lifetimes, and documented IdP revocation expectations.

## Context

F-09 is currently marked **NOT MITIGATED**. The gateway accepts JWTs after local signature/issuer/audience/lifetime/scope validation, but it does not ask the issuer whether a token is still active and does not maintain a revocation denylist.

Relevant current state:

- `src/InfraGate.McpGateway.Auth/GatewayAuthentication.cs` configures JWT bearer validation with `ValidateLifetime = true`, issuer validation, audience validation, and scope authorization.
- `GatewayAuthentication.CreateJwtBearerEvents` already uses `OnTokenValidated` for optional DPoP proof validation, making it the natural hook for token activity validation.
- `src/InfraGate.McpGateway.Auth/GatewayAuthOptions.cs`, `GatewayAuthConventions.cs`, and `InfraGateAuthSettings.cs` own auth configuration. They currently have DPoP settings but no introspection, max-token-age, revocation, or blacklist settings.
- `src/InfraGate.McpGateway/Approval/Service/GatewayApprovalEndpoints.cs` logout signs out only the browser approval cookie; it does not revoke access tokens at the IdP.
- `deploy/keycloak/infra-gate-realm.json` and `tests/TestData/keycloak/infra-gate-realm.json` already set `accessTokenLifespan` to `300` seconds, but the gateway does not enforce that bound for other providers.
- `docs/production-oidc.md` says production should use a secure token lifetime, but does not define an introspection/revocation contract.
- `docs/configuration.md` documents an in-memory DPoP replay-store limitation. DPoP reduces replay risk for bound tokens, but it is not a revocation mechanism and does not cover normal bearer tokens.

## Request

Create an implementation plan only. Do not modify production code or tests in this planning pass.

Acceptance criteria for the eventual remediation:

- The gateway can reject an otherwise valid JWT when the issuer reports it as inactive/revoked.
- Active-token checks are cached only for a short, bounded window and never beyond the token's expiration.
- The gateway enforces a configurable maximum accepted access-token lifetime so providers cannot issue long-lived access tokens unnoticed.
- No bearer token or credential material is written to logs, audit files, test failure messages, or documentation examples.
- Local Keycloak/demo and production OIDC docs explain how revocation is expected to work.
- Tests cover active, inactive/revoked, introspection failure, caching, max-token-lifetime rejection, and existing valid JWT behavior.

## Recommended remediation shape

Prefer standards-based issuer introspection over a gateway-owned `/revoke` endpoint for the first implementation slice:

1. Add configurable OAuth token introspection to the gateway auth layer.
2. Validate active status in `JwtBearerEvents.OnTokenValidated` after local JWT validation and before DPoP success is accepted.
3. Cache successful introspection for a short configurable TTL, defaulting to 30 seconds, capped by JWT `exp`.
4. Fail closed when introspection is enabled and the introspection endpoint is unavailable or returns inactive.
5. Enforce `MaxAcceptedAccessTokenLifetimeSeconds`, defaulting to 300 seconds for production guidance, by comparing token lifetime claims.

Do **not** add a gateway `/revoke` endpoint in the first slice unless the reviewer explicitly requires it. Revocation should remain source-of-truth in the IdP; otherwise the gateway needs a durable multi-replica denylist, endpoint authorization model, audit trail, and operational playbook.

## Plan

### Phase 1: Confirm OIDC contract and config surface

- [ ] Task 1: Define the auth options needed for token activity validation.
  - Acceptance criteria:
    - [ ] Options cover `TokenIntrospectionEnabled`, `TokenIntrospectionEndpoint`, `TokenIntrospectionClientId`, `TokenIntrospectionClientSecret`, `TokenIntrospectionCacheSeconds`, and `MaxAcceptedAccessTokenLifetimeSeconds`.
    - [ ] Defaults preserve local-development compatibility while production docs recommend enabling introspection and a 300-second max lifetime.
    - [ ] Secret-bearing settings are named consistently with existing `InfraGate__Auth__...` conventions.
  - Verification:
    - [ ] Review against `GatewayAuthOptions`, `GatewayAuthConventions`, and `InfraGateAuthSettings` before implementation.
  - Likely files:
    - `src/InfraGate.McpGateway.Auth/GatewayAuthOptions.cs`
    - `src/InfraGate.McpGateway.Auth/GatewayAuthConventions.cs`
    - `src/InfraGate.McpGateway.Auth/InfraGateAuthSettings.cs`

- [ ] Task 2: Decide provider discovery behavior.
  - Acceptance criteria:
    - [ ] If `TokenIntrospectionEndpoint` is omitted, the gateway can resolve `introspection_endpoint` from OIDC discovery when the provider exposes it.
    - [ ] If neither configured nor discoverable and introspection is enabled, startup or first validation fails closed with a credential-free error.
  - Verification:
    - [ ] Confirm Keycloak's endpoint path for the local realm and production docs: `/protocol/openid-connect/token/introspect`.

### Checkpoint: Configuration review

- [ ] Security reviewer agrees that IdP introspection is the primary revocation path.
- [ ] Reviewer confirms gateway-owned `/revoke` remains out of scope for the first remediation slice.

### Phase 2: Implement gateway token activity validation

- [ ] Task 3: Add an internal token introspection client and result model.
  - Acceptance criteria:
    - [ ] Sends `POST` form data containing the access token to the configured introspection endpoint with configured client authentication.
    - [ ] Treats only `active: true` as success.
    - [ ] Does not log or return raw token values.
    - [ ] Handles malformed responses, HTTP errors, and cancellation deterministically.
  - Verification:
    - [ ] Unit tests with fake `HttpMessageHandler` for active, inactive, malformed, and HTTP failure responses.
  - Likely files:
    - New files under `src/InfraGate.McpGateway.Auth/` for the client, options/result, and cache seam.
    - New tests under `tests/InfraGate.McpGateway.Tests/UnitTests/`.

- [ ] Task 4: Add short-lived positive-result caching.
  - Acceptance criteria:
    - [ ] Cache key is a non-reversible hash of the token, not the raw token.
    - [ ] Cache TTL is the smaller of configured cache seconds, token remaining lifetime, and any useful introspection expiry claim if available.
    - [ ] Inactive and failed introspection responses are not cached as successful authorizations.
  - Verification:
    - [ ] Tests prove a second request within the TTL avoids another introspection call.
    - [ ] Tests prove a request after TTL re-introspects and can reject an inactive token.

- [ ] Task 5: Hook activity validation into JWT bearer `OnTokenValidated`.
  - Acceptance criteria:
    - [ ] Local JWT validation still rejects bad issuer/audience/lifetime before introspection-specific success is possible.
    - [ ] When introspection is disabled, existing valid JWT behavior remains unchanged.
    - [ ] When introspection is enabled, inactive or failed introspection calls produce `401 Unauthorized`.
    - [ ] Existing DPoP validation continues to run and still rejects bearer tokens when DPoP is required.
  - Verification:
    - [ ] Extend `GatewayAuthenticationTests` with TestServer coverage for enabled/disabled introspection paths.

- [ ] Task 6: Enforce maximum accepted token lifetime.
  - Acceptance criteria:
    - [ ] Tokens whose `exp - iat` or `exp - nbf` exceeds the configured maximum are rejected.
    - [ ] Behavior for tokens missing both `iat` and `nbf` is explicit and tested.
    - [ ] Default/configured max does not break the existing local Keycloak 300-second token lifespan.
  - Verification:
    - [ ] Unit tests cover acceptable lifetime, excessive lifetime, expired token, and missing-baseline-claim cases.

### Checkpoint: Auth behavior review

- [ ] All auth tests pass locally.
- [ ] Failure messages and logs are checked for token leakage.
- [ ] DPoP-required scenarios still pass.

### Phase 3: Validate with Keycloak and run-profile configuration

- [ ] Task 7: Add Keycloak-backed integration coverage where practical.
  - Acceptance criteria:
    - [ ] A real Keycloak-issued token is accepted when introspection reports active.
    - [ ] A revoked/logged-out/session-invalidated token is rejected after the configured cache TTL, if Keycloak test APIs make this reliable.
    - [ ] If direct revocation is not reliable in Keycloak Testcontainers, document the limitation in the test with a narrower active-introspection assertion and keep inactive-token behavior covered by fake endpoint tests.
  - Verification:
    - [ ] `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"` when opt-in prerequisites are available.

- [ ] Task 8: Thread new auth settings through generated run profiles if generated env support is needed.
  - Acceptance criteria:
    - [ ] `deploy/run-profiles.yaml` can express introspection settings for local/prod-like profiles without scattering manual env vars.
    - [ ] Run-profile validation tests cover the new fields if the schema changes.
  - Verification:
    - [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate`.
    - [ ] Relevant `InfraGate.RunProfiles.Tests` pass.

### Checkpoint: Integration review

- [ ] Local generated profiles still validate.
- [ ] Local Keycloak token lifespan remains 300 seconds.
- [ ] Production-like configuration path has a clear way to enable introspection.

### Phase 4: Documentation and audit closure

- [ ] Task 9: Update auth and production documentation.
  - Acceptance criteria:
    - [ ] `src/InfraGate.McpGateway.Auth/README.md` explains token introspection, cache behavior, and max token lifetime.
    - [ ] `docs/configuration.md` lists all new auth settings, defaults, and production guidance.
    - [ ] `docs/production-oidc.md` includes Keycloak introspection client setup, endpoint path, token lifetime guidance, and revocation/session invalidation expectations.
    - [ ] Documentation says approval UI logout clears only the gateway cookie and does not revoke IdP access tokens.
  - Verification:
    - [ ] Docs contain no real tokens or client secrets.

- [ ] Task 10: Update the F-09 audit entry after implementation is verified.
  - Acceptance criteria:
    - [ ] `.agents/Plans/loose/security-audit.md` F-09 status changes only after tests prove the remediation.
    - [ ] Implementation notes cite the files/tests that enforce introspection and max token lifetime.
  - Verification:
    - [ ] Reviewer confirms the audit wording matches actual behavior.

## Optional follow-up: Gateway-managed emergency denylist

Only add this if stakeholders require revocation without access to the IdP revocation/session APIs.

- Add a strongly authorized SRE-only endpoint to revoke by `jti` or token hash.
- Store denylist entries durably with expiry, preferably in PostgreSQL or another shared store; do not use process-local memory for production.
- Check the denylist in `OnTokenValidated` before accepting the token.
- Audit denylist additions without logging raw token values.

This is intentionally a follow-up because it adds a second revocation authority and requires multi-replica durability semantics.

## Risks and mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Introspection adds latency to every gateway request. | Medium | Cache positive active results for a short TTL capped by token expiry. |
| IdP outage blocks gateway traffic when introspection is enabled. | High | Fail closed for security; document operational dependency and monitor IdP health. |
| Client secret for introspection leaks through config/logging. | High | Treat as secret config, never log it, and keep examples placeholder-only. |
| Keycloak access-token revocation semantics differ from generic OAuth expectations. | Medium | Test what Keycloak reliably supports; document session/logout/admin revocation behavior explicitly. |
| Multi-replica gateways accept tokens during cache TTL after revocation. | Medium | Keep TTL short, default 30 seconds, and call out maximum revocation propagation delay. |
| Enforcing max token lifetime breaks providers that omit `iat`. | Medium | Make missing-baseline-claim behavior explicit and covered by tests before enabling strict production guidance. |

## Decisions

- **Production introspection requirement:** Production-like modes should fail startup when introspection is disabled unless an explicit documented override is set. Short 2–5 minute access-token lifetimes remain useful defense-in-depth, but they are not a revocation substitute. If an IdP cannot introspect JWT access tokens, the override path must require a maximum accepted access-token lifetime of 300 seconds or less, clear documentation, and preferably DPoP or another token-binding control.
- **Keycloak introspection client:** Use a dedicated confidential resource-server client for introspection. Do not reuse the gateway/approval UI client. The introspection client should have only the permissions needed to introspect tokens for the gateway protected resource.
- **Gateway-owned `/revoke`:** Do not add a gateway-owned `/revoke` endpoint in the first remediation slice. Incident response should use IdP session/token revocation APIs as the source of truth. If operations later require an emergency kill switch, add the optional SRE-only denylist follow-up backed by durable shared storage and audited by token `jti` or non-reversible token hash.
- **Service-account tokens:** Apply introspection to all gateway bearer tokens, including Observer, Planner, and Executor service-account/client-credentials tokens. Machine tokens are high-value and need the same revocation path as human/MCP client tokens. Temporary per-client exemptions should require short token lifetimes and explicit documentation, but the target state is introspection for every gateway bearer token.

## Review gate

Implementation must wait for explicit user approval.
