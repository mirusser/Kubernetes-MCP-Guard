# InfraGate.RuntimeSafety

`InfraGate.RuntimeSafety` provides runtime mode resolution, production safety validation, and environment variable conventions shared across InfraGate projects.

## Contents

- `RuntimeMode.cs` defines the `Development`, `Demo`, and `Production` runtime modes.
- `RuntimeModeResolver.cs` resolves the runtime mode from environment variables (`INFRA_GATE_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT`).
- `ProductionSafetyValidator.cs` validates that production-safe configuration is present before starting in Production mode.
- `RuntimeSafetyConventions.cs` defines environment variable and value conventions used across the repo.

## Boundaries

This project has no dependencies on other InfraGate projects. It is a shared leaf module consumed by any project that needs runtime-mode awareness.
