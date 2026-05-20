# Implementation Plan: Typed Configuration Binding

## Overview

Replace manual `GetConfigurationValue` field-by-field mapping with typed settings POCOs bound via `IConfiguration.Bind()` and registered as `IOptions<T>`. The core challenge: flat env vars (`INFRA_GATE_DOWNSTREAM_ASSEMBLY`) and hierarchical JSON keys (`InfraGate:Gateway:DownstreamAssembly`) are different `IConfiguration` keys. A custom `IConfigurationSource` maps flat env vars into hierarchy, enabling standard .NET binding with proper precedence.

## Architecture Decisions

- **Settings POCOs** are simple records — pure data, no behavior. One per JSON section.
- **Existing option classes** (`McpGatewayOptions`, `GatewayAuthOptions`, `K8SMcpOptions`) continue to own behavior (validation, computed properties, `Is*Explicit` flags). They construct from settings POCOs.
- **`FromEnvironment()` stays** as a compatibility wrapper that reads env vars directly.
- **`FromConfiguration()` becomes a thin adapter** that binds settings POCOs from `IConfiguration`.
- **Config provider lives** in `InfraGate.RuntimeSafety`. Each downstream project registers its own env-to-config mappings.
- Precedence: JSON (step 1) → mapped InfraGate env vars (step 2) → standard env vars (step 3) → CLI args (step 4).

## Task List

### Phase 1: Settings POCOs + Config Provider (foundation)

**Task 1: Create typed settings records**
- `InfraGateRuntimeSettings` in `InfraGate.RuntimeSafety`
- `InfraGateGatewaySettings` in `InfraGate.McpGateway`
- `InfraGateAuthSettings` in `InfraGate.McpGateway.Auth`
- `InfraGateApprovalSettings` in `InfraGate.McpGateway`
- `InfraGateKubernetesSettings` in `InfraGate.McpServer`

**Task 2: Create InfraGate env-to-hierarchy configuration provider**
- `InfraGateEnvironmentVariablesConfigurationProvider` — reads env vars, maps to hierarchy
- Extension method `AddInfraGateEnvironmentVariables(this IConfigurationBuilder)`
- Each downstream conventions class registers its own env→config-key mappings

### Phase 2: Wiring + Refactoring

**Task 3: Replace AddInfraGateConfiguration with typed loading**
- Replace `AddEnvironmentVariables()` with `AddInfraGateEnvironmentVariables()` + `AddEnvironmentVariables()` in both Program.cs
- Register settings POCOs with `services.Configure<T>()`

**Task 4: Refactor FromConfiguration to use typed binding**
- Replace manual `GetConfigurationValue` with `configuration.GetSection(...).Get<T>()`
- Remove `GetConfigurationValue` helpers

### Phase 3: Tests + Cleanup

**Task 5: Add/update tests for typed binding**
**Task 6: Remove dead code**

## Verification

- `dotnet test tests/InfraGate.RuntimeSafety.Tests`
- `dotnet test tests/InfraGate.McpGateway.Tests`
- `dotnet test tests/InfraGate.McpServer.Tests`
- `dotnet build` clean

## Assumptions

- The `KUBECONFIG` and `ASPNETCORE_URLS` env vars are mapped to `InfraGate:*` hierarchy in addition to their standard flat keys
- `AllowedNamespaces` supports both comma-separated env var and JSON array binding
- `FromEnvironment()` methods remain unchanged
