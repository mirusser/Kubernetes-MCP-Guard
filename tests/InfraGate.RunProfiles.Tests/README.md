# InfraGate.RunProfiles.Tests

`InfraGate.RunProfiles.Tests` covers the run-profile CLI and appsettings renderer without requiring Docker, Kubernetes, or live gateway processes.

## What It Covers

- `AppSettingsRendererTests.cs`: section omission, approval/gateway/auth/Kubernetes rendering, boolean validation, and JSON shape for generated appsettings.
- `RunProfileCliTests.cs`: `list`, `validate`, and `generate` behavior for env and appsettings outputs, including deterministic file content and validation errors.

## Running Tests

- Default suite: `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`
