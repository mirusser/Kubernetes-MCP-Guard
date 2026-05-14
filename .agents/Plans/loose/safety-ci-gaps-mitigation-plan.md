# Implementation Plan: Safety & CI Gaps — Mitigation

## Overview

Address six confirmed gaps across security testing (base64 guardrail bypass, real-JWT wrong-user coverage, plan-replay unit test) and CI infrastructure (Safety E2E workflow, CodeQL SAST, RBAC matrix tests). Each phase is independently testable.

### Gap Analysis: Reality vs. Perceived Gaps

| User's concern | Actually missing? | Reality |
|---|---|---|
| **1A. Plan replay attack** | Partially | Already protected — `ApprovalStore.GetApprovedPlanAsync` checks for `applied/` file. `AlreadyAppliedPlanTests` covers it end-to-end (request → approve → apply → re-apply → refusal). **Only gap**: no isolated unit test in `InfraGate.McpServer.Tests`. |
| **1B. Cross-user privilege escalation** | Partially | Same-subject enforcement already exists — User 2 CANNOT approve User 1's challenge. `WrongUserApprovalTests` proves this. **Real gap**: tests use synthetic injected subjects (`SetAuthenticatedSubject`), not real Keycloak JWT subjects from two distinct realm users. No second user in `infra-gate-realm.json`. |
| **1C. Chunk-boundary prompt injection** | No | The guardrail scans the complete assembled response (`Task<string>`), not a stream. There is no chunking to bypass. This is a hypothetical concern for a future streaming transport. |
| **1D. Base64 encoded payload bypass** | **Yes** | Zero base64 detection/decoding in `PromptInjectionGuard`. K8s Secret data (base64-encoded) is scanned as plaintext — malicious payloads encoded as base64 would bypass the guardrail entirely. |
| **2A. KinD in CI** | **Yes** | Integration tests use Minikube on a self-hosted runner. Safety E2E tests have no CI workflow at all. No ephemeral cluster created in-CI for the full safety test suite. |
| **2B. Headless browser (Playwright)** | Partially | Not implemented, but the fixture already drives the full browser approval flow through `HttpClient` (OAuth redirect → callback/cookie → GET approval page → POST approve with antiforgery). Playwright would add real HTML scraping but the critical logic is already tested. |
| **2C. SAST / Container scanning** | Partially | Trivy scan already runs in `package-docker.yml`. SonarCloud SAST already runs in `sonar.yml`. NuGet dependency scan already runs in `dependency-scan.yml`. **Only gap**: no CodeQL source analysis (only Trivy SARIF is uploaded to CodeQL dashboard — no actual CodeQL scan). |
| **2D. RBAC matrix** | **Yes** | Single SA (`infra-gate-mcp`), single Role, single namespace. No tests verify behavior under different permission levels (view-only, edit, admin). No second kubeconfig. |

## Architecture Decisions

- **No Playwright.** The fixture's `HttpClient`-driven approval flow already exercises the full browser approval path (redirect → login → callback/cookie → page render → antiforgery POST). Adding Playwright would slow CI significantly for marginal gain. Defer to a future hardening phase.
- **No chunk-boundary protection.** The transport is `Task<string>`, not `IAsyncEnumerable<string>`. There is no streaming to protect. File a tracking issue for if/when streaming transport is adopted.
- **Base64 decoding in guardrail, not in server.** The guardrail already owns response sanitization. Base64 decoding belongs at the guardrail layer, not per-tool in the server.
- **KinD, not Minikube, for Safety E2E CI.** Minikube requires a persistent self-hosted runner. KinD boots ephemerally inside any GitHub runner (including `ubuntu-latest`) and is the standard for Kubernetes CI testing.
- **Add a second user to existing realm JSON.** Modifying `deploy/keycloak/infra-gate-realm.json` is low-risk and lets real-JWT wrong-user E2E tests use real Keycloak identities instead of synthetic injection.
- **Keep both wrong-user tests.** The synthetic test covers edge cases (force principal mid-service-call) that real JWT can't. The real-JWT test proves the full pipeline. Defense-in-depth.
- **Safety E2E CI triggered by `workflow_dispatch`.** Full suite with Keycloak container + KinD + 12 tests takes 5-8 minutes. Manual trigger by default, PR-label gated for safety-impacting PRs.
- **RBAC matrix as an E2E test, not a separate workflow.** The fixture already manages kubeconfig. A new test file uses a second kubeconfig created from the same cluster with a different SA. No new workflow needed.

## Task List

### Phase 1: Test Gaps (code-level)

#### Task 1: Add base64 decoding to guardrail response sanitization

**Description:** Add base64 detection to `PromptInjectionGuard.SanitizeResponse` so that base64-encoded strings (especially from K8s Secret `data` fields) are decoded before regex pattern matching. The guardrail should detect strings that appear to be valid base64 (character set + length > 20 chars, multiple of 4), decode them, and scan the decoded content alongside the original text.

**Acceptance criteria:**
- [ ] Base64-encoded payload `"ignore previous instructions"` (e.g. `"aWtub3JlIHByZXZpb3VzIGluc3RydWN0aW9ucw=="`) is detected by the guardrail
- [ ] Plaintext payloads are still detected (no regression)
- [ ] K8s Secret `data` values are detected when they contain prompt-injection strings
- [ ] Decoding failures (invalid base64, non-UTF8 result) are silently skipped (don't crash the guardrail)
- [ ] Short strings (< 20 chars) are not decoded even if valid base64 (avoid false positives on IDs/tokens)

**Verification:**
- [ ] New unit tests in `PromptInjectionGuardTests` for base64-encoded ignore-instructions, reveal-prompts, secret-exfiltration
- [ ] Existing guardrail tests continue to pass
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` — green

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.McpGateway/PromptInjectionGuard.Sanitization.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/PromptInjectionGuardTests.cs`

**Estimated scope:** Small (1-2 files)

---

#### Task 2: Add second user to Keycloak realm and real-JWT wrong-user E2E test

**Description:** Add a second user (`demo2` / `demo2`) to `deploy/keycloak/infra-gate-realm.json`. Add a new `[Fact]` to `WrongUserApprovalTests` that creates a challenge as `demo` through the real Keycloak JWT + HTTP MCP pipeline, then attempts browser approval as `demo2` through the real approval OAuth backchannel with the second user's Keycloak identity token. Assert refusal with same-subject message. Keep existing synthetic-subject tests as defense-in-depth.

**Acceptance criteria:**
- [ ] `infra-gate-realm.json` has a second user with password grant enabled
- [ ] New test acquires real Keycloak JWTs for both users
- [ ] Challenge is created as user `demo` through HTTP MCP
- [ ] Browser approval as user `demo2` is refused with "same authenticated subject"
- [ ] Audit event `approval_challenge_rejected` is written with `demo2`'s subject
- [ ] Existing wrong-user tests (synthetic subject) continue to pass alongside the new real-JWT test

**Verification:**
- [ ] `INFRA_GATE_RUN_SAFETY_E2E=1 dotnet test tests/InfraGate.Safety.E2E.Tests/ --filter "FullyQualifiedName~WrongUserApproval"` — green
- [ ] Keycloak container starts with the updated realm JSON (both users present)

**Dependencies:** None

**Files likely touched:**
- `deploy/keycloak/infra-gate-realm.json`
- `tests/InfraGate.Safety.E2E.Tests/Workflows/WrongUserApprovalTests.cs`

**Estimated scope:** Small (2 files)

---

#### Task 3: Add unit test for already-applied plan replay refusal

**Description:** Add a unit test to `tests/InfraGate.McpServer.Tests/UnitTests/ApprovalStoreTests.cs` that calls `ApprovalStore.MarkAppliedAsync` on a plan, then asserts `GetApprovedPlanAsync` returns denial with "already applied" message. Also confirm `GetPendingPlanAsync` returns denial for the same plan. This fills the missing unit-test coverage — the E2E `AlreadyAppliedPlanTests` already covers the full gateway path, but the server-level store check has no isolated test.

**Acceptance criteria:**
- [ ] Test creates a pending plan, writes approval hash, marks as applied, then calls `GetApprovedPlanAsync` — assertion confirms `IsApproved == false` and message contains "already applied"
- [ ] Test also confirms `GetPendingPlanAsync` returns denial (the second code path)

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/ --filter "FullyQualifiedName~AlreadyApplied"` — green
- [ ] Full server test suite passes: `dotnet test tests/InfraGate.McpServer.Tests/`

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.McpServer.Tests/UnitTests/ApprovalStoreTests.cs`

**Estimated scope:** XS (1 file)

---

### Checkpoint: Test Gaps

- [ ] `dotnet test InfraGate.slnx --no-build --filter "Category!=Keycloak&Category!=SafetyE2E"` — all unit tests pass
- [ ] New guardrail tests detect base64-encoded payloads
- [ ] New server test catches already-applied rejection
- [ ] `INFRA_GATE_RUN_SAFETY_E2E=1 dotnet test tests/InfraGate.Safety.E2E.Tests/ --filter "Category=SafetyE2E"` — 13 tests pass (12 existing + 1 new real-JWT wrong-user)

---

### Phase 2: E2E CI Infrastructure (Safety E2E + RBAC)

#### Task 4: Create Safety E2E CI workflow with KinD

**Description:** Create `.github/workflows/safety-e2e.yml` that:
1. Creates a KinD cluster on `ubuntu-latest`
2. Applies `deploy/minikube/rbac.yaml` and `examples/failing-deployment/deployment.yaml`
3. Creates a kubeconfig from the KinD cluster
4. Runs the full Safety E2E test suite with `INFRA_GATE_RUN_SAFETY_E2E=1`
5. Tears down the KinD cluster on job completion

Gate on `workflow_dispatch` (manual trigger). No automatic PR trigger.

**Acceptance criteria:**
- [ ] Workflow creates a KinD cluster, applies RBAC + demo deployment, and boots within 5 minutes
- [ ] All 13 Safety E2E tests pass against the live KinD cluster
- [ ] Keycloak Testcontainer starts and is disposed cleanly
- [ ] Workflow fails fast if KinD or Keycloak setup fails
- [ ] KinD cluster is deleted on job completion (even on failure)

**Verification:**
- [ ] Manual `workflow_dispatch` run of the workflow — green
- [ ] KinD cluster logs show no errors
- [ ] Test output matches the expected 13 tests passing

**Dependencies:** Tasks 1, 2, 3 (tests should be complete before CI runs them)

**Files likely touched:**
- `.github/workflows/safety-e2e.yml` (new)

**Estimated scope:** Medium (1 workflow file, 3-5 steps)

---

#### Task 5: RBAC matrix — add read-only SA and E2E test for forbidden mutations

**Description:** Add a read-only ServiceAccount (`infra-gate-mcp-view`) to `deploy/minikube/rbac.yaml` with only `get/list/watch` on the allowed resources. Extend `scripts/create-demo-kubeconfig.sh` with a `--sa-name` flag. Add `RbacMatrixTests.cs` to the Safety E2E suite that uses this read-only kubeconfig: verify that `request_apply_manifest` succeeds (it only does `dryRun=All`, which is read-only), but `apply_approved_plan` returns a K8s 403 Forbidden because the SA lacks write verbs. This proves the gateway inherits the SA's RBAC and doesn't bypass it.

**Acceptance criteria:**
- [ ] `rbac.yaml` gains a read-only SA + Role + RoleBinding with no create/patch/delete/update verbs
- [ ] `create-demo-kubeconfig.sh` accepts a `--sa-name` flag to generate kubeconfigs for different SAs
- [ ] New test: read-only kubeconfig → `request_apply_manifest` succeeds → approve plan → `apply_approved_plan` returns 403 Forbidden from Kubernetes
- [ ] Existing tests continue to use the full-permission SA (no regression)

**Verification:**
- [ ] `INFRA_GATE_RUN_SAFETY_E2E=1 dotnet test tests/InfraGate.Safety.E2E.Tests/ --filter "FullyQualifiedName~RbacMatrix"` — green
- [ ] `kubectl auth can-i create deployments -n mcp-nginx-demo --as=system:serviceaccount:mcp-nginx-demo:infra-gate-mcp-view` returns `no`

**Dependencies:** None (can be done in parallel with Task 4)

**Files likely touched:**
- `deploy/minikube/rbac.yaml`
- `scripts/create-demo-kubeconfig.sh`
- `tests/InfraGate.Safety.E2E.Tests/Workflows/RbacMatrixTests.cs` (new)

**Estimated scope:** Medium (3 files)

---

### Phase 3: CI/CD — CodeQL

#### Task 6: Add CodeQL SAST scanning

**Description:** Add `github/codeql-action` (init → autobuild → analyze) to a new workflow `codeql.yml`. CodeQL for C# detects injection flaws, improper authorization checks, and other OWASP-category vulnerabilities at the source level — complementing SonarCloud's code-quality focus. Upload results as SARIF to GitHub's security tab.

**Acceptance criteria:**
- [ ] CodeQL analysis runs on push to `main`/`dev` and PRs
- [ ] C# codebase is analyzed for security vulnerabilities
- [ ] Results appear in GitHub's Security → Code scanning tab
- [ ] Does not duplicate SonarCloud coverage (CodeQL = security-specific, Sonar = quality)

**Verification:**
- [ ] `codeql-action/analyze` step completes without errors
- [ ] SARIF upload appears in GitHub Security tab

**Dependencies:** None (runs independently)

**Files likely touched:**
- `.github/workflows/codeql.yml` (new)

**Estimated scope:** XS (1 file)

---

### Phase 4: Documentation

#### Task 7: Update Safety E2E README with new coverage

**Description:** Add entries to the README's "what it covers" table for the new tests (real-JWT wrong-user test, RBAC matrix test). Update the "Test architecture" section to document base64 guardrail detection. Add a CI section describing the `safety-e2e.yml` workflow with KinD and `workflow_dispatch` trigger.

**Acceptance criteria:**
- [ ] README table lists new test files (`RbacMatrixTests.cs`, updated `WrongUserApprovalTests.cs`)
- [ ] Test architecture section mentions base64 decoding in guardrail
- [ ] CI section documents `workflow_dispatch` trigger and KinD requirement
- [ ] Section on guardrail mentions base64-decode capability

**Verification:**
- [ ] `git diff --check` clean
- [ ] README links are valid

**Dependencies:** Tasks 1-6

**Files likely touched:**
- `tests/InfraGate.Safety.E2E.Tests/README.md`

**Estimated scope:** XS (1 file)

---

### Checkpoint: Complete

- [ ] `dotnet build InfraGate.slnx` — clean
- [ ] `dotnet test InfraGate.slnx --no-build --filter "Category!=Keycloak&Category!=SafetyE2E"` — all unit tests pass
- [ ] `INFRA_GATE_RUN_SAFETY_E2E=1 dotnet test tests/InfraGate.Safety.E2E.Tests/ --filter "Category=SafetyE2E"` — all E2E tests pass (14 total: 12 existing + new wrong-user + new RBAC matrix)
- [ ] `safety-e2e.yml` workflow run — green on KinD
- [ ] `codeql.yml` scan results visible in GitHub Security tab

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Base64 false-positives (decoding short strings that happen to be valid base64) | Med | Only decode strings > 20 chars with valid base64 charset; decode to UTF8 and scan result; skip if result is non-printable garbage |
| KinD startup time exceeds GitHub runner timeout | Med | Use `kindest/node` image with pre-pulled images; cache with `actions/cache`; KinD boots in ~60-90s cold start |
| `infra-gate-realm.json` second-user changes break existing Keycloak test fixtures | Low | Both Safety E2E and `KeycloakTests` use the same realm JSON. Adding one user object is additive — no existing test references the second user |
| CodeQL scans produce duplicative noise alongside SonarCloud | Low | CodeQL focuses on security; Sonar on quality. Slight overlap in vulnerability detection is acceptable defense-in-depth |
| `create-demo-kubeconfig.sh --sa-name` flag conflicts with existing script logic | Low | The script already creates one SA token per run. `--sa-name` just changes which SA is used. Backward-compatible when omitted |

## Files Touched Summary

| Path | Change |
|---|---|
| `src/InfraGate.McpGateway/PromptInjectionGuard.Sanitization.cs` | Add base64 detection/decoding |
| `tests/InfraGate.McpGateway.Tests/UnitTests/PromptInjectionGuardTests.cs` | Add base64-encoded injection tests |
| `deploy/keycloak/infra-gate-realm.json` | Add second user `demo2` |
| `tests/InfraGate.Safety.E2E.Tests/Workflows/WrongUserApprovalTests.cs` | Add real-JWT wrong-user test |
| `tests/InfraGate.McpServer.Tests/UnitTests/ApprovalStoreTests.cs` | Add already-applied replay test |
| `.github/workflows/safety-e2e.yml` | New: KinD + Safety E2E CI workflow |
| `.github/workflows/codeql.yml` | New: CodeQL SAST for C# |
| `deploy/minikube/rbac.yaml` | Add read-only SA + Role + RoleBinding |
| `scripts/create-demo-kubeconfig.sh` | Add `--sa-name` flag |
| `tests/InfraGate.Safety.E2E.Tests/Workflows/RbacMatrixTests.cs` | New: RBAC matrix E2E test |
| `tests/InfraGate.Safety.E2E.Tests/README.md` | Update table and architecture docs |
