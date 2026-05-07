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
| `InfraGate.DevIssuer` | `InfraGate.DevIssuer.Tests` |

Tests are split by scope:

- `UnitTests/` — no network, no Kubernetes, no filesystem (or temp-path only).
- `IntegrationTests/` — require a real cluster; opt-in, not run by default.

Add new test files to the appropriate subdirectory.

## Naming

Follow `Method_State_ExpectedResult`:

```csharp
Validate_PrivilegedContainer_IsDenied()
RequestApplyManifestAsync_RejectsDisallowedNamespace()
ApplyApprovedPlanAsync_RefusesPendingPlanWithoutApproval()
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
