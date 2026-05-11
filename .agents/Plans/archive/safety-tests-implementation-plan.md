# Plan: Tests Proving the Safety Model (`minimum-for-demo.md` §6)

## Context

`minimum-for-demo.md` §6 requires tests that prove seven safety properties of the approval-gated mutation flow:

1. Plan hash mismatch fails
2. Expired approval fails
3. Already-applied plan fails
4. Dangerous manifest fails
5. Modified pending plan fails
6. Wrong user approval fails
7. Dry-run failure blocks approval/apply

The safety model is **already implemented** across [`InfraGate.Approvals`](../../src/InfraGate.Approvals/), [`InfraGate.McpServer`](../../src/InfraGate.McpServer/), and [`InfraGate.McpGateway`](../../src/InfraGate.McpGateway/), and many properties have unit-level coverage. The goal of this work is a **separate end-to-end test project** that exercises each property through the production code paths — real OAuth (Keycloak in a container), real gateway HTTP host, real McpServer subprocess, real Kubernetes API via developer kubeconfig — so the demo can be backed by tests that mirror how the gateway actually runs. The current branch is `feature/safety-tests`.

Two ground rules from the user shape this plan:

- **No copied helpers.** Tests construct production types via their real DI/wiring or call them through the real HTTP/MCP boundary. Existing test helpers in `InfraGate.McpServer.Tests` and `InfraGate.McpGateway.Tests` are not duplicated.
- **One file per workflow.** Each of the seven demo bullets gets its own test file (and likely its own class), so each safety property has a single readable demonstration. This respects [code-standards](../skills/code-standards/SKILL.md)' "one meaningful top-level type per file".

## New test project: `InfraGate.Safety.E2E.Tests`

Location: `tests/InfraGate.Safety.E2E.Tests/`. Sibling of the existing `InfraGate.McpGateway.KeycloakTests` and follows the same conventions ([KeycloakTests csproj](../../tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj) for Keycloak + TestHost; [McpServerIntegrationTests](../../tests/InfraGate.McpServer.Tests/IntegrationTests/McpServerIntegrationTests.cs) for the MCP subprocess pattern).

### Project shape

- `net10.0`, nullable + implicit usings on, xUnit 2.9.3.
- `FrameworkReference: Microsoft.AspNetCore.App` (for hosting the gateway in-process).
- Package references:
  - `Testcontainers.Keycloak` 4.11.0 (real OAuth issuer)
  - `Microsoft.AspNetCore.TestHost` 10.0.7 (host gateway in-process)
  - `ModelContextProtocol.Client` (MCP wire protocol for talking to the subprocess via the gateway)
  - `Microsoft.NET.Test.Sdk` 18.5.1, `xunit.runner.visualstudio` 3.1.5, `coverlet.collector` 10.0.0
- Project references:
  - `src/InfraGate.Approvals` — read pending plan files, audit log, approval state for assertions.
  - `src/InfraGate.McpGateway` — host gateway.
  - `src/InfraGate.McpGateway.Auth` — auth conventions / claim names.
  - `src/InfraGate.McpServer` — only for compile-time constants like `K8SMcpOptions.DefaultNamespace`; the server itself runs as a subprocess (matching production).
- Test data content: link `deploy/keycloak/infra-gate-realm.json` (already used by KeycloakTests).
- Add the project to [InfraGate.slnx](../../InfraGate.slnx) under the `/tests/` folder.
- Add `[Trait("Category", "SafetyE2E")]` at the class level so the default suite excludes them (mirroring `[Trait("Category", "Keycloak")]` on the existing project).

### Opt-in gate

Each test bails out early with `Skip.If` (or a simple `if/return`) when **either** of these is unmet, matching the `INFRA_GATE_RUN_INTEGRATION` pattern from `McpServerIntegrationTests`:

- `INFRA_GATE_RUN_SAFETY_E2E=1` env var set.
- `KUBECONFIG` (or default `~/.kube/config`) points at a reachable cluster with the demo namespace (default: `mcp-nginx-demo`, configurable via env var).

If env vars are unset, tests are silently skipped — same UX as `INFRA_GATE_RUN_INTEGRATION`. Docker is assumed available (Testcontainers handles Keycloak; failure surfaces as a fixture init error).

### Shared fixture (`SafetyE2EFixture : IAsyncLifetime`)

xUnit class fixture, lifecycle scoped to the assembly via `[CollectionDefinition]`. Sets up once, reused by all seven test files:

1. **Keycloak container**: `KeycloakBuilder(KeycloakImage).WithRealm(realmJsonPath).Build()` — exact pattern from [KeycloakIntegrationTests:41](../../tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs#L41). Exposes realm authority + token endpoint.
2. **Approval root**: temp directory per fixture instance.
3. **MCP server subprocess**: spawn `dotnet run --project src/InfraGate.McpServer/InfraGate.McpServer.csproj` with env vars `K8S_MCP_APPROVAL_ROOT`, `K8S_MCP_ALLOWED_NAMESPACES`, `KUBECONFIG` — pattern from [McpServerIntegrationTests:36-50](../../tests/InfraGate.McpServer.Tests/IntegrationTests/McpServerIntegrationTests.cs#L36-L50).
4. **Gateway TestHost**: `Microsoft.AspNetCore.TestHost` web server hosting the real `InfraGate.McpGateway` configured with the Keycloak realm as OAuth authority, the spawned McpServer process as downstream, and the same approval root. Pattern from [KeycloakIntegrationTests:`CreateGatewayServer`](../../tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs).
5. **Helpers exposed by the fixture** (purpose-specific, not generic copy-paste):
   - `AcquireTokenAsync(string clientId, string? scope)` — password-grant to Keycloak (already proven in KeycloakIntegrationTests).
   - `CreateHttpClient(string bearerToken)` — `server.CreateClient()` + bearer header.
   - `CallToolAsync(client, toolName, args)` — wraps the gateway MCP HTTP endpoint and returns the tool result text.
   - `ReadAuditEventsAsync()` — parses `audit.jsonl` for assertions on event types.

The fixture's purpose is plumbing only; tests use real services for the safety logic itself.

### File layout

```
tests/InfraGate.Safety.E2E.Tests/
  InfraGate.Safety.E2E.Tests.csproj
  README.md                                    # how to run, prerequisites, what's proven
  GlobalUsings.cs                              # Xunit, common namespaces
  SafetyE2EFixture.cs                          # Keycloak + gateway + McpServer process + helpers
  SafetyE2ECollection.cs                       # [CollectionDefinition] binding fixture
  Workflows/
    PlanHashMismatchTests.cs                   # Bullet #1
    ExpiredApprovalTests.cs                    # Bullet #2
    AlreadyAppliedPlanTests.cs                 # Bullet #3
    DangerousManifestTests.cs                  # Bullet #4
    ModifiedPendingPlanTests.cs                # Bullet #5
    WrongUserApprovalTests.cs                  # Bullet #6
    DryRunFailureTests.cs                      # Bullet #7
  TestData/
    infra-gate-realm.json                      # linked from deploy/keycloak/ (existing file)
```

One class per file (`sealed class XyzTests : IClassFixture<SafetyE2EFixture>`), file-scoped namespace `InfraGate.Safety.E2E.Tests.Workflows`. Test methods follow `Method_State_ExpectedResult`. Each class typically has 1–3 `[Fact]`s — the happy-path proof plus a tight variant where it strengthens the demo (e.g., `DryRunFailureTests` has one at request time and one at apply time).

## Task list

### Phase 1: Project scaffolding

- [ ] **Task 1**: Create `tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj` with the package and project references listed above. Link `deploy/keycloak/infra-gate-realm.json` as content.
- [ ] **Task 2**: Add the new project to [InfraGate.slnx](../../InfraGate.slnx) under `/tests/`.
- [ ] **Task 3**: `GlobalUsings.cs` (`Xunit`, `InfraGate.Approvals`, common namespaces).
- [ ] **Task 4**: `README.md` describing prerequisites (Docker, kubeconfig, `INFRA_GATE_RUN_SAFETY_E2E=1`), how to run, and a one-line mapping of file → demo bullet.
- [ ] **Verification**: `dotnet build InfraGate.slnx` clean.

### Phase 2: Fixture

- [ ] **Task 5**: `SafetyE2EFixture` — Keycloak container, McpServer subprocess, gateway TestHost, helpers. One `IAsyncLifetime` class. Mirrors keycloak fixture; spawns and shuts down the subprocess cleanly on dispose.
- [ ] **Task 6**: `SafetyE2ECollection.cs` — `[CollectionDefinition("SafetyE2E")]` referencing the fixture.
- [ ] **Verification**: write a 1-test smoke file inside `Workflows/` that just calls `get_allowed_namespaces` through the gateway with a real JWT and asserts a non-error response. Run it locally with `INFRA_GATE_RUN_SAFETY_E2E=1`.

### Phase 3: One workflow file per demo bullet

Each test class is decorated with `[Collection("SafetyE2E")]`. Each test starts with the env-var skip guard. Per-test approval root via `Guid.NewGuid()` subdirectory under the fixture root (cheap isolation; no cross-test contamination of `pending/`, `approved/`, `applied/`).

- [ ] **Task 7**: `PlanHashMismatchTests.cs` (bullet #1)
  - Workflow: request a scale plan via gateway → tamper the pending plan file → call `apply_approved_plan`. Expect refusal text mentioning the hash mismatch + `approval_hash_mismatch` (or `apply_denied`) audit event.
- [ ] **Task 8**: `ExpiredApprovalTests.cs` (bullet #2)
  - Workflow: request a plan → create approval challenge → manipulate the stored challenge to expire it (write `ExpiresAtUtc` in the past via `ApprovalChallengeStore`) → call approve. Expect refusal + `approval_challenge_expired` audit event.
- [ ] **Task 9**: `AlreadyAppliedPlanTests.cs` (bullet #3)
  - Workflow: request → approve (out-of-band write to `approved/`) → apply (succeeds against real K8s) → apply again. Expect second response is a refusal naming "already applied" + audit shows one `plan_applied` and one `apply_denied`.
- [ ] **Task 10**: `DangerousManifestTests.cs` (bullet #4)
  - Workflow: call `request_apply_manifest` with a manifest containing `securityContext.privileged: true`. Expect refusal text from the policy validator + no file in `pending/` + no `plan_requested` audit event.
- [ ] **Task 11**: `ModifiedPendingPlanTests.cs` (bullet #5)
  - Workflow: request → approve → mutate `pending/<id>.json` after approval → apply. Expect refusal text mentioning "changed after approval" + audit shows `apply_denied`.
- [ ] **Task 12**: `WrongUserApprovalTests.cs` (bullet #6)
  - Workflow: acquire token for user A, call `request_apply_manifest` (creates pending plan with challenge for A). Acquire token for user B, hit the approval HTTP endpoint as B. Expect HTTP refusal mentioning "same authenticated subject" + no `approved/` file written + approval challenge status not `approved`.
  - Requires Keycloak realm to have a second user. The existing realm JSON has `demo`; add a second user (e.g. `demo2`) to the realm JSON if not present, or use a second client. Confirm during implementation; update `infra-gate-realm.json` only if necessary, and only the new test project's linked copy if we want to avoid disturbing existing tests — otherwise update the source file (carefully).
- [ ] **Task 13**: `DryRunFailureTests.cs` (bullet #7)
  - Workflow A (request side): call `request_apply_manifest` with a manifest containing a field that fails admission/server-side validation (e.g. invalid replica count, or unknown field with `fieldValidation=Strict`). Expect refusal + no pending plan + `dry_run_failed` audit.
  - Workflow B (apply side): request a healthy plan, approve, then mutate cluster state so pre-apply dry-run fails (e.g. delete the target deployment between approval and apply). Expect refusal + `dry_run_failed` audit at the apply phase.

### Phase 4: Documentation and verification

- [ ] **Task 14**: Update the [AGENTS.md solution map](../../AGENTS.md) (Test projects list around lines 107-111) to include the new project.
- [ ] **Task 15**: Final verification:
  - `dotnet build InfraGate.slnx` — clean.
  - `dotnet test InfraGate.slnx --no-build --filter "Category!=Keycloak&Category!=SafetyE2E"` — default unit pass unchanged.
  - `INFRA_GATE_RUN_SAFETY_E2E=1 KUBECONFIG=… dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj` — all seven workflow tests green; verify Keycloak container starts and is disposed.

## Files touched

| Path | Change |
|---|---|
| `tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj` | new |
| `tests/InfraGate.Safety.E2E.Tests/GlobalUsings.cs` | new |
| `tests/InfraGate.Safety.E2E.Tests/README.md` | new |
| `tests/InfraGate.Safety.E2E.Tests/SafetyE2EFixture.cs` | new |
| `tests/InfraGate.Safety.E2E.Tests/SafetyE2ECollection.cs` | new |
| `tests/InfraGate.Safety.E2E.Tests/Workflows/*.cs` | new (7 files) |
| [InfraGate.slnx](../../InfraGate.slnx) | add project entry |
| [AGENTS.md](../../AGENTS.md) | add line to test-project list |
| `deploy/keycloak/infra-gate-realm.json` | possibly add a second user — only if `WrongUserApprovalTests` cannot be satisfied with the existing realm. Decided during Task 12. |

No existing source or test files are otherwise modified.

## Key existing code to read and use (not duplicate)

- [KeycloakIntegrationTests.cs](../../tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs) — Keycloak Testcontainer setup, `AcquireTokenAsync`, `CreateGatewayServer` pattern.
- [McpServerIntegrationTests.cs](../../tests/InfraGate.McpServer.Tests/IntegrationTests/McpServerIntegrationTests.cs) — McpServer subprocess spawn via `StdioClientTransport`, `INFRA_GATE_RUN_INTEGRATION` gating, `ApprovePlanAsync` semantics, `FindRepoRoot`.
- [Program.cs](../../src/InfraGate.McpServer/Program.cs) — production DI wiring to mirror in fixture if/where we host parts in-process.
- [K8sManager.Apply.cs:11](../../src/InfraGate.McpServer/K8sManager.Apply.cs#L11) `ApplyApprovedPlanAsync` — the apply flow under test.
- [K8sManager.Requests.cs:11](../../src/InfraGate.McpServer/K8sManager.Requests.cs#L11) `RequestApplyManifestAsync` — the request flow under test.
- [GatewayApprovalService.cs](../../src/InfraGate.McpGateway/GatewayApprovalService.cs) — challenge lifecycle (expired, wrong-subject).
- [ApprovalConventions.AuditEvents](../../src/InfraGate.Approvals/ApprovalConventions.cs#L22-L38) — event-name constants for audit assertions (no string literals in tests).

## Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Tests require Docker (Keycloak) + a real cluster | Med | Already the standing pattern for `KeycloakTests` and `IntegrationTests`. Default test pass excludes both via `--filter "Category!=Keycloak&Category!=SafetyE2E"`. README documents prerequisites. |
| Spawning McpServer subprocess slow per fixture | Low | One subprocess for the whole assembly, started in `IAsyncLifetime.InitializeAsync` and reused. |
| `KUBECONFIG` shared between tests → state leakage | Med | Each test creates a unique resource name within the allowed namespace and deletes it on teardown via the gateway's delete tool (or via direct K8s call in fixture cleanup). Avoid per-test namespace creation (slow). |
| Keycloak realm lacks a second user for bullet #6 | Med | Inspect `deploy/keycloak/infra-gate-realm.json` during Task 12. If missing, either add a user (small, well-scoped change), or use `mcp-client-limited` differently. Decision deferred to implementation. |
| `Microsoft.AspNetCore.TestHost` and a subprocess McpServer have to share the approval root path on disk | Low | Both processes read `K8S_MCP_APPROVAL_ROOT` from environment; fixture passes the same temp path to both. |
| `INFRA_GATE_RUN_SAFETY_E2E` is yet another env var | Low | Mirrors existing per-suite conventions (`INFRA_GATE_RUN_INTEGRATION`, `INFRA_GATE_RUN_GATEWAY_INTEGRATION`). New name signals new prerequisite set (Docker + K8s). |
| Solution file (`InfraGate.slnx`) is XML, not classic `.sln` | Low | Format observed; entry is a single `<Project Path="..." />` line under `/tests/`. |

## Verification

Done when:

1. `dotnet build InfraGate.slnx` is clean.
2. `dotnet test InfraGate.slnx --no-build --filter "Category!=Keycloak&Category!=SafetyE2E"` shows the default unit pass unchanged.
3. With Docker + a cluster: `INFRA_GATE_RUN_SAFETY_E2E=1 KUBECONFIG=.kube/mcp-nginx-demo.config dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj` runs all seven workflow tests green, with Keycloak started and disposed and the McpServer subprocess started and stopped exactly once.
4. The `Workflows/` directory contains exactly seven test files, one per demo bullet; AGENTS.md and the new README enumerate the mapping.
