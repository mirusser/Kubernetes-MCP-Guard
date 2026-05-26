# Implementation Plan: Dynamic Domain Adapter Loading

## Overview
Currently, the McpGateway acts as the Generic Approval Core but statically binds to the `KubernetesAdapter` at compile time and initialization time. This violates the goal of a deployment boundary seam. This plan completely decouples the `KubernetesAdapter` from the `McpGateway` by introducing Dynamic Assembly Loading driven by the `InfraGate.RunProfiles` system.

## Architecture Decisions
- **Adapter Contract:** We will introduce an `IAdapterRegistration` interface in `InfraGate.Approvals`. Any domain adapter must implement this to register its `IDomainPlanBuilder` and `IDomainPlanExecutor` implementations.
- **Run Profiles as Source of Truth:** `run-profiles.yaml` will dictate which adapters to load for a given environment. The `InfraGate.RunProfiles` CLI will compile this into `appsettings.json`.
- **Compile-Time Decoupling via MSBuild:** To ensure the gateway compiles cleanly without knowing about Kubernetes, but the `.dll` is still present in the bin folder for local runs, we will change the project reference in `InfraGate.McpGateway.csproj` to `ReferenceOutputAssembly="false"`. This prevents compile-time coupling but ensures the artifact is copied.
- **Dynamic Loading:** `Program.cs` in the Gateway will read `McpGatewayOptions.Adapters`, load the requested assemblies via `AssemblyLoadContext.Default.LoadFromAssemblyPath`, reflectively find implementations of `IAdapterRegistration`, and invoke them.

## Task List

### Phase 1: Foundation (Adapter Contract)
## Task 1: Define Adapter Registration Contract
**Description:** Introduce the generic contract that any domain adapter must implement to attach itself to the Gateway's DI container.
**Acceptance criteria:**
- [ ] `IAdapterRegistration` exists in `InfraGate.Approvals`.
- [ ] `KubernetesAdapterRegistration` exists in `InfraGate.KubernetesAdapter` and implements the contract (calling the existing `AddKubernetesAdapter()` logic).
**Verification:**
- [ ] `dotnet build src/InfraGate.KubernetesAdapter` succeeds.
**Dependencies:** None
**Files likely touched:**
- `src/InfraGate.Approvals/IAdapterRegistration.cs` (NEW)
- `src/InfraGate.KubernetesAdapter/KubernetesAdapterRegistration.cs` (NEW)
**Estimated scope:** Small

### Phase 2: Configuration (Run Profiles)
## Task 2: Expose Adapters in Run Profiles
**Description:** Update the Run Profiles schema and CLI generators to support an `adapters` array for the Gateway.
**Acceptance criteria:**
- [ ] `GatewayProfile` and `RunProfileDocument.MergeGateway` handle an `Adapters` array.
- [ ] `AppSettingsRenderer.cs` outputs the array to `InfraGate:Gateway:Adapters` in the generated JSON.
**Verification:**
- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests` passes.
- [ ] Running `run-profiles` CLI against a test yaml correctly emits the adapters to `appsettings.json`.
**Dependencies:** Task 1
**Files likely touched:**
- `src/InfraGate.RunProfiles/GatewayProfile.cs`
- `src/InfraGate.RunProfiles/RunProfileDocument.cs`
- `src/InfraGate.RunProfiles/RunProfileDocumentReader.cs`
- `src/InfraGate.RunProfiles/AppSettingsRenderer.cs`
**Estimated scope:** Medium

## Task 3: Bind Adapters in Gateway Options
**Description:** Update the Gateway's configuration binding to ingest the adapters array from `appsettings.json` or environment variables.
**Acceptance criteria:**
- [ ] `InfraGateGatewaySettings` includes `string[]? Adapters`.
- [ ] `McpGatewayOptions` parses the array.
**Verification:**
- [ ] `dotnet build src/InfraGate.McpGateway` succeeds.
**Dependencies:** Task 2
**Files likely touched:**
- `src/InfraGate.McpGateway/InfraGateGatewaySettings.cs`
- `src/InfraGate.McpGateway/McpGatewayOptions.cs`
- `src/InfraGate.McpGateway/McpGatewayConventions.cs`
**Estimated scope:** Small

### Checkpoint: Foundation & Configuration
- [ ] All tests pass.
- [ ] The Run Profiles CLI correctly generates configuration.

### Phase 3: Decoupling and Dynamic Loading
## Task 4: Enforce Compile-Time Decoupling and Implement Dynamic Loading
**Description:** Sever the static compile-time link between Gateway and KubernetesAdapter, and replace the composition root with dynamic assembly scanning.
**Acceptance criteria:**
- [ ] `InfraGate.McpGateway.csproj` uses `ReferenceOutputAssembly="false"` for the `KubernetesAdapter`.
- [ ] `Program.cs` no longer calls `AddKubernetesAdapter()`.
- [ ] `Program.cs` loops over `McpGatewayOptions.Adapters`, loads each assembly via `AssemblyLoadContext`, finds `IAdapterRegistration` types, and invokes them.
**Verification:**
- [ ] `dotnet build` succeeds (proving compile-time isolation).
- [ ] `dotnet run` (or a gateway unit test) successfully boots up and still has the Kubernetes adapter registered in the DI container.
**Dependencies:** Task 3
**Files likely touched:**
- `src/InfraGate.McpGateway/InfraGate.McpGateway.csproj`
- `src/InfraGate.McpGateway/Program.cs`
**Estimated scope:** Medium

### Checkpoint: Complete
- [ ] All tests pass.
- [ ] End-to-end approval flow works (adapter is successfully loaded and routes intents).
- [ ] Ready for review.

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| Missing DLL at runtime | High | Use MSBuild `ReferenceOutputAssembly="false"` to ensure build artifacts are physically co-located in the bin directory without creating a compile-time bond. |
| Reflection Performance | Low | Assembly scanning only happens exactly once at startup. |
| Dependency Resolution in loaded assemblies | Medium | The `AssemblyLoadContext.Default` will natively resolve dependencies that are already in the base directory (like `InfraGate.Approvals`). |

## Open Questions
> [!IMPORTANT]
> The `ReferenceOutputAssembly="false"` MSBuild trick works perfectly for local development (`dotnet run`), but when building Docker images, how does the `Dockerfile` currently package the Gateway? Does it do a `dotnet publish` on the solution, or just the Gateway project? If it only publishes the Gateway project, we need to ensure the adapter gets published too. I will check the Dockerfiles if you approve this plan.
