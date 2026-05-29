# ADR-0021: McpServer Uses Local DTO Copies Over a Shared Contracts Project

**Date:** 2026-05-29
**Status:** Accepted

---

## Context

`InfraGate.McpServer` was decoupled from `InfraGate.KubernetesAdapter` as part of severing its project references (see the `mcp-server-decoupling` plan). After the decoupling, the only remaining link was `KubernetesObjectRef` — an MSBuild file-link compiled the adapter's source file directly into the McpServer assembly.

The file-link approach caused CS0433 ("type exists in two assemblies") whenever a third project referenced both assemblies, which forced the contract test to use `extern alias`. This created friction and raised the question: where should shared types live?

Three options were considered:

1. **File-link** (status quo) — keep `KubernetesObjectRef.cs` linked from `InfraGate.KubernetesAdapter/PlanBuilding/` into McpServer.
2. **Contracts/abstractions project** — extract `KubernetesObjectRef` (and potentially the other DTOs) into a thin shared `InfraGate.Kubernetes.Contracts` project that both sides reference.
3. **Local DTO copy** — define `KubernetesObjectRef` in `InfraGate.McpServer.Models` alongside the other evidence DTOs; verify JSON compatibility via the existing contract test.

## Decision

**Option 3 — local DTO copy** was chosen. `KubernetesObjectRef` is now defined in `InfraGate.McpServer.Models` with identical property names and types. The file-link is removed.

## Rationale

### McpServer is a temporary component

The active roadmap plans to swap `InfraGate.McpServer` with the external [`containers/kubernetes-mcp-server`](https://github.com/containers/kubernetes-mcp-server) Go binary. When that swap happens:

- `InfraGate.McpServer` and every file in it is deleted.
- A shared contracts project would have to be deleted (or orphaned) at the same time — it would have existed purely to serve a component that no longer exists.
- The `InfraGate.KubernetesAdapter` side would then need to adapt its DTOs to match whatever JSON schema the external Go server actually emits, which a .NET contracts library cannot enforce.

A contracts project is the right choice when both sides of the contract are long-lived, in-repo, .NET assemblies. That is not the case here.

### Local copies are consistent with the rest of the decoupling

All other evidence and diff DTOs (`KubernetesApplyEvidence`, `KubernetesPlanDryRun`, `KubernetesPlanDiff`, etc.) were already copied into `InfraGate.McpServer.Models`. Keeping `KubernetesObjectRef` as a file-link was the odd one out. The local copy makes the boundary uniform: everything McpServer serializes over MCP is defined in `InfraGate.McpServer.Models`.

### The JSON contract test enforces correctness

`InfraGate.McpServer.ContractTests` serializes McpServer DTOs and deserializes them as `InfraGate.KubernetesAdapter.Evidence` types, asserting property-level equality. This catches any drift between the two DTO definitions at CI time, without requiring a shared type.

### No CS0433 or extern alias

With the local copy, `KubernetesObjectRef` in `InfraGate.McpServer.Models` and `KubernetesObjectRef` in `InfraGate.KubernetesAdapter.PlanBuilding` are different fully-qualified types in different namespaces. No ambiguity arises when both assemblies are referenced, and the contract test no longer needs `extern alias`.

## Consequences

- `InfraGate.McpServer` has zero project references to `InfraGate.KubernetesAdapter` or `InfraGate.Approvals`. It is a pure execution substrate.
- When `containers/kubernetes-mcp-server` is adopted, `InfraGate.McpServer` is deleted in its entirety. The `InfraGate.KubernetesAdapter` DTOs are then updated to match the external server's JSON schema independently.
- If the swap never happens and the shared DTO surface grows substantially, revisit whether a contracts project is warranted at that point.
