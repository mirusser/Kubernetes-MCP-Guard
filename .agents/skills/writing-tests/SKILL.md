---
name: writing-tests
description: Apply when adding or modifying tests in k8s-toolkit. Covers test project structure, naming, InternalsVisibleTo setup for internal types, and what to verify before and after.
---

# Writing Tests

## Test Project Structure

Each runtime project has a matching test project under `tests/`:

| Source project | Test project |
|---|---|
| `InfraGate.McpServer` | `InfraGate.McpServer.Tests` |
| `InfraGate.McpGateway` | `InfraGate.McpGateway.Tests` |
| `InfraGate.RuntimeSafety` | `InfraGate.RuntimeSafety.Tests` |
| `InfraGate.Observability` | `InfraGate.Observability.Tests` |
| `InfraGate.RunProfiles` | `InfraGate.RunProfiles.Tests` |
| (gateway + auth OIDC integration) | `InfraGate.McpGateway.KeycloakTests` |
| (full approval-flow safety E2E) | `InfraGate.Safety.E2E.Tests` |

Tests are split by scope:

- `UnitTests/` — no network, no Kubernetes, no filesystem (or temp-path only).
- `IntegrationTests/` — may require TestHost, a fake downstream server, or opt-in external dependencies.
- Safety E2E and Keycloak tests are opt-in categories; do not make the default test run depend on Docker, Keycloak, or a live Kubernetes cluster.

Add new test files to the appropriate subdirectory.

## Assertion Surface

Prefer assertions on behavior contracts, not presentation text. When code returns a typed result, assert the stable fields first.

Do not assert user-facing prose such as approval/refusal sentences etc. unless the test is specifically about rendering, CLI/external protocol output, redaction text, or a documented wire contract. If parsing is unavoidable, parse by format (URL path, plan-id pattern, JSON field, form token) rather than by label text like `Approval URL:`.

Use convention constants for contract strings.

## Naming

Follow `Method_State_ExpectedResult`:

```csharp
Validate_PrivilegedContainer_IsDenied()
RequestApplyManifestAsync_RejectsDisallowedNamespace()
ExecuteApprovedPlanAsync_RefusesPendingPlanWithoutApproval()
```

## InternalsVisibleTo

Most types in this repo are `internal`. Before writing tests that reference internal types, check whether the source project already exposes its internals to the test project:

```bash
grep "InternalsVisibleTo" src/<Project>/<Project>.csproj
```

If the entry is missing, add it following the pattern used by other projects (e.g., `src/InfraGate.McpGateway/InfraGate.McpGateway.csproj`):

```xml
<ItemGroup>
  <InternalsVisibleTo Include="InfraGate.<Project>.Tests" />
</ItemGroup>
```

Without this, tests that reference internal types fail at compile time with `CS0122: inaccessible due to its protection level`.

## Verification

After adding or changing tests, run the narrowest useful test command:

```bash
dotnet test tests/<Project>.Tests/<Project>.Tests.csproj
```

All pre-existing tests must continue to pass. New tests must pass too — do not commit failing tests.

## Mocking

Never use mocks (Moq, NSubstitute) or any similar packages. Write integration tests using Testcontainers instead.
