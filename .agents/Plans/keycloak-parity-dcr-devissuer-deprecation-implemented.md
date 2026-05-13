# Implemented Plan: Keycloak Parity, DCR, and DevIssuer Deprecation

Date: 2026-05-13
Status: Implemented

## Summary

Keycloak is now the primary local/test OAuth path for InfraGate. DevIssuer remains available as a deprecated fallback and compatibility test target.

This work implemented DCR parity first, kept audience binding mapper-based for Keycloak, added a Keycloak release smoke path, and updated docs to describe Mode D as the recommended local development flow.

## Implemented Scope

### 1. Keycloak Version And Realm Alignment

- Updated local/test Keycloak references from `26.2` to `26.6.1`.
- Normalized deploy and test realms:
  - `deploy/keycloak/infra-gate-realm.json`
  - `tests/TestData/keycloak/infra-gate-realm.json`
- Kept realm client/scope/component sections intentionally aligned.
- Added missing optional client-scope definitions to avoid import warnings.

Implemented clients:

- `mcp-client`: public authorization-code + PKCE S256 client for normal MCP OAuth.
- `mcp-smoke-client`: public direct-grant client for local/test non-browser token acquisition.
- `mcp-client-limited`: valid audience but no `mcp:tools`, used for 403 coverage.
- `infra-gate-approval-ui`: public authorization-code + PKCE S256 client for browser approvals.

### 2. Keycloak DCR Parity

- Enabled anonymous OIDC Dynamic Client Registration for local/demo Keycloak only.
- Added registration policies for:
  - trusted loopback hosts,
  - allowed client scopes,
  - anonymous max-client cap,
  - full-scope-disabled behavior.
- Added tests for:
  - discovery exposing `registration_endpoint`,
  - successful public loopback client registration,
  - rejection of untrusted redirect URIs,
  - rejection of disallowed scopes.

### 3. Gateway Token Requirements

- Kept Keycloak audience binding through the `mcp:tools` audience mapper.
- Added real `mcp-client` authorization-code + PKCE coverage:
  - requests a Keycloak authorization code with `resource=http://127.0.0.1:3001/mcp`,
  - exchanges the code with the correct verifier and proves the JWT is accepted by the gateway,
  - exchanges a second code with the wrong verifier and verifies Keycloak returns `invalid_grant`.
- Added tests asserting Keycloak-issued tokens include:
  - `aud=http://127.0.0.1:3001/mcp`,
  - `scope=mcp:tools`,
  - usable identity claims (`sub` / `preferred_username`).
- Preserved rejection coverage:
  - wrong audience returns 401,
  - valid audience without required scope returns 403.

### 4. Approval OAuth Coverage

- Avoided scraping the real Keycloak login form because that path is brittle.
- Added stable coverage for the gateway approval OAuth redirect/callback/cookie path using a real Keycloak-issued token supplied through a controlled OAuth token backchannel.
- Left fake-backchannel Safety E2E tests in place for edge cases that need precise principal/time/hash control.

### 5. Keycloak Release Smoke

- Added `scripts/smoke-test-keycloak-release.sh`.
- Smoke behavior:
  - boots Mode D release compose,
  - verifies Keycloak discovery,
  - verifies gateway unauthenticated 401 challenge includes `resource_metadata`,
  - acquires a real Keycloak token through `mcp-smoke-client`,
  - confirms authenticated `/mcp` is not rejected as 401/403.
- Kept the existing DevIssuer smoke script during the deprecation window.

### 6. Docs And Deprecation Messaging

- Updated README, setup guide, developer runbook, architecture, MCP compliance, configuration, production OIDC, release, security, demo, and test READMEs.
- Mode D / Keycloak is documented as primary.
- Mode C / DevIssuer is documented as deprecated fallback.
- Documented the Keycloak RFC 8707/resource-indicator limitation and mapper-based audience binding.
- Updated test docs to reflect DCR/token/browser-approval coverage.

## Intentional Deviations From Original Plan

- The real browser approval test does not scrape Keycloak's login HTML form.
- Instead, it exercises the gateway's real approval OAuth redirect/callback/cookie path and uses a real Keycloak-issued JWT returned through a controlled backchannel.
- Reason: Keycloak login-form scraping is high-maintenance and brittle; the implemented path covers gateway behavior and real token shape without depending on Keycloak theme/form details.
- The `mcp-client` auth-code + PKCE test first tries the Keycloak admin impersonation endpoint, but Keycloak `26.6.1` did not establish a reusable browser SSO session from bearer-token impersonation in this Testcontainer path.
- To keep the auth-code + PKCE coverage real, the test helper falls back to a small local-container login form POST with explicit cookie propagation.

## Verification Evidence

Commands run successfully:

```bash
dotnet build InfraGate.slnx
```

Result:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

```bash
dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"
```

Result:

```text
Passed: 11, Failed: 0
```

```bash
dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --no-build
```

Result:

```text
Passed: 138, Failed: 0
```

```bash
dotnet test tests/InfraGate.DevIssuer.Tests/InfraGate.DevIssuer.Tests.csproj --no-build
```

Result:

```text
Passed: 23, Failed: 0
```

```bash
TAG=latest ./scripts/smoke-test-keycloak-release.sh
```

Result:

```text
OK: Keycloak smoke test passed for tag 'latest'.
```

```bash
git diff --check
```

Result: no whitespace errors.

Additional checks:

- `jq empty` passed for both Keycloak realm JSON files.
- Deploy/test Keycloak realm components, client scopes, and clients matched by normalized diff.
- No Mode D containers were left running after the smoke script.

## Follow-Up Candidates

- Decide when to remove DevIssuer code, image publishing, Mode C compose, and DevIssuer tests.
- Consider a later Playwright/browser-level Keycloak login test only if theme/form fragility is acceptable.
- Add CI execution for `scripts/smoke-test-keycloak-release.sh` once a Kubernetes-in-CI path is available.
