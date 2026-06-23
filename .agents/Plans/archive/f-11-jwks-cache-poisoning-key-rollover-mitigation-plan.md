# Remediation Plan: F-11 JWKS Cache Poisoning / Key Rollover Race

**Date:** 2026-06-21
**Finding:** F-11 · JWKS Cache Poisoning / Key Rollover Race
**Severity:** Medium
**Source:** `.agents/Plans/loose/security-audit.md`
**Goal:** Harden every inbound-JWT signing-key validation path so the gateway (and the downstream stdio server) enforce strict `kid` resolution, bound how long a rotated-out signing key stays trusted, and degrade safely on JWKS fetch failure — without breaking local-dev HTTP or the existing 300-second Keycloak token lifespan.

## Context

F-11 is currently marked **NOT MITIGATED**. JWTs are validated against the issuer's JWKS using framework defaults, with no explicit control over key matching, cache lifetime, or fetch-failure behavior.

Confirmed current state (verbatim source reviewed via codegraph):

- **Stack:** `net10.0`; `Microsoft.AspNetCore.Authentication.JwtBearer` `10.0.8` (gateway auth); `Microsoft.IdentityModel.JsonWebTokens` / `Microsoft.IdentityModel.Protocols.OpenIdConnect` `8.18.0` (downstream server).
- **Gateway path** — `src/InfraGate.McpGateway.Auth/GatewayAuthentication.cs` `ConfigureJwtBearerOptions` (lines 123-147) sets `jwtOptions.Authority` (+ optional `OAuthMetadataAddress`) and a `TokenValidationParameters` with `ValidateIssuerSigningKey = true`, but:
  - never assigns a `jwtOptions.ConfigurationManager`, so it relies on JwtBearer's *internal* `ConfigurationManager<OpenIdConnectConfiguration>` with framework defaults (`AutomaticRefreshInterval` 12 h, `RefreshInterval` 5 min);
  - does not set `TryAllIssuerSigningKeys`, which on IdentityModel 8.x defaults to **`true`** — a token whose `kid` matches no JWKS key is still tried against *every* key in the set;
  - does not configure last-known-good fallback.
- **Downstream path** — `src/InfraGate.McpServer/DownstreamAuth/DownstreamTokenValidator.cs` builds its own `ConfigurationManager<OpenIdConnectConfiguration>` (lines 36-40) with default intervals, then `ResolveSigningKeysAsync` (lines 130-147) calls `GetConfigurationAsync()` and copies `config.SigningKeys` into `TokenValidationParameters.IssuerSigningKeys` (line 85). Same three gaps: no `TryAllIssuerSigningKeys = false`, no bounded intervals, no explicit LKG. First-fetch failure fails closed (correct); steady-state serves cached config.
- **Production safety** — `src/InfraGate.McpGateway/Configuration/McpGatewayOptions.cs` `ValidateProductionSafety` (lines 99-189) already forces `Auth.OAuthRequireHttpsMetadata == true` in Production and runs `ProductionSafetyValidator.RequireHttpsNonLoopbackUri` against `OAuthAuthority` and `OAuthMetadataAddress`. So "pin the JWKS endpoint URI + require TLS" is **already satisfied for the gateway in Production**. The downstream server's `DownstreamAuthOptions.RequireHttpsMetadata` defaults `true` but its production HTTPS-non-loopback enforcement must be confirmed.
- **Config surface** — the F-09 work established the extension pattern: constants in `GatewayAuthConventions` → properties on `GatewayAuthOptions` → nullable bindings on `InfraGateAuthSettings` → wiring in `GatewayAuthOptions.FromConfiguration`. Downstream mirrors this in `DownstreamAuthConventions` / `DownstreamAuthOptions` (see `Defaults.ServerClockSkew`).
- **ADRs** — latest is `docs/adr/0030-defer-local-semantic-classifier-sidecar.md`; the next number is **0031**.

## Request

Create an implementation plan only. Do not modify production code or tests in this planning pass.

Acceptance criteria for the eventual remediation:

- A JWT whose `kid` header is missing or matches no key in the active JWKS is rejected (no all-keys brute force).
- The window during which a rotated-out signing key remains trusted is explicitly bounded and small (target ≤ 5 min), on both the gateway and downstream paths.
- A transient JWKS/metadata fetch failure does **not** fail otherwise-valid requests when a previously-good key set is available, and does **not** trigger per-request synchronous refetch storms.
- Local development (Keycloak over HTTP on loopback) and the existing 300-second access-token lifespan keep working unchanged.
- No token, JWKS key material, or client secret is written to logs, audit files, test output, or docs.
- Tests cover: unknown-`kid` rejection, missing-`kid` rejection, valid-`kid` acceptance, rotated-key pickup within the bounded interval, fetch-failure resilience, and existing valid-JWT behavior.

## Recommended remediation shape

Prefer configuring the existing IdentityModel/JwtBearer machinery over building new infrastructure. The four audit recommendations map to concrete, proportionate levers:

| Audit recommendation | Concrete lever |
|---|---|
| Strict `kid` matching; reject unknown/missing `kid` | `TokenValidationParameters.TryAllIssuerSigningKeys = false` on both paths (verify the 8.x default first; the test is the source of truth). |
| Bounded JWKS cache TTL with background refresh, not per-failure refetch | Assign an explicitly-constructed `ConfigurationManager<OpenIdConnectConfiguration>` with bounded `AutomaticRefreshInterval` (≈5 min) and a sane `RefreshInterval` floor on both paths. |
| Last-known-good on fetch failure (don't fail open, don't refetch synchronously per request) | Keep `ConfigurationManager`'s cached-config-on-background-failure behavior; **do not** enable blanket `ValidateWithLKG` (see Decisions — it widens the stale-trust window). |
| Pin JWKS URI + validate TLS, even in dev | Already enforced for the gateway in Production via `RequireHttpsMetadata` + `RequireHttpsNonLoopbackUri`. Extend the same assertion to the downstream path; keep local-dev HTTP-over-loopback as a documented accepted risk (see Decisions). |

This is a configuration-and-hardening slice, not a new subsystem. It deliberately does **not** add custom signing-key resolvers, a bespoke JWKS cache, or CA-pinning in local dev.

## Dependency graph

```
Confirm IdentityModel 8.18.0 behavior + config surface (Task 1, 2)
        │
        ├── Gateway: strict kid (Task 3) ──┐
        │                                   ├── Gateway: bounded CM + LKG-on-failure (Task 4)
        │                                   │
        └── Downstream: strict kid + bounded CM + LKG (Task 5)
                │
                └── Production safety + run-profiles (Task 6)
                        │
                        └── Keycloak integration (Task 7) → ADR 0031 + docs (Task 8) → audit closure (Task 9)
```

Foundation first (confirm behavior), then vertical slices per validation path (each independently testable), then prod-safety, integration, docs, and audit update. High-risk assumption (library default behavior) is validated in Task 1 to fail fast.

## Plan

### Phase 0: Confirm library behavior and config surface

- [x] **Task 1: Confirm IdentityModel 8.18.0 key-resolution and refresh semantics; pin the levers with a characterization test.**
  - Description: De-risk the central assumption before building. Confirm, against `Microsoft.IdentityModel.Tokens` 8.18.0, that (a) `TryAllIssuerSigningKeys` defaults to `true` and setting it `false` makes unknown-`kid` tokens fail, (b) a custom `ConfigurationManager` assigned to `JwtBearerOptions.ConfigurationManager` overrides the Authority-derived default, and (c) `ConfigurationManager` serves the cached config when a background refresh fails. Also confirm no *other* component validates inbound JWTs (Observer/Planner/Executor reference JwtBearer but are token *clients*, not resource servers — verify they host no JWKS-validating endpoint).
  - Acceptance criteria:
    - [x] A throwaway/characterization test proves an unknown-`kid` token is accepted with default TVP and rejected with `TryAllIssuerSigningKeys = false`. This test (or its assertion) becomes the real regression test in Task 3 — the behavior, not the recollected default, is authoritative.
    - [x] Confirmed list of inbound-JWT validators is exactly: gateway `ConfigureJwtBearerOptions` and `DownstreamTokenValidator`. Any additional validator found is added to scope explicitly.
  - Verification:
    - [x] Run the characterization test locally; capture pass/fail output.
  - Likely files: `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayAuthenticationTests.cs` (scratch assertion), read-only sweep of `src/InfraGate.{Observer,Planner,Executor}`.
  - Scope: **S**

- [x] **Task 2: Define the JWKS-hardening configuration surface.**
  - Description: Add default constants and (only where tests/operators need them) configuration bindings for the bounded refresh intervals, following the F-09 pattern. Decide whether `kid` strictness is a hardcoded constant (recommended — it should always be on) versus an option.
  - Acceptance criteria:
    - [x] New default constants live next to existing ones (`GatewayAuthConventions` and `DownstreamAuthConventions.Defaults`), e.g. `JwksAutomaticRefresh` (≈5 min) and `JwksMinimumRefreshInterval` (≈1 min floor).
    - [x] `TryAllIssuerSigningKeys = false` is applied unconditionally (not a tunable knob) on both paths.
    - [x] If refresh intervals are made configurable, the new keys follow the `InfraGate__Auth__...` / `InfraGate__DownstreamAuth__...` naming and bind through `InfraGateAuthSettings` / `DownstreamAuthOptions`; otherwise they remain internal constants. Default to constants unless a test requires otherwise (Simplicity First).
  - Verification:
    - [x] Review against `GatewayAuthConventions.cs`, `GatewayAuthOptions.cs`, `InfraGateAuthSettings.cs`, `DownstreamAuthConventions.cs`, `DownstreamAuthOptions.cs` before implementation.
  - Likely files: `src/InfraGate.McpGateway.Auth/GatewayAuthConventions.cs`, `src/InfraGate.DownstreamAuth/DownstreamAuthConventions.cs` (+ options/settings only if made configurable).
  - Scope: **S**

### Checkpoint: Behavior and config review

- [x] Library default for `TryAllIssuerSigningKeys` is empirically confirmed, not assumed.
- [x] The set of inbound-JWT validators is confirmed complete.
- [x] Reviewer agrees `kid` strictness is unconditional and refresh intervals default to bounded constants.

### Phase 1: Gateway JWKS hardening (primary — Diagram 1)

- [x] **Task 3: Enforce strict `kid` matching on the gateway.**
  - Description: Set `TryAllIssuerSigningKeys = false` in the gateway `TokenValidationParameters` (`ConfigureJwtBearerOptions`, around line 137-145).
  - Acceptance criteria:
    - [x] A token signed by a key whose `kid` is not in the active JWKS is rejected with `401`.
    - [x] A token with no `kid` header is rejected.
    - [x] A token signed by the current JWKS key is still accepted (no regression).
  - Verification:
    - [x] Extend `GatewayAuthenticationTests` with TestServer cases for unknown-`kid`, missing-`kid`, and valid-`kid` using locally-minted signing keys.
  - Likely files: `src/InfraGate.McpGateway.Auth/GatewayAuthentication.cs`, `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayAuthenticationTests.cs`.
  - Dependencies: Task 1, Task 2.
  - Scope: **S**

- [x] **Task 4: Bound the gateway JWKS cache and degrade safely on fetch failure.**
  - Description: Construct a `ConfigurationManager<OpenIdConnectConfiguration>` from the resolved metadata address (`OAuthMetadataAddress` ?? `Authority` + `/.well-known/openid-configuration`) with bounded `AutomaticRefreshInterval` and `RefreshInterval`, an `HttpDocumentRetriever { RequireHttps = options.OAuthRequireHttpsMetadata }`, and assign it to `jwtOptions.ConfigurationManager`. Rely on the manager's cached-config-on-background-failure behavior for fetch resilience; do **not** enable blanket `ValidateWithLKG`.
  - Acceptance criteria:
    - [x] Rotated keys are picked up within the bounded `AutomaticRefreshInterval` (provable with a short interval in test).
    - [x] A token signed by a key that has rotated *out* of the JWKS stops being accepted once the cache refreshes (stale-trust window is bounded, not 12 h).
    - [x] A transient metadata/JWKS fetch failure after at least one successful fetch does not reject tokens that the cached key set still validates, and does not trigger a synchronous refetch per request.
    - [x] `RequireHttps` on the retriever follows `OAuthRequireHttpsMetadata` so local-dev HTTP keeps working and Production stays HTTPS-only.
  - Verification:
    - [x] Unit/TestServer tests with a fake document retriever / short intervals covering rollover pickup and fetch-failure resilience.
  - Likely files: `src/InfraGate.McpGateway.Auth/GatewayAuthentication.cs`, `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayAuthenticationTests.cs`.
  - Dependencies: Task 3.
  - Scope: **M**

### Checkpoint: Gateway auth review

- [x] All gateway auth tests pass; DPoP-required and introspection-enabled paths (F-07, F-09) still pass.
- [x] Failure messages/logs reviewed for token or key-material leakage.

### Phase 2: Downstream server JWKS hardening (secondary — internal stdio path)

- [x] **Task 5: Apply strict `kid` matching, bounded refresh, and fetch-failure resilience to `DownstreamTokenValidator`.**
  - Description: Set `TryAllIssuerSigningKeys = false` in the downstream `TokenValidationParameters`; bound the existing `ConfigurationManager`'s `AutomaticRefreshInterval`/`RefreshInterval`. Keep `ResolveSigningKeysAsync` serving the cached config on background-refresh failure (already its effective behavior); preserve current fail-closed on first-fetch failure.
  - Acceptance criteria:
    - [x] Unknown-/missing-`kid` downstream tokens are rejected; valid-`kid` tokens still validate.
    - [x] Rotated-out keys stop validating within the bounded interval.
    - [x] Behavior parity with the gateway path; no change to the static-keys test constructor.
  - Verification:
    - [x] Extend the downstream validator's unit tests (static-keys constructor) for unknown/missing/valid `kid`; assert bounded-interval config.
  - Likely files: `src/InfraGate.McpServer/DownstreamAuth/DownstreamTokenValidator.cs`, downstream validator tests under `tests/InfraGate.McpServer.Tests/` (and/or `tests/InfraGate.DownstreamAuth.Tests/`).
  - Dependencies: Task 2.
  - Scope: **M**

### Checkpoint: Downstream auth review

- [x] Downstream tests pass; stdio bootstrap/initialize auth (ADR 0011) unaffected.

### Phase 3: Production safety and run-profiles

- [x] **Task 6: Close production-safety parity for the downstream JWKS/metadata endpoint and thread any new config.**
  - Description: Confirm whether `DownstreamAuthOptions.ValidateForServer` (or `McpGatewayOptions.ValidateProductionSafety`) already asserts HTTPS-non-loopback for the downstream `Authority`/`MetadataAddress`; add the assertion if missing (mirroring the gateway's existing `RequireHttpsNonLoopbackUri` calls). Thread any new refresh-interval settings through `deploy/run-profiles.yaml` only if they were made configurable in Task 2.
  - Acceptance criteria:
    - [x] In Production mode, a loopback/HTTP downstream metadata URI is rejected at startup (or confirmed already rejected, with a test asserting it).
    - [x] No change to local/dev profiles' ability to use HTTP over loopback.
  - Verification:
    - [x] `RuntimeSafety` / `McpGatewayOptions` production-safety unit tests; `dotnet run --project src/InfraGate.RunProfiles -- validate` if schema changed.
  - Likely files: `src/InfraGate.McpGateway/Configuration/McpGatewayOptions.cs`, `src/InfraGate.DownstreamAuth/DownstreamAuthOptions.cs`, `tests/InfraGate.McpGateway.Tests/UnitTests/McpGatewayOptionsTests.cs`, possibly `deploy/run-profiles.yaml`.
  - Dependencies: Task 5.
  - Scope: **S**

### Phase 4: Integration, ADR, docs, and audit closure

- [x] **Task 7: Add Keycloak-backed coverage for `kid` behavior where practical.**
  - Description: Use the existing Keycloak Testcontainers suite (already exercises real OIDC discovery + JWKS validation) to prove a real Keycloak-issued token (which always carries a `kid`) is accepted, and that a token whose `kid` is absent from the realm JWKS is rejected. If realm key-rollover is not reliably reproducible in Testcontainers, document the limitation in the test (as F-09 did) and keep rollover/fetch-failure covered by fake-retriever unit tests.
  - Acceptance criteria:
    - [x] Real Keycloak token with valid `kid` is accepted through the gateway JWT bearer pipeline.
    - [x] Unknown-`kid` token is rejected against the real realm JWKS.
    - [x] Any Testcontainers limitation on rollover is documented in-test with a narrower assertion.
  - Verification:
    - [x] `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"` when opt-in prerequisites are available.
  - Likely files: `tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs`.
  - Dependencies: Task 3, Task 4.
  - Scope: **M**

- [x] **Task 8: Write ADR 0031 and update documentation.**
  - Description: Record the JWKS-validation hardening decision and update operator docs.
  - Acceptance criteria:
    - [x] `docs/adr/0031-jwks-validation-hardening.md` states: unconditional strict `kid` (`TryAllIssuerSigningKeys = false`), bounded refresh intervals, LKG used only as a fetch-failure fallback (not blanket superseded-key acceptance), and local-dev HTTP-over-loopback as an accepted risk with TLS enforced in Production.
    - [x] `src/InfraGate.McpGateway.Auth/README.md`, `docs/configuration.md`, and `docs/production-oidc.md` describe strict `kid` matching, the bounded JWKS cache/refresh behavior, fetch-failure degradation, and any new settings + defaults.
    - [x] No real tokens, keys, or secrets in any example.
  - Verification:
    - [x] Docs reviewed against actual constants/behavior; ADR numbering correct (0031).
  - Likely files: `docs/adr/0031-jwks-validation-hardening.md`, `src/InfraGate.McpGateway.Auth/README.md`, `docs/configuration.md`, `docs/production-oidc.md`.
  - Dependencies: Tasks 3-6.
  - Scope: **S**

- [x] **Task 9: Update the F-11 audit entry after verification.**
  - Description: Flip F-11 status only after tests prove the remediation.
  - Acceptance criteria:
    - [x] `.agents/Plans/loose/security-audit.md` F-11 Resolution and the Summary Table / Prioritization rows updated to reflect actual behavior.
    - [x] Implementation Notes cite the files/tests that enforce strict `kid`, bounded refresh, and fetch-failure degradation, and explicitly state what remains out of scope (e.g., CA-pinning in local dev).
  - Verification:
    - [x] Reviewer confirms the audit wording matches actual behavior.
  - Dependencies: Tasks 3-8.
  - Scope: **XS**

### Checkpoint: Complete

- [x] All unit + (opt-in) Keycloak integration tests pass.
- [x] Local dev (HTTP Keycloak) and the 300-second token lifespan still work end-to-end.
- [x] Audit entry reflects reality; ready for review.

## Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| `TryAllIssuerSigningKeys` default differs from recollection, so the "strict kid" change is a no-op or over-strict. | Medium | Task 1 confirms the default empirically; the unknown-`kid`-rejection test is the source of truth, not the recollected default. |
| Requiring a `kid` rejects a legitimate provider that omits it. | Low | Keycloak always emits `kid`; covered by a real-Keycloak acceptance test. Document the requirement; revisit only if a real provider needs an exception. |
| Bounding `AutomaticRefreshInterval` too low causes JWKS fetch pressure / DoS-by-cache-bust. | Medium | Enforce a `RefreshInterval` floor (≈1 min); strict `kid` rejects unknown-`kid` floods cheaply without forcing refetch. |
| LKG fallback widens the stale-trust window (keeps trusting a rotated-out key). | Medium | Use LKG only as a fetch-failure fallback via cached config; do **not** enable blanket `ValidateWithLKG`. Documented in ADR 0031 (see Decisions). |
| Replacing the gateway's implicit ConfigurationManager regresses introspection (F-09) or DPoP (F-07). | High | Keep `OnTokenValidated` and all TVP fields intact; only add `TryAllIssuerSigningKeys` and assign `ConfigurationManager`. Re-run F-07/F-09 tests at the Phase 1 checkpoint. |
| Downstream first-fetch JWKS failure fails closed and blocks the stdio path at startup. | Low | This is existing, correct fail-closed behavior; preserve it. Steady-state uses cached config. |

## Decisions

- **Strict `kid` is unconditional.** `TryAllIssuerSigningKeys = false` is always applied; it is a correctness/safety property, not an operator-tunable knob.
- **LKG is a fetch-failure fallback, not blanket acceptance.** The audit asks to both *shrink* the stale-trust window and *fall back to last-known-good on fetch failure* — these are in tension. Resolution: rely on `ConfigurationManager` continuing to serve its cached config when a background refresh fails (availability), but do **not** enable `TokenValidationParameters.ValidateWithLKG`, which would accept tokens signed by superseded keys and widen the very window we are bounding.
- **No CA-pinning in local dev.** The audit recommends pinning the JWKS TLS cert to a CA "even in local/dev." Local dev intentionally runs Keycloak over HTTP on loopback; CA-pinning there is impractical and contradicts the existing setup. TLS is already enforced in Production via `RequireHttpsMetadata` + `RequireHttpsNonLoopbackUri`. Local-dev HTTP-over-loopback is recorded as an accepted risk in ADR 0031. This is a deliberate scope decision, surfaced rather than silently dropped.
- **Scope: gateway primary, downstream secondary, agents out.** F-11 targets Diagram 1 (the gateway). The downstream stdio validator is an internal secondary consumer included for parity. Observer/Planner/Executor are token clients, not resource servers, and are excluded unless Task 1 finds a JWKS-validating endpoint among them.
- **Configurability minimized.** Refresh intervals default to bounded constants; they become bound configuration only if a test or operator requirement demands it (Simplicity First, per AGENTS.md).

## Open questions

- **Base branch for implementation.** Per repo convention, confirm the base branch before any implementation (the active feature branch is almost always the correct base — do not assume `main`). This plan is written against the current working tree only.
- **Make refresh intervals configurable or leave as constants?** Recommendation: constants, unless the reviewer wants per-environment tuning.

## Review gate

Implementation must wait for explicit user approval, and for confirmation of the base branch to implement on.
