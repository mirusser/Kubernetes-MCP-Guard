# Implementation Plan: Split Keycloak Tests Into Dedicated Project

## Overview

Separate Keycloak-backed OIDC integration tests from the regular gateway test project while keeping them visible in the solution build. The new project will own all Testcontainers.Keycloak dependencies and realm test data. Unit and coverage CI will exclude `Category=Keycloak`; the dedicated Keycloak workflow will restore, build, and run only the Keycloak project.

Success criteria:

- Keycloak tests live in `tests/InfraGate.McpGateway.KeycloakTests`.
- `InfraGate.McpGateway.Tests` no longer references `Testcontainers.Keycloak` or Keycloak realm test data.
- `InfraGate.slnx` includes the new project.
- Unit and Sonar workflows do not execute Keycloak tests.
- The Keycloak workflow executes only the dedicated Keycloak test project.
- Local verification passes for build, filtered unit tests, Keycloak test discovery, and live Keycloak tests when Docker is available.

## Architecture Decisions

- Create `InfraGate.McpGateway.KeycloakTests` rather than keeping provider-specific integration tests in `InfraGate.McpGateway.Tests`.
- Keep the new project in `InfraGate.slnx` so solution builds catch compile errors.
- Keep `[Trait("Category", "Keycloak")]` on the Keycloak test class so CI filters remain explicit and readable.
- Move `Testcontainers.Keycloak` and the realm JSON content item into the new project only.
- Use Testcontainers.Keycloak's `.WithRealm(...)` helper for realm import instead of manually composing Keycloak startup arguments.
- Add `InternalsVisibleTo Include="InfraGate.McpGateway.KeycloakTests"` only where required by the moved tests.

## Task List

### Task 1: Create Dedicated Test Project

Description: Add `tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj` with the dependencies and project references required for Keycloak OIDC integration tests.

Acceptance criteria:

- The project targets `net10.0`, enables nullable and implicit usings, and is not packable.
- The project references `InfraGate.Approvals`, `InfraGate.McpGateway.Auth`, and `InfraGate.McpGateway`.
- The project owns the `Testcontainers.Keycloak` package reference.
- The project links `deploy/keycloak/infra-gate-realm.json` to `TestData/infra-gate-realm.json` with `CopyToOutputDirectory=PreserveNewest`.

Verification:

```bash
dotnet restore tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj
```

Files likely touched:

- `tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj`
- `InfraGate.slnx`

### Task 2: Move Keycloak Test Class

Description: Move `KeycloakIntegrationTests.cs` from `InfraGate.McpGateway.Tests` into the new Keycloak test project and update namespace/import behavior.

Acceptance criteria:

- `KeycloakIntegrationTests` lives under `tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/`.
- Namespace is `InfraGate.McpGateway.KeycloakTests.IntegrationTests`.
- `[Trait("Category", "Keycloak")]` remains on the class.
- Keycloak startup uses `.WithRealm(realmJsonPath)`.
- The tests still cover:
  - valid Keycloak token accepted by gateway auth;
  - wrong audience rejected;
  - valid audience but missing `mcp:tools` scope rejected.

Verification:

```bash
dotnet build InfraGate.slnx --configuration Release --no-restore
```

Files likely touched:

- `tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs`
- `tests/InfraGate.McpGateway.Tests/IntegrationTests/KeycloakIntegrationTests.cs`

### Task 3: Remove Keycloak Coupling From Gateway Tests

Description: Remove Keycloak-specific dependencies and test data from `InfraGate.McpGateway.Tests`.

Acceptance criteria:

- `tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` has no `Testcontainers.Keycloak` package reference.
- `tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` no longer links `infra-gate-realm.json`.
- Gateway unit and non-Keycloak integration tests still build and run.

Verification:

```bash
rg -n "Testcontainers.Keycloak|infra-gate-realm.json" tests/InfraGate.McpGateway.Tests --glob '!**/bin/**' --glob '!**/obj/**'
```

Expected result: no matches.

Files likely touched:

- `tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`

### Task 4: Add Friend Assembly Access

Description: Add the new test assembly to production projects that expose internals to integration tests.

Acceptance criteria:

- `src/InfraGate.McpGateway/InfraGate.McpGateway.csproj` includes `InternalsVisibleTo Include="InfraGate.McpGateway.KeycloakTests"`.
- `src/InfraGate.McpGateway.Auth/InfraGate.McpGateway.Auth.csproj` includes `InternalsVisibleTo Include="InfraGate.McpGateway.KeycloakTests"`.
- No unrelated project files are changed.

Verification:

```bash
dotnet build InfraGate.slnx --configuration Release --no-restore
```

Files likely touched:

- `src/InfraGate.McpGateway/InfraGate.McpGateway.csproj`
- `src/InfraGate.McpGateway.Auth/InfraGate.McpGateway.Auth.csproj`

### Task 5: Fix Keycloak Realm Scope/Audience Test Data

Description: Ensure the limited Keycloak client can produce a token with the gateway audience but without `mcp:tools`, so the missing-scope test exercises authorization rather than audience validation.

Acceptance criteria:

- `mcp-client-limited` has an audience mapper for `http://127.0.0.1:3001/mcp`.
- `mcp-client-limited` still does not include `mcp:tools` in default or optional scopes.
- `TokenWithoutScope_Rejects` receives `403 Forbidden`.

Verification:

```bash
dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --configuration Release --no-build --filter "Category=Keycloak"
```

Files likely touched:

- `deploy/keycloak/infra-gate-realm.json`
- `tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs`

### Task 6: Wire CI Split

Description: Update GitHub Actions so regular jobs exclude Keycloak tests and the dedicated Keycloak workflow targets only the new project.

Acceptance criteria:

- `.github/workflows/unit-tests.yml` uses `--filter "Category!=Keycloak"`.
- `.github/workflows/sonar.yml` uses the same filter inside coverage collection.
- `.github/workflows/keycloak-tests.yml` restores, builds, and tests `tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj`.
- `keycloak-tests.yml` triggers for PRs to both `main` and `dev`.

Verification:

```bash
rg -n "Category!=Keycloak|InfraGate.McpGateway.KeycloakTests" .github
```

Files likely touched:

- `.github/workflows/unit-tests.yml`
- `.github/workflows/sonar.yml`
- `.github/workflows/keycloak-tests.yml`

### Task 7: Refresh Documentation

Description: Update developer-facing commands so they point to the new Keycloak project and make the default non-Keycloak solution command explicit.

Acceptance criteria:

- PR template uses `dotnet test InfraGate.slnx --no-build --filter "Category!=Keycloak"`.
- Developer runbook points Keycloak commands at `InfraGate.McpGateway.KeycloakTests`.
- Configuration docs list the new Keycloak project path.
- Gateway test README no longer describes Keycloak tests as part of the gateway test project.
- New Keycloak test README documents how to list and run Keycloak tests.

Verification:

```bash
rg -n "Category=Keycloak|Category!=Keycloak|InfraGate.McpGateway.KeycloakTests" .github docs tests --glob '!**/bin/**' --glob '!**/obj/**'
```

Files likely touched:

- `.github/PULL_REQUEST_TEMPLATE.md`
- `docs/devs-readme.md`
- `docs/configuration.md`
- `tests/InfraGate.McpGateway.Tests/README.md`
- `tests/InfraGate.McpGateway.KeycloakTests/README.md`

## Final Verification

Run the full verification set:

```bash
dotnet restore tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj
dotnet build InfraGate.slnx --configuration Release --no-restore
dotnet test InfraGate.slnx --configuration Release --no-build --filter "Category!=Keycloak"
dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --configuration Release --no-build --list-tests --filter "Category=Keycloak"
dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --configuration Release --no-build --filter "Category=Keycloak"
git diff --check
```

Expected results:

- Solution build succeeds with no warnings or errors.
- Filtered solution tests pass without starting Keycloak tests.
- Keycloak list-tests command lists exactly 3 tests:
  - `ValidToken_FromKeycloak_AllowsToolCall`
  - `TokenWithWrongAudience_Rejects`
  - `TokenWithoutScope_Rejects`
- Live Keycloak test project passes when Docker is available.
- `git diff --check` reports no whitespace issues.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| New project is in `InfraGate.slnx`, so solution test commands still discover it | Unit CI may report "No test matches" for the Keycloak assembly | Keep `Category!=Keycloak` filter in unit/Sonar and accept the harmless no-match line |
| Keycloak startup arguments drift across image/Testcontainers versions | Dedicated workflow may fail before auth assertions | Use `.WithRealm(...)` instead of manual command composition |
| Missing-scope token lacks audience | Test asserts `401` path instead of `403` scope path | Give limited client the audience mapper but no `mcp:tools` scope |
| Docs continue pointing to old gateway test project | Developers run the wrong command | Update PR template, runbook, configuration docs, and test READMEs |

## Assumptions

- The new project name is `InfraGate.McpGateway.KeycloakTests`.
- The new project stays in `InfraGate.slnx`.
- No production runtime behavior changes are intended.
- The dedicated Keycloak workflow is the intended owner for Docker/Testcontainers failures.
- Keycloak tests should remain provider-specific and should not be generalized into a broader OIDC test project yet.
