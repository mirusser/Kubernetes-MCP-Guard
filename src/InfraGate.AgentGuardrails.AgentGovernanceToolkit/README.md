# InfraGate.AgentGuardrails.AgentGovernanceToolkit

`InfraGate.AgentGuardrails.AgentGovernanceToolkit` is the deterministic adapter that wraps the `AgentGovernance.Security` `PromptInjectionDetector` behind the `IModelVisibleContentGuard` seam. It converts AGT threat levels into InfraGate model-visible content actions.

**Owns:** AGT threat-level → InfraGate action mapping

## Threat Level Mapping

| AGT `ThreatLevel` | InfraGate `ModelVisibleContentAction` | Behavior |
|---|---|---|
| `None` | `Allow` | Original text passes through unchanged. |
| `Low` | `Redact` | Potential injection pattern detected; text replaced with `[CONTENT REDACTED: potential injection pattern detected by deterministic filter]`. |
| `Medium` | `Redact` | Same as Low. |
| `High` | `Quarantine` | Suspicious content withheld; placeholder sent to LLM. |
| `Critical` | `BlockModelIngestion` | LLM ingestion blocked entirely. |

## Wiring

- `AddAgentGovernanceToolkitContentGuard(this IServiceCollection, DetectionConfig?)` — registers `AgentGovernanceToolkitContentGuard` as an `IModelVisibleContentGuard` directly (without composite wrapper).
- `AddModelVisibleContentGuard(this IServiceCollection, ModelVisibleContentOptions, DetectionConfig?)` — composes `AgentGovernanceToolkitContentGuard` inside `CompositeModelVisibleContentGuard` and configures the size bound from `ModelVisibleContentOptions`. Hosts use this method; they only need `using InfraGate.AgentGuardrails;`.

## Package Policy

- **Pinned:** `AgentGovernance.Security` 3.7.0. The `PromptInjectionDetector` is the only dependency.
- **Offline-only:** The detector runs entirely in-process with no outbound calls. No Azure, no cloud, no telemetry.
- **Deterministic:** The adapter returns synchronous results with no network I/O. No LLM judge, no probabilistic classification.

## Verification

- Unit tests: `dotnet test tests/InfraGate.AgentGuardrails.AgentGovernanceToolkit.Tests/InfraGate.AgentGuardrails.AgentGovernanceToolkit.Tests.csproj`
- Full solution check: `dotnet test InfraGate.slnx`
