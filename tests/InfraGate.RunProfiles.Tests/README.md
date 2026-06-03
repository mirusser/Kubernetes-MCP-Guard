# InfraGate.RunProfiles.Tests

`InfraGate.RunProfiles.Tests` covers the run-profile CLI and env renderer without requiring Docker, Kubernetes, or live gateway processes.

## What It Covers

- `EnvFileRendererTests.cs`: section omission, approval/gateway/auth/Kubernetes rendering, list indexing, and boolean rendering for generated env files.
- `RunProfileCliTests.cs`: `list`, `validate`, and `generate` behavior for env output, including deterministic file content and validation errors.

## Running Tests

- Default suite: `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`
