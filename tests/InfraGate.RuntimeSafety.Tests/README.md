# InfraGate.RuntimeSafety.Tests

Unit tests for `InfraGate.RuntimeSafety`.

## Coverage

- `RuntimeModeResolverTests` — runtime mode detection from environment variables (defaults, overrides, and precedence).
- `ProductionSafetyValidatorTests` — production safety checks that refuse startup when dev-only configuration is present in Production mode.

## Run

```bash
dotnet test tests/InfraGate.RuntimeSafety.Tests/InfraGate.RuntimeSafety.Tests.csproj
```
