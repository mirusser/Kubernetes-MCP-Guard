# Plan: Residual Gaps — Mitigation

## Overview

Address three residual gaps identified after the `safety-ci-gaps-mitigation-plan.md` implementation. Two are concrete code improvements (base64 embedded-substring detection, real-JWT browser approval identity); one is an architectural limitation that is accepted as sufficient for the current design (RBAC isolation).

---

## Gap Analysis & Decision

### Gap 1: Base64 substring detection in guardrail (HIGH priority)

**Current state:** `TryScanBase64Payloads` in `PromptInjectionGuard.Regex.cs` only decodes strings that are *entirely* valid base64 (charset check + length > 20). It operates on the full `text` argument passed to `AddTextFindings`. If base64 is embedded in a line like `Note: aWtub3JlIHByZXZpb3Vz...`, the colon and space make the charset check fail and the entire string is skipped.

**Risk:** An attacker could bypass the guardrail by prefixing a base64-encoded payload with any non-base64 character. For example, a ConfigMap annotation like `description: aWtub3JlIHByZXZpb3VzIGluc3RydWN0aW9ucw==` would pass through undetected.

**Mitigation approach:** Add a `TryScanEmbeddedBase64Payloads` method that uses a `GeneratedRegex` to find base64-looking substrings (sequences of 20+ `[A-Za-z0-9+/=]` characters) within arbitrary text. Each matched substring is decoded and scanned against the same `Patterns`. This catches base64 payloads embedded in log lines, annotations, labels, and any field where the value is mixed plaintext + base64.

**Trade-off:** Regex scanning for substrings is O(n) per call to `AddTextFindings`. Since `AddTextFindings` is called per-string-value in both argument scanning and response sanitization, the regex should be precompiled (via `[GeneratedRegex]`) and use a short timeout.

### Gap 2: Real-JWT browser approval identity (MEDIUM priority)

**Current state:** The wrong-user browser approval test (`ApproveChallengeBrowser_BrowserSessionAsDifferentSubject_IsRefused`) uses a simulated approval OAuth token from `FakeApprovalOAuthBackchannel` (returns a JWT with `alg: none`, no real Keycloak signature validation). The test proves same-subject enforcement logic but does NOT prove the gateway validates real Keycloak-issued browser tokens during the approval flow.

**Mitigation approach:** Acquire a real Keycloak JWT for the approver (`demo2`) via password grant at test time, parse the claims (`sub`, `scope`, etc.), create a `ClaimsPrincipal` from those claims, and inject it via `SetAuthenticatedSubject`. This proves the identity was derived from a real Keycloak token. Full OAuth token-validation path (JWT signature, issuer, aud, JWKS) is covered by `SmokeTests` for the MCP bearer path; for the browser path, full closure would require replacing `FakeApprovalOAuthBackchannel` with a real token-endpoint caller, which is deferred to a future hardening phase.

### Gap 3: RBAC matrix isolation (ACCEPTED — no action)

**Current state:** `RbacMatrixTests` spawns a direct McpServer subprocess (bypassing the gateway) to verify the server respects its kubeconfig RBAC. It does not route through the gateway.

**Decision:** The architecture uses a *static* SA (`infra-gate-mcp`) for the gateway-to-server connection. There is no dynamic SA switching, so there is no gateway-level identity-forwarding path to test from a second SA. The existing test correctly proves the enforcement point (the server using its kubeconfig SA) is solid. No action needed at this time. If dynamic SAs are added in the future, a gateway-path RBAC test should be added then.

---

## Architecture Decisions

- **GeneratedRegex for base64 substring scanning.** Precompile the regex for zero runtime compilation cost. Use `[GeneratedRegex]` with a short timeout (500ms) matching the existing pattern conventions in `PromptInjectionGuard.Regex.cs`.
- **Real JWT claims in `SetAuthenticatedSubject`.** Parse the decoded JWT payload to extract standard claims (`sub`, `scope`, `client_id`) rather than hardcoding synthetic values. The rest of the `ClaimsPrincipal` construction (identity name, authentication type) stays the same.
- **No fixture restructuring for real OAuth backchannel.** The `FakeApprovalOAuthBackchannel` remains the default. Replacing it with a real Keycloak token-endpoint caller would require changing the OAuth grant type from authorization-code to password, which is a protocol-level change best deferred to a dedicated hardening phase.

---

## Task List

### Phase 1: Base64 embedded-substring detection

#### Task 1: Add `TryScanEmbeddedBase64Payloads` to `PromptInjectionGuard.Regex.cs`

**Description:** Add a `[GeneratedRegex]` pattern `[A-Za-z0-9+/]{20,}={0,2}` to find base64-looking substrings within arbitrary text. For each match, decode with `Convert.FromBase64String`, validate printable-UTF8 heuristics, and scan the decoded text against `Patterns`. Append findings with the original `location`. The existing `TryScanBase64Payloads` for pure-base64 strings remains as-is (catches the fast path for Secret `data` values). The new method handles the mixed-content path (annotations, log lines, labels).

**Acceptance criteria:**
- [ ] Base64 embedded in `Note: aWtub3JlIHByZXZpb3VzIGluc3RydWN0aW9ucw==` is detected
- [ ] Base64 embedded mid-sentence: `description: ignore this then aWtub3JlIHByZXZpb3Vz` is detected
- [ ] Pure base64 strings are still detected (no regression via existing path)
- [ ] Short base64 substrings (< 20 chars) are not decoded (false-positive avoidance)
- [ ] Non-UTF8 decoded substrings are silently skipped
- [ ] Performance: `AddTextFindings` does not regress — the regex is precompiled

**Verification:**
- [ ] New unit tests in `ResponseSanitizationTests.cs` for embedded-base64 injection
- [ ] Existing guardrail tests continue to pass
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/ --filter "FullyQualifiedName~ResponseSanitization"` — green
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/ --filter "FullyQualifiedName~PromptInjectionGuard"` — green

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.McpGateway/PromptInjectionGuard.Regex.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/ResponseSanitizationTests.cs`

**Estimated scope:** Small (2 files)

---

### Phase 2: Real-JWT browser approval identity

#### Task 2: Use real Keycloak JWT claims in `SetAuthenticatedSubject` for wrong-user test

**Description:** The existing wrong-user browser test uses `CreateAuthenticatedApprovalBrowserAsync` with a synthetic subject via `FakeApprovalOAuthBackchannel`. Add a new test variant that acquires a real Keycloak JWT for `demo2`, parses the claims, builds a `ClaimsPrincipal` from them, injects via `SetAuthenticatedSubject`, and calls `ApproveChallengeAsync` directly. The challenge is created by `demo` through the real HTTP MCP path (real Keycloak JWT). This proves same-subject enforcement with identities derived from real Keycloak JWTs.

**Acceptance criteria:**
- [ ] Real Keycloak JWT is acquired for `demo2` (password grant, real signature)
- [ ] Claims from the real JWT are extracted and used to build the `ClaimsPrincipal`
- [ ] Challenge is created by `demo` through real HTTP MCP JWT path
- [ ] `ApproveChallengeAsync` called with `demo2`'s real-JWT-derived identity is refused
- [ ] The existing synthetic-subject tests continue to pass alongside the new test
- [ ] The new test does NOT use `CreateAuthenticatedApprovalBrowserAsync`

**Verification:**
- [ ] `INFRA_GATE_RUN_SAFETY_E2E=1 dotnet test tests/InfraGate.Safety.E2E.Tests/ --filter "FullyQualifiedName~WrongUserApproval"` — 4 tests green
- [ ] Keycloak container starts with both users present

**Dependencies:** None (Keycloak second user already exists from previous plan)

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/Workflows/WrongUserApprovalTests.cs`
- `tests/InfraGate.Safety.E2E.Tests/SafetyE2EFixture.cs`

**Estimated scope:** Small (1-2 files)

---

### Phase 3: Documentation

#### Task 3: Update README with gap-mitigation notes

**Description:** Add a "Known limitations" subsection to the Safety E2E README's "Test architecture" section that documents: embedded base64 detection, real-JWT identity with deferred backchannel, RBAC isolation acceptance.

**Acceptance criteria:**
- [ ] README has a "Known limitations" subsection
- [ ] Gap 1 mitigation is documented (embedded base64 detection)
- [ ] Gap 2 mitigation scope is documented (real-JWT identity, deferred backchannel)
- [ ] Gap 3 is documented as architecturally sufficient

**Verification:**
- [ ] `git diff --check` clean
- [ ] Links in README are valid

**Dependencies:** Tasks 1, 2

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/README.md`

**Estimated scope:** XS (1 file)

---

### Checkpoint: Complete

- [ ] `dotnet build InfraGate.slnx` — clean (0 warnings, 0 errors)
- [ ] `dotnet test InfraGate.slnx --no-build --filter "Category!=Keycloak&Category!=SafetyE2E"` — all unit tests pass
- [ ] `INFRA_GATE_RUN_SAFETY_E2E=1 dotnet test tests/InfraGate.Safety.E2E.Tests/ --filter "Category=SafetyE2E"` — all E2E tests pass (15 total)

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Base64 substring regex performance regression | Med | Use `[GeneratedRegex]` with `RegexOptions.CultureInvariant`. The pattern `[A-Za-z0-9+/]{20,}={0,2}` is cheap to evaluate. Only apply when `text` is not pure base64 (falls through existing fast path first). |
| Base64 substring false positives on Kubernetes resource names/UIDs | Low | Require minimum 20 chars (typical K8s UIDs are 8 chars). Require printable-UTF8 heuristic on decoded content. If a false positive occurs, it only adds a guardrail finding — it does not redact or block. |
| Real JWT parsing from password grant fails if Keycloak realm is misconfigured | Low | Same `AcquireTokenAsync` method already proven in `SmokeTests` and `FullApprovalFlowTests`. Second user `demo2` already exists in realm JSON from previous plan. |
| `ClaimsPrincipal` construction from parsed JWT is fragile if Keycloak changes claim names | Low | The fixture already reads `sub` and `client_id` tokens from JWT payloads in `ReadJwtSubject`. Standard claims are stable. |
