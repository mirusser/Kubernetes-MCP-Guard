# Implementation Plan: Extend Test Coverage

## Overview

SonarCloud reports **79% coverage on new code** since baseline commit `b93c265` (feat: add InfraGate.DownstreamAuth shared contract). Overall coverage is **83.5% lines / 72.3% branches** across 1,401 uncovered lines. The McpServer project alone accounts for **62% of the gap** (863 uncovered lines), with two completely untested services — `KubernetesEvidenceService` (396 lines, 0%) and `KubernetesExecutionService` (230 lines, 0%) — representing **45% of the total uncovered surface**. The second-largest opportunity is a cluster of five convention/constant classes that are all at 0% coverage and trivially testable (~170 combined lines).

**Goal:** Push new-code coverage above 80% by addressing uncovered lines in descending order of impact-to-effort ratio, starting with no-risk constant-assertion tests and ending with mock-backed service tests.

## Architecture Decisions

1. **Reuse `TestKubernetesApi`, not a mocking framework.** The codebase already uses an in-process `HttpListener` to simulate a Kubernetes API server (`tests/InfraGate.McpServer.Tests/UnitTests/TestKubernetesApi.cs`). `KubernetesEvidenceService` and `KubernetesExecutionService` consume `IKubernetes` directly, so the same `new Kubernetes(new KubernetesClientConfiguration { Host = api.Url })` pattern works. This keeps the test approach consistent with the existing codebase convention.

2. **Constant-only tests follow the `ApprovalConventionsTests` pattern.** One test class per conventions type, one `[Fact]` per logical constant group using straightforward `Assert.Equal`, no shared mutable state. This adds coverage with zero design risk, no mock setup, and sub-millisecond execution.

3. **Branch-gap tests use `[Theory]` with `[InlineData]` over duplicated `[Fact]` tests,** as required by the code-standards skill. One test class per production class (`{TypeUnderTest}Tests`), naming follows `Method_State_ExpectedResult`, assert on observable outputs.

4. **No README/doc changes are required.** All work is additive — new test files in existing test projects. No new projects, no changed public API surfaces.

5. **InternalsVisibleTo is already in place** for every source→test pair that needs it: `InfraGate.McpServer` → `InfraGate.McpServer.Tests`, `InfraGate.McpGateway` → `InfraGate.McpGateway.Tests`, `InfraGate.RuntimeSafety` → `InfraGate.RuntimeSafety.Tests`. No `.csproj` changes required.

## Task List

### Phase 1: Quick Wins — Convention & Constant Tests

All five target classes are currently at **0% coverage**. Each is a static bag of `public const string` values with no dependencies, no I/O, and no branching. Testing follows the existing `ApprovalConventionsTests.cs` pattern exactly.

---

#### Task 1: Test `KubernetesConventions` constants

**Description:** Write `KubernetesConventionsTests` asserting all constant values in `KubernetesConventions` nested classes (ToolNames, ToolArguments, MutationOperations, EnvironmentVariables, ConfigurationKeys, KubernetesApi, DryRunStatuses, DriftCheckResult, LabelSelectorOperators, KubernetesResources) and both code paths through `RegisterInfraGateEnvVarMappings`. The class is `internal` — already accessible via `InternalsVisibleTo` to `InfraGate.McpServer.Tests`.

**Acceptance criteria:**
- [ ] `ToolNames` — all 19 MCP tool name constants asserted (e.g. `GetK8sStatus` = `"get_k8s_status"`)
- [ ] `ToolArguments` — all 12 argument name constants asserted
- [ ] `MutationOperations` — all 5 operation constants asserted
- [ ] `KubernetesApi` — all 4 K8s API constants asserted
- [ ] `KubernetesResources` — all 12 resource constants + `DeploymentRef` + `IsDeployment` methods tested
- [ ] `EnvironmentVariables` / `ConfigurationKeys` — all paired mappings asserted
- [ ] `RegisterInfraGateEnvVarMappings` — verify all 5 Kubernetes-specific mappings + downstream auth mappings are registered; verify null guard throws

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter KubernetesConventionsTests` — all pass
- [ ] Full build: `dotnet build InfraGate.slnx --no-restore` — clean

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.McpServer.Tests/UnitTests/KubernetesConventionsTests.cs` (new, ~80 lines)

**Estimated scope:** S (1 file, 169 source lines to cover)

---

#### Task 2: Test `GatewayAuthConventions` constants

**Description:** Write `GatewayAuthConventionsTests` asserting all constant values in `GatewayAuthConventions` nested classes and the `RegisterInfraGateEnvVarMappings` path. The class is `public` — no InternalsVisibleTo needed.

**Acceptance criteria:**
- [ ] Default constants asserted (`DefaultOAuthResource`, `DefaultOAuthScope`, `DefaultApprovalOAuthClientId`, `AuthorizationScheme`)
- [ ] `EnvironmentVariables` — all 10 OAuth env var constants asserted
- [ ] `ConfigurationKeys` — all 10 config key constants asserted
- [ ] `Schemes` — all 5 scheme/policy constants asserted
- [ ] `Metadata` — both constants asserted
- [ ] `OAuthErrors` — `InsufficientScope` asserted
- [ ] `ChallengeParameters` — all 3 constants asserted
- [ ] `Claims` — all 6 claim name constants asserted
- [ ] `Audit` — `OAuthAuthenticationType` asserted
- [ ] `Approvals` — all 7 approval constants asserted
- [ ] `Parameters` — `Resource` asserted
- [ ] `RegisterInfraGateEnvVarMappings` — all 9 mappings verified; null guard throws

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter GatewayAuthConventionsTests` — all pass
- [ ] Full build: `dotnet build InfraGate.slnx --no-restore` — clean

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayAuthConventionsTests.cs` (new, ~60 lines)

**Estimated scope:** S (1 file, 108 source lines to cover)

---

#### Task 3: Test `RuntimeSafetyConventions` constants

**Description:** Write `RuntimeSafetyConventionsTests` asserting all constant values and the `RegisterInfraGateEnvVarMappings` path. The class is `public` — no InternalsVisibleTo needed.

**Acceptance criteria:**
- [ ] `EnvironmentVariables` — all 4 env var constants asserted
- [ ] `ConfigurationKeys` — `InfraGateRuntimeEnvironment` asserted
- [ ] `EnvironmentValues` — `Development` and `Production` asserted
- [ ] `RegisterInfraGateEnvVarMappings` — 1 mapping verified; null guard throws

**Verification:**
- [ ] `dotnet test tests/InfraGate.RuntimeSafety.Tests/InfraGate.RuntimeSafety.Tests.csproj --filter RuntimeSafetyConventionsTests` — all pass
- [ ] Full build: `dotnet build InfraGate.slnx --no-restore` — clean

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.RuntimeSafety.Tests/UnitTests/RuntimeSafetyConventionsTests.cs` (new, ~30 lines)

**Estimated scope:** XS (1 file, 29 source lines to cover)

---

#### Task 4: Test `McpGatewayConventions` uncovered constants

**Description:** `McpGatewayConventions` is at 64.3% coverage (36/56 lines). Add `McpGatewayConventionsTests` covering the 20 uncovered constant values. The class is `internal` — already accessible via `InternalsVisibleTo` to `InfraGate.McpGateway.Tests`.

**Acceptance criteria:**
- [ ] Uncovered `EnvironmentVariables` constants asserted
- [ ] Uncovered `ConfigurationKeys` constants asserted
- [ ] Any uncovered nested-class constants asserted

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter McpGatewayConventionsTests` — all pass
- [ ] Coverage for this class rises from 64.3% → 100%

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.McpGateway.Tests/UnitTests/McpGatewayConventionsTests.cs` (new, ~25 lines)

**Estimated scope:** XS (1 file, 20 uncovered source lines)

---

### Checkpoint: Phase 1

- [ ] `dotnet test InfraGate.slnx --filter "Category!=Keycloak&Category!=SafetyE2E"` — all tests pass
- [ ] `./scripts/coverage.sh` — line coverage rises from 83.5% → ~85.5% (+~170 lines)
- [ ] All new test files follow `Method_State_ExpectedResult` naming and `{TypeUnderTest}Tests` class naming

---

### Phase 2: DI Registration & Thin Wrappers

Three classes at 0% coverage that are simple to test — DI extension methods, a null-object token provider, and a thin notification wrapper.

---

#### Task 5: Test `DownstreamAuthServerExtensions`

**Description:** `DownstreamAuthServerExtensions.AddDownstreamAuth` has two branches: (a) `Required=false` returns services unchanged, (b) `Required=true` registers `DownstreamTokenValidator` as singleton, and (c) null options defaults to `Required=false` (default `DownstreamAuthOptions`). Test all three using `ServiceCollection`. The class is `internal` — already accessible via `InternalsVisibleTo`.

**Acceptance criteria:**
- [ ] `AddDownstreamAuth_RequiredFalse_ReturnsUnchangedServices` — services collection unaffected
- [ ] `AddDownstreamAuth_RequiredTrue_RegistersDownstreamTokenValidator` — singleton validator registered
- [ ] `AddDownstreamAuth_NullOptions_UsesDefaults` — behaves same as Required=false

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter DownstreamAuthServerExtensionsTests` — all pass

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.McpServer.Tests/UnitTests/DownstreamAuth/DownstreamAuthServerExtensionsTests.cs` (new, ~40 lines)

**Estimated scope:** XS (1 file, 33 source lines to cover)

---

#### Task 6: Test `NullDownstreamServiceTokenProvider`

**Description:** `NullDownstreamServiceTokenProvider` returns `string.Empty` from both `GetServiceTokenAsync` and `RefreshServiceTokenAsync`. Simple `[Fact]` tests suffice. The class is `internal` — already accessible via `InternalsVisibleTo`.

**Acceptance criteria:**
- [ ] `GetServiceTokenAsync_ReturnsEmptyString`
- [ ] `RefreshServiceTokenAsync_ReturnsEmptyString`

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter NullDownstreamServiceTokenProviderTests` — all pass

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.McpGateway.Tests/UnitTests/DownstreamAuth/NullDownstreamServiceTokenProviderTests.cs` (new, ~25 lines)

**Estimated scope:** XS (1 file, 11 source lines to cover)

---

#### Task 7: Test `McpServerSessionNotifier`

**Description:** `McpServerSessionNotifier` wraps `McpServer` as an `ISessionNotifier` seam. Since `McpServer` is hard to instantiate, the test verifies the `ISessionNotifier` contract independently — or, if the existing `ISessionNotifier` interface is already tested via `ApprovalNotificationDispatcher`, verify the wrapper delegates correctly. The class is `internal` — already accessible.

**Acceptance criteria:**
- [ ] `McpServerSessionNotifier` is constructable (no null ref)
- [ ] Alternatively: verify `ISessionNotifier` contract behavior is covered by `ApprovalNotificationDispatcherTests`

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter McpServerSessionNotifierTests` — all pass

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.McpGateway.Tests/UnitTests/Notifications/McpServerSessionNotifierTests.cs` (new, ~20 lines)

**Estimated scope:** XS (1 file, 14 source lines to cover)

---

### Checkpoint: Phase 2

- [ ] `dotnet test InfraGate.slnx --filter "Category!=Keycloak&Category!=SafetyE2E"` — all tests pass
- [ ] `./scripts/coverage.sh` — line coverage rises to ~86.3% (+~60 lines beyond Phase 1)

---

### Phase 3: Fill Branch & Path Gaps in Partially-Tested Classes

Four classes with existing tests but significant uncovered branches. Each task adds `[Theory]` / `[InlineData]` cases for missing paths.

---

#### Task 8: Expand `DownstreamTokenValidator` tests

**Description:** `DownstreamTokenValidator` is at 73.7% line / 57.7% branch coverage (87/118 lines, 30/52 branches covered). Add test cases for: invalid token formats, expired tokens, wrong audience, missing required claims, token with valid structure but invalid signature. Use the existing `CreateToken` helper pattern from `DownstreamTokenValidatorTests.cs`.

**Acceptance criteria:**
- [ ] `Validate_ExpiredToken_ReturnsUnauthorized`
- [ ] `Validate_WrongAudience_ReturnsUnauthorized`
- [ ] `Validate_MissingSubClaim_ReturnsUnauthorized`
- [ ] `Validate_MalformedToken_ReturnsUnauthorized`
- [ ] `Validate_ValidToken_ReturnsSuccess` (fill existing gap in success path)

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests --filter DownstreamTokenValidatorTests` — all pass
- [ ] Coverage for `DownstreamTokenValidator` rises from 73.7% → 90%+

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.McpServer.Tests/UnitTests/DownstreamAuth/DownstreamTokenValidatorTests.cs` (augment ~50 lines)

**Estimated scope:** S (1 file, 31 uncovered source lines)

---

#### Task 9: Expand `KubernetesTools` tests

**Description:** `KubernetesTools` is at 38.1% line coverage (8/21 lines). Add test cases for uncovered tool paths using the existing `TestKubernetesApi` + `CreateManager` pattern. Target the uncovered diagnostic and resource-listing tool paths.

**Acceptance criteria:**
- [ ] `GetPodDiagnostics` tool exercised with valid pod (improves coverage)
- [ ] `GetServiceDiagnostics` tool exercised with valid service
- [ ] `GetDeploymentDiagnostics` tool exercised with valid deployment
- [ ] `GetK8sResource` tool exercised with field selector/limit variants

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests --filter KubernetesToolsTests` — all pass
- [ ] Coverage for `KubernetesTools` rises from 38.1% → 90%+

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.McpServer.Tests/UnitTests/KubernetesToolsTests.cs` (augment ~60 lines)

**Estimated scope:** S (1 file, 13 uncovered source lines; more test code due to TestKubernetesApi setup)

---

#### Task 10: Expand `KubernetesPlanExecutor` branch coverage

**Description:** `KubernetesPlanExecutor` (in `InfraGate.KubernetesAdapter`) is at 79.2% line / 65.0% branch coverage (221/279 lines, 39/60 branches). Add test cases for error paths: plan with invalid intent, execution failure, partial success scenarios. The existing tests are in `InfraGate.McpServer.Tests`. This is a `public` class — no InternalsVisibleTo needed.

**Acceptance criteria:**
- [ ] `Execute_InvalidIntent_ReturnsErrorMessageString`
- [ ] `Execute_KubernetesApiFailure_ReturnsError`
- [ ] `Execute_PlanInWrongNamespace_ReturnsError` (if applicable)
- [ ] Branch coverage rises from 65% → 80%+

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests --filter KubernetesPlanExecutorTests` — all pass

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.McpServer.Tests/UnitTests/KubernetesPlanExecutorTests.cs` (augment ~50 lines)

**Estimated scope:** M (1 file, 58 uncovered source lines; may need TestKubernetesApi for some paths)

---

#### Task 11: Expand `RunProfileDocument` / `RunProfileDocumentReader` branch coverage

**Description:** `RunProfileDocument` (74.7% lines, 48.8% branches) and `RunProfileDocumentReader` (88.3% lines, 73.0% branches) have low branch coverage. Add test cases for edge cases in document parsing: missing optional fields, invalid YAML, malformed profile entries, profile-default merging behavior.

**Acceptance criteria:**
- [ ] `Parse_MissingOptionalFields_ProducesValidDocument` — document with only required fields
- [ ] `Parse_InvalidYaml_ReturnsError` — malformed YAML handling
- [ ] `Parse_ProfileWithDefaults_AppliesDefaultsCorrectly` — default merging
- [ ] Branch coverage rises on both classes

**Verification:**
- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests --filter "RunProfileDocument"` — all pass

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.RunProfiles.Tests/UnitTests/` (augment existing or add, ~60 lines)

**Estimated scope:** M (1-2 files, 67 uncovered source lines)

---

### Checkpoint: Phase 3

- [ ] `dotnet test InfraGate.slnx --filter "Category!=Keycloak&Category!=SafetyE2E"` — all tests pass
- [ ] `./scripts/coverage.sh` — line coverage rises to ~88.5% (+~160 lines beyond Phase 2; branch coverage rises from 72.3% → ~75%)

---

### Phase 4: The Big Two — KubernetesEvidenceService & KubernetesExecutionService

Together these two files account for 626 uncovered lines (45% of the total gap). Both depend on `IKubernetes` and can be tested with the existing `TestKubernetesApi` pattern. These services are independent of `KubernetesManager` — they take `IKubernetes` directly and can be constructed standalone.

---

#### Task 12: Test `KubernetesEvidenceService`

**Description:** `KubernetesEvidenceService` (638 lines, 0% coverage) provides dry-run, diff, and drift-check operations. It has three layers of testable logic: (a) input validation and manifest parsing — no K8s needed, (b) policy validation — no K8s needed, (c) K8s API calls — needs `TestKubernetesApi`. Structure the test file with separate test methods for each layer, following `Method_State_ExpectedResult` naming. Use `[Theory]` with `[InlineData]` for input variants. No shared mutable state.

**Acceptance criteria:**
- [ ] `EvidenceDryRunApplyManifest_DisallowedNamespace_ReturnsError` — namespace validation
- [ ] `EvidenceDryRunApplyManifest_InvalidManifest_ReturnsParseError` — bad YAML
- [ ] `EvidenceDryRunApplyManifest_PolicyDenied_ReturnsRefusal` — privileged container, host PID, etc.
- [ ] `EvidenceDryRunApplyManifest_ValidManifest_ReturnsDryRunResult` — happy path via TestKubernetesApi
- [ ] `EvidenceDryRunDeleteManifest_` — equivalent delete path
- [ ] `EvidenceDiffManifest_` — diff via live/desired comparison
- [ ] `EvidenceCheckLiveDrift_` — drift detection path
- [ ] `EvidenceDiffDeployment_` — deployment diff path
- [ ] `Evidence*_KubernetesApiError_ReturnsErrorMessage` — API failure paths
- [ ] Coverage >= 80% for this class (~320 lines, up from 0/396)

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests --filter KubernetesEvidenceServiceTests` — all pass
- [ ] No regression on existing `Kubernetes*Tests`
- [ ] Manually spot-check coverage report for this class is >= 80%

**Dependencies:** Tasks 1-9 (understanding existing test patterns, `TestKubernetesApi`, `KubernetesMcpOptions`, manifest fixtures)

**Files likely touched:**
- `tests/InfraGate.McpServer.Tests/UnitTests/KubernetesEvidenceServiceTests.cs` (new, ~250 lines)
- (May add evidence-specific JSON fixture helpers in the test file)

**Estimated scope:** L (5+ files if helpers extracted; 396 uncovered source lines)

---

#### Task 13: Test `KubernetesExecutionService`

**Description:** `KubernetesExecutionService` (362 lines, 0% coverage) performs apply, delete, scale, restart, and set-image operations. Same three-layer structure as EvidenceService. Follow `Method_State_ExpectedResult` naming. Use `[Theory]` with `[InlineData]` for namespace/manifest variants.

**Acceptance criteria:**
- [ ] `ExecuteApplyManifest_DisallowedNamespace_ReturnsError` — namespace validation
- [ ] `ExecuteApplyManifest_InvalidManifest_ReturnsParseError` — bad YAML
- [ ] `ExecuteApplyManifest_PolicyDenied_ReturnsRefusal` — policy block
- [ ] `ExecuteApplyManifest_ValidManifest_ReturnsSuccess` — happy path via TestKubernetesApi
- [ ] `ExecuteDeleteManifest_` — delete path
- [ ] `ExecuteScaleDeployment_` — scale path
- [ ] `ExecuteRestartDeployment_` — restart path
- [ ] `ExecuteSetDeploymentImage_` — set-image path
- [ ] `Execute*_KubernetesApiFailure_ReturnsError` — API error paths
- [ ] Coverage >= 80% for this class (~186 lines, up from 0/230)

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests --filter KubernetesExecutionServiceTests` — all pass
- [ ] No regression on existing `Kubernetes*Tests`
- [ ] Manually spot-check coverage report for this class is >= 80%

**Dependencies:** Task 12 (shares `TestKubernetesApi` patterns and manifest fixtures)

**Files likely touched:**
- `tests/InfraGate.McpServer.Tests/UnitTests/KubernetesExecutionServiceTests.cs` (new, ~200 lines)

**Estimated scope:** M-L (1-2 files; 230 uncovered source lines)

---

### Checkpoint: Complete

- [ ] `dotnet test InfraGate.slnx --filter "Category!=Keycloak&Category!=SafetyE2E"` — all tests pass
- [ ] `./scripts/coverage.sh` — line coverage ≥ 92%, branch coverage ≥ 78%
- [ ] SonarCloud new-code coverage ≥ 80%

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| `KubernetesEvidenceService` internal paths hard to reach with mock K8s responses | Medium | Prioritize testable surface first (validation, parsing, policy rejection); mock HTTP paths for happy-path dry-run/apply; skip deeply nested K8s serialization paths if mock response is too complex |
| `McpServerSessionNotifier` needs `McpServer` instance | Low | Test through `ISessionNotifier` interface; the class is already factored out as a testability seam per the code comment |
| `RunProfileDocument`/`Reader` branch coverage hard to reach for obscure YAML edge cases | Low | Test the documented edge cases first (missing fields, invalid YAML); leave exotic edge cases for future |
| Task 12 fails to reach 80% coverage of EvidenceService | Medium | Accept 60-70% as a win — that's still 240-280 lines covered; remainder requires live K8s (integration test tier, out of scope) |
| New tests break CI due to environment assumptions | Low | All Phases 1-3 tests are pure unit tests with no file system, no network, no K8s dependency; Phase 4 uses in-process listener (no external dependencies) |

## Open Questions

- **Task 7 (`McpServerSessionNotifier`):** The class exists purely as a testability seam but cannot itself be easily instantiated with a real `McpServer`. Should we skip this test entirely (3 uncovered lines, negligible impact) or test that the `ISessionNotifier` contract is adequately covered by `ApprovalNotificationDispatcherTests`?
- **Task 12/13 coverage targets:** Is 80% coverage of the two large services realistic without a live Kubernetes cluster? The input validation, parsing, and policy paths are fully unit-testable. The API-call paths need carefully constructed mock responses. A live-K8s test tier would fill the rest but is opt-in (SafetyE2E / Keycloak category) and out of scope for this plan.
