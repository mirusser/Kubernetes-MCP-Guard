# Epic 5 Plan: Production OIDC Guide

## Summary

Implement Epic 5 as a documentation-only change. Add a production OIDC guide centered on Keycloak, link it from the main docs, and update any “planned” references so readers can move from DevIssuer to a real provider without guessing. Do not add runtime auth behavior, helper scripts, or compose files.

## Key Changes

- Create `docs/production-oidc.md` with:
  - Clear warning that DevIssuer and `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false` are development-only.
  - Gateway OIDC contract: issuer, JWKS/discovery, JWT signature/lifetime/issuer/audience validation, `scope` or `scp` containing `mcp:tools`, and `sub` or `client_id` for approval identity binding.
  - Keycloak end-to-end setup: realm, client scope for `mcp:tools`, audience mapper for `INFRA_GATE_OAUTH_RESOURCE`, MCP client registration guidance, approval UI client with redirect URI `${gatewayBaseUrl}/approvals/oauth/callback`, and required gateway env vars.
  - Keycloak-specific endpoint overrides:
    - `INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT=${issuer}/protocol/openid-connect/auth`
    - `INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT=${issuer}/protocol/openid-connect/token`
  - Production checklist: HTTPS issuer, stable public gateway URL, strict redirect URIs, token lifetime policy, RBAC still required, no static bearer tokens, no DevIssuer.
  - “Entra ID later” placeholder only, matching the roadmap.

- Add links to the new guide from:
  - `README.md` Explore/Compatibility area, changing OIDC status from “Keycloak planned” to “Keycloak documented; Entra ID later”.
  - `docs/setup-guide.md`, near DevIssuer/Mode B, as a “Production identity providers” link without duplicating provider setup.
  - `docs/devs-readme.md` and/or `src/InfraGate.McpGateway.Auth/README.md` where they mention external OAuth/OIDC issuer setup.
  - `docs/security-model.md`, replacing the “planned production OIDC guidance” wording with a link.

- Keep the guide honest about current code:
  - Gateway supports external OIDC via existing env vars.
  - Approval UI endpoint paths default to DevIssuer-style `/authorize` and `/token`, so Keycloak requires endpoint override env vars.
  - Do not claim confidential-client secret support unless the code adds a configurable approval client secret in a later epic.

## Test Plan

- Run `git diff --check`.
- Search for stale references:
  - `rg -n "Keycloak planned|production-oidc.md.*planned|DevIssuer.*production|static-bearer|static bearer" README.md docs src -g '*.md'`
- Verify all new relative links point to existing files.
- Verify env var names in the guide match `GatewayAuthConventions` and `GatewayAuthOptions`.
- No .NET tests required unless implementation changes runtime auth code, which this plan explicitly excludes.

## Assumptions

- Scope is docs-only, per your choice.
- Keycloak is the only complete provider walkthrough for Epic 5.
- Entra ID remains future documentation, not part of this implementation.
- The implementer should stop and report a code gap, not paper over it, if live Keycloak validation proves the current approval OAuth client-secret behavior blocks the documented flow.
