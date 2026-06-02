# InfraGate.RuntimeSafety

`InfraGate.RuntimeSafety` provides runtime mode resolution, production safety validation, and environment variable conventions shared across InfraGate projects.

**Owns:** runtime mode resolution, production safety validation, environment variable conventions

## Contents

- `RuntimeMode.cs` defines the `Development`, `Demo`, and `Production` runtime modes.
- `RuntimeModeResolver.cs` resolves the runtime mode from configuration (`InfraGate:Runtime:Environment`, then `DOTNET_ENVIRONMENT`, then `ASPNETCORE_ENVIRONMENT`). Its direct environment-only helper reads the standard .NET environment variables.
- `ProductionSafetyValidator.cs` validates that production-safe configuration is present before starting in Production mode.
- `RuntimeSafetyConventions.cs` defines environment variable and value conventions used across the repo.

## Boundaries

This project has no dependencies on other InfraGate projects. It is a shared leaf module consumed by any project that needs runtime-mode awareness.
