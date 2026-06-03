# Remediation Plan: Agent-Security Hardening Verification Findings

> Status: proposed (2026-06-03)
>
> Addresses findings from the verification of
> [`2026-06-02-agent-security-hardening-vertical-slice.md`](./2026-06-02-agent-security-hardening-vertical-slice.md).
> Original plan's Task D (local semantic classifier) remains deliberately skipped.

## Overview

The verification report surfaced 3 blockers, 10 important findings, and 6 nice-to-have items across code correctness, documentation, and test coverage. This plan groups remediations into three phases: correctness fixes first (stop the SnapshotExecutor bug), then documentation and test closure, then polish.

## Architecture Decisions

- **SnapshotExecutor branch-stop pattern mirrors DecideExecutor:** when `BlockModelIngestion`, send a bounded safe message to the workflow fan-in (not the original hostile content) but mark the branch as skipped so the LLM never processes it.
- **`IModelVisibleContentAudit` contract tightened:** add a `Digest` property to `ModelVisibleContentDecision` so the audit adapter receives bounded metadata, not raw original text. The guard computes a SHA-256 digest of the original content and passes only that.
- **All documentation findings resolved in a single phase** (Phase 2) because they are independent of each other and of code changes.

## Task List

### Phase 1: Correctness (blockers + two high-impact important fixes)

#### Task 1.1: Fix SnapshotExecutor BlockModelIngestion branch stop

**Description:** `SnapshotExecutor` unconditionally sends `decision.Text` as a `ChatMessage` to the A2A workflow even when the guard returns `BlockModelIngestion`. Mirror the `DecideExecutor` pattern: check `decision.Action` before sending the message. For a blocked branch, emit a bounded placeholder `ChatMessage` so the fan-in workflow completes, but the LLM receives a safe marker indicating the branch was skipped.

**Acceptance criteria:**
- [ ] `SnapshotExecutor.HandleAsync` checks `decision.Action == ModelVisibleContentAction.BlockModelIngestion` before constructing the `ChatMessage`.
- [ ] Blocked branch sends a safe placeholder (`"[BRANCH SKIPPED: content guard blocked model ingestion]"`) instead of `decision.Text`.
- [ ] Allowed/Redacted/Quarantined branches still send `decision.Text` as before.
- [ ] Existing Observer workflow tests pass unchanged.

**Verification:**
- [ ] `dotnet test tests/InfraGate.Observer.Tests/InfraGate.Observer.Tests.csproj`
- [ ] Add 4 focused `SnapshotExecutor` unit tests for Allow/Redact/Quarantine/BlockModelIngestion actions.

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Observer/Cycle/Workflow/SnapshotExecutor.cs`
- `tests/InfraGate.Observer.Tests/UnitTests/SnapshotExecutorTests.cs` (new)

**Estimated scope:** Medium

#### Task 1.2: Harden `IModelVisibleContentAudit` to bound raw content exposure

**Description:** `CompositeModelVisibleContentGuard` passes `content.Text` (original pre-evaluation text) to `audit.PersistAsync()`. Add a `Digest` property to `ModelVisibleContentDecision` and pass a SHA-256 digest instead of raw text. Update the audit interface to accept only bounded metadata. The guard still holds the original text in memory for evaluation but never persists it through the audit seam.

**Acceptance criteria:**
- [ ] `ModelVisibleContentDecision` gains `Digest string?` property (SHA-256 of original content text).
- [ ] `IModelVisibleContentAudit.PersistAsync` receives `Digest` and `Source` but not raw `originalContent.Text`.
- [ ] Composite guard computes the digest and includes it in the final decision.
- [ ] Existing composite guard tests updated; audit-persistence tests verify raw text is absent.

**Verification:**
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.Tests/InfraGate.AgentGuardrails.Tests.csproj`

**Dependencies:** None (independent of Task 1.1)

**Files likely touched:**
- `src/InfraGate.AgentGuardrails/ModelVisibleContentDecision.cs`
- `src/InfraGate.AgentGuardrails/IModelVisibleContentAudit.cs`
- `src/InfraGate.AgentGuardrails/CompositeModelVisibleContentGuard.cs`
- `tests/InfraGate.AgentGuardrails.Tests/UnitTests/CompositeModelVisibleContentGuardTests.cs`

**Estimated scope:** Medium

#### Task 1.3: Enforce `MaximumInputCharacters` at runtime

**Description:** `ModelVisibleContentOptions.MaximumInputCharacters` is validated at startup but never enforced. Add a check in `CompositeModelVisibleContentGuard.EvaluateAsync` that rejects content exceeding the bound before any adapter evaluation. Return a `Quarantine` decision with a reason indicating the content exceeded the size limit. Do not log or persist the oversized content.

**Acceptance criteria:**
- [ ] Guard checks `content.Text.Length > options.MaximumInputCharacters` as the first pipeline step.
- [ ] Oversized content returns `Quarantine` with placeholder and reason `"exceeded_maximum_input_characters"`.
- [ ] Bound is configurable and defaults to 100,000.
- [ ] Metrics record the oversized-content decision under `Quarantine` action.

**Verification:**
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.Tests/InfraGate.AgentGuardrails.Tests.csproj`
- [ ] Add composite guard test with input exceeding the bound.

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.AgentGuardrails/CompositeModelVisibleContentGuard.cs`
- `src/InfraGate.AgentGuardrails/AgentGuardrailConventions.cs` (reason constant)
- `tests/InfraGate.AgentGuardrails.Tests/UnitTests/CompositeModelVisibleContentGuardTests.cs`

**Estimated scope:** Small

#### Task 1.4: Remove Observer/Planner compile-time dependency on AGT adapter assembly

**Description:** Both `Observer/Program.cs` and `Planner/Program.cs` import `InfraGate.AgentGuardrails.AgentGovernanceToolkit` and call `AddAgentGovernanceToolkitModelVisibleContentGuard()`. This leaks the adapter choice into host composition roots. Move registration to a service-collection extension in `InfraGate.AgentGuardrails` that wires `CompositeModelVisibleContentGuard` with the AGT adapter as an internal detail. Hosts call a generic `AddModelVisibleContentGuard()` that resolves configuration and returns the composite.

**Acceptance criteria:**
- [ ] Observer and Planner `Program.cs` no longer import `InfraGate.AgentGuardrails.AgentGovernanceToolkit`.
- [ ] `AgentGuardrailServiceCollectionExtensions` gains `AddModelVisibleContentGuard(IConfigurationSection)` that internally composes `AgentGovernanceToolkitContentGuard` inside `CompositeModelVisibleContentGuard`.
- [ ] Observer and Planner call only `services.AddModelVisibleContentGuard(configSection)`.
- [ ] Tests prove resolution from configuration (AGT enabled by default, disabled falls back to AllowAll).

**Verification:**
- [ ] `dotnet build InfraGate.slnx --configuration Release`
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.Tests/InfraGate.AgentGuardrails.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.AgentGovernanceToolkit.Tests/InfraGate.AgentGuardrails.AgentGovernanceToolkit.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.Observer.Tests/InfraGate.Observer.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.Planner.Tests/InfraGate.Planner.Tests.csproj`

**Dependencies:** None (independent of Tasks 1.1-1.3)

**Files likely touched:**
- `src/InfraGate.AgentGuardrails/AgentGuardrailServiceCollectionExtensions.cs`
- `src/InfraGate.Observer/Program.cs`
- `src/InfraGate.Planner/Program.cs`
- `tests/InfraGate.AgentGuardrails.Tests/UnitTests/` (new DI resolution tests)
- `tests/InfraGate.Observer.Tests/GlobalUsings.cs` (may need import adjustment)

**Estimated scope:** Medium

### Checkpoint: Phase 1
- [ ] SnapshotExecutor branch-stop behavior correct for all four actions.
- [ ] Audit seam passes digest only, never raw text.
- [ ] MaximumInputCharacters enforced at runtime.
- [ ] Observer/Planner composition roots reference only `InfraGate.AgentGuardrails`, not the AGT adapter.
- [ ] `dotnet build InfraGate.slnx --configuration Release` — 0 errors, 0 warnings.
- [ ] `dotnet test InfraGate.slnx --configuration Release --filter "Category!=Keycloak&Category!=SafetyE2E"` — all pass.

### Phase 2: Documentation

#### Task 2.1: Update `src/InfraGate.AgentGuardrails/README.md`

**Description:** Rewrite the Module Surface and Metric Taxonomy sections to include all 11 new public types, 3 new metrics, and the DI wiring helpers. Add a "Model-Visible Content Guard" section explaining the seam, the four actions, and the configuration section name.

**Acceptance criteria:**
- [ ] Module Surface lists all public types: `IModelVisibleContentGuard`, `IModelVisibleContentAudit`, `ModelVisibleContent`, `ModelVisibleContentDecision`, `ModelVisibleContentAction`, `ModelVisibleContentSource`, `ModelVisibleContentOptions`, `ModelVisibleContentUnavailableBehavior`, `CompositeModelVisibleContentGuard`, `AllowAllModelVisibleContentGuard`, `ModelVisibleContentGuardExtensions`.
- [ ] Metric taxonomy includes `infragate.agentguardrails.model_visible.decision`, `infragate.agentguardrails.model_visible.degraded`, `infragate.agentguardrails.model_visible.evaluation_duration_ms`.
- [ ] Wiring section documents `AddModelVisibleContentGuard()`, `AddAllowAllModelVisibleContentGuard()`, and `UseModelVisibleContentGuard` middleware.

**Verification:**
- [ ] `verify-readme-docs` skill passes for `InfraGate.AgentGuardrails`.

**Dependencies:** Task 1.4 (refresh DI registration doc if signature changes)

**Files likely touched:**
- `src/InfraGate.AgentGuardrails/README.md`

**Estimated scope:** Medium

#### Task 2.2: Document `InfraGate:AgentGuardrails:ModelVisibleContent` in `docs/configuration.md`

**Description:** Add a `### InfraGate.AgentGuardrails.ModelVisibleContent` section documenting all 7 options with their defaults, descriptions, and production guidance (fail-closed, offline-only for now).

**Acceptance criteria:**
- [ ] All 7 options listed: `Enabled`, `SemanticClassifierEnabled`, `LocalClassifierBaseUrl`, `RequestTimeoutMilliseconds`, `MaximumInputCharacters`, `UnavailableBehavior`, `QuarantinePlaceholder`.
- [ ] Defaults match `ModelVisibleContentOptions` source.
- [ ] Note that `SemanticClassifierEnabled` is enforced as unsupported until Phase D.

**Verification:**
- [ ] `verify-readme-docs` skill passes for `docs/configuration.md`.

**Dependencies:** None

**Files likely touched:**
- `docs/configuration.md`

**Estimated scope:** Small

#### Task 2.3: Update Observer and Planner READMEs with content guard integration

**Description:** Add a "Model-Visible Content Guard" subsection to each README's Guardrails section, documenting that `SnapshotExecutor` / `DecideExecutor` evaluates content through `IModelVisibleContentGuard` before LLM ingestion, and describing the four actions' effects on the ingestion path.

**Acceptance criteria:**
- [ ] Observer README describes snapshot content guard: `SnapshotExecutor` evaluates `SnapshotDocument` JSON before `ChatMessage` creation.
- [ ] Planner README describes anomaly content guard: `DecideExecutor` evaluates `AnomalyReport` JSON before `agent.RunAsync`; `BlockModelIngestion` skips the LLM path entirely.
- [ ] Both mention that the AGT deterministic adapter is enabled by default.

**Verification:**
- [ ] `verify-readme-docs` skill passes for Observer and Planner READMEs.

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Observer/README.md`
- `src/InfraGate.Planner/README.md`

**Estimated scope:** Small

#### Task 2.4: Add README to `InfraGate.AgentGuardrails.AgentGovernanceToolkit`

**Description:** Create a README for the new adapter project documenting the AGT→InfraGate threat-level mapping, DI helpers, pinned package policy, and offline-only operation guarantee.

**Acceptance criteria:**
- [ ] Documents `ThreatLevel` → `ModelVisibleContentAction` mapping:
  - `ThreatLevel.High` → `BlockModelIngestion`
  - `ThreatLevel.Medium` → `Quarantine`
  - `ThreatLevel.Low` → `Redact`
  - `ThreatLevel.None` → `Allow`
- [ ] Documents `AddAgentGovernanceToolkitContentGuard()` and `AddAgentGovernanceToolkitModelVisibleContentGuard()`.
- [ ] States package pin policy (3.7.0), offline operation, and Azure-free guarantee.

**Verification:**
- [ ] README exists and links are valid.

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.AgentGuardrails.AgentGovernanceToolkit/README.md` (new)

**Estimated scope:** Small

#### Task 2.5: Add glossary terms to `CONTEXT.md`

**Description:** Add definitions for **Model-Visible Content Guard**, **Model-Visible Content**, and **Quarantine** to the canonical glossary.

**Acceptance criteria:**
- [ ] **Model-Visible Content Guard** defined as the `IModelVisibleContentGuard` seam that evaluates text before LLM ingestion.
- [ ] **Model-Visible Content** defined as any text (snapshot JSON, anomaly JSON, tool result) that will be consumed by an LLM.
- [ ] **Quarantine** defined as replacing suspicious content with a bounded safe placeholder while recording metadata for investigation.

**Verification:**
- [ ] Terms appear in CONTEXT.md glossary section.

**Dependencies:** None

**Files likely touched:**
- `CONTEXT.md`

**Estimated scope:** Small

#### Task 2.6: Add model-visible content guard configuration to `deploy/run-profiles.yaml`

**Description:** Add an `agentGuardrails.modelVisibleContent` block to the development profile(s) with defaults. The guard is on by default at the DI level, but profiles should surface the configuration section so operators can toggle or tune it.

**Acceptance criteria:**
- [ ] `development` profile includes `agentGuardrails.modelVisibleContent` with inline comments documenting each option.
- [ ] Defaults match `ModelVisibleContentOptions` source (enabled, fail-closed, etc.).
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate` passes.

**Verification:**
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate`
- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`

**Dependencies:** None

**Files likely touched:**
- `deploy/run-profiles.yaml`

**Estimated scope:** Small

### Checkpoint: Phase 2
- [ ] All 5 module READMEs + CONTEXT.md + configuration.md + run-profiles.yaml are up to date.
- [ ] `verify-readme-docs` skill passes for all touched modules.
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate` passes.

### Phase 3: Test coverage and polish

#### Task 3.1: Add missing metrics test for `Redact` action

**Description:** `ModelVisibleContentGuardMetricsTests` has tests for Allow, Quarantine, and BlockModelIngestion but not Redact. Add a `RecordModelVisibleDecision_Redact_RecordsCounterWithRedactAction` test.

**Acceptance criteria:**
- [ ] Redact action is tested in isolation (single measurement, correct tag value).
- [ ] Existing metrics tests pass unchanged.

**Verification:**
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.Tests/InfraGate.AgentGuardrails.Tests.csproj`

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.AgentGuardrails.Tests/UnitTests/ModelVisibleContentGuardMetricsTests.cs`

**Estimated scope:** Small

#### Task 3.2: Add missing `ModelVisibleContentOptions.Validate()` test coverage

**Description:** Three validation branches are untested: `RequestTimeoutMilliseconds <= 0`, `MaximumInputCharacters <= 0`, and `LocalClassifierBaseUrl` set to a non-null but invalid (non-absolute) URI. Add tests for each.

**Acceptance criteria:**
- [ ] `Validate_RequestTimeoutMillisecondsZero_Throws` test.
- [ ] `Validate_MaximumInputCharactersZero_Throws` test.
- [ ] `Validate_InvalidLocalClassifierBaseUrl_Throws` test.
- [ ] All three assert `InvalidOperationException` with descriptive messages.

**Verification:**
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.Tests/InfraGate.AgentGuardrails.Tests.csproj`

**Dependencies:** None

**Files likely touched:**
- `tests/InfraGate.AgentGuardrails.Tests/UnitTests/ModelVisibleContentOptionsTests.cs`

**Estimated scope:** Small

#### Task 3.3: Add missing `#pragma` justification comment in `DecideExecutor.cs`

**Description:** `DecideExecutor.cs:173` has `#pragma warning disable S1144, S3459` with no explanatory comment. The same pattern in `ObservationCycleRunner.cs:20-29` has a 4-line justification. Add an equivalent comment.

**Acceptance criteria:**
- [ ] Comment above the pragma explains that `LlmDecisionOutput` is a schema-only DTO referenced by `ChatResponseFormat.ForJsonSchema`, so the unused-setter and unused-property warnings are intentional.

**Verification:**
- [ ] `dotnet build InfraGate.slnx --configuration Release` — still 0 warnings.

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Planner/Cycle/Workflow/DecideExecutor.cs`

**Estimated scope:** XS

#### Task 3.4: Replace bare `catch` in `ObservationCycleRunner.HasWarningEventsAsync`

**Description:** `ObservationCycleRunner.cs:307-311` uses a bare `catch` (no exception type) in a non-boundary method. Change to `catch (Exception)` or specifically catch the exceptions `HasWarningEventsAsync` can produce.

**Acceptance criteria:**
- [ ] `catch` specifies a type (`Exception` or more specific types).
- [ ] Existing behavior preserved: on catch, return `true` to err on the side of running the cycle.
- [ ] Build produces no new warnings.

**Verification:**
- [ ] `dotnet build InfraGate.slnx --configuration Release` — 0 warnings.
- [ ] `dotnet test tests/InfraGate.Observer.Tests/InfraGate.Observer.Tests.csproj`

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs`

**Estimated scope:** XS

### Checkpoint: Phase 3
- [ ] All test gaps closed.
- [ ] Code hygiene issues resolved (pragma comment, bare catch).
- [ ] `dotnet test InfraGate.slnx --configuration Release --filter "Category!=Keycloak&Category!=SafetyE2E"` — all pass.

### Phase 4: Optional polish (nice-to-have)

#### Task 4.1: Consider removing unreachable default arms in metric switch expressions

**Description:** `CompositeModelVisibleContentGuard.ActionStrength` and `AgentGovernanceToolkitContentGuard` have `_ =>` default arms in switch expressions on sealed enums. These are dead code. Either remove them or add a comment explaining they are defensive against future enum additions.

**Acceptance criteria:**
- [ ] Each unreachable default arm is removed or documented with a comment.
- [ ] No build warnings.

**Verification:**
- [ ] `dotnet build InfraGate.slnx --configuration Release` — 0 warnings.

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.AgentGuardrails/CompositeModelVisibleContentGuard.cs`
- `src/InfraGate.AgentGuardrails.AgentGovernanceToolkit/AgentGovernanceToolkitContentGuard.cs`

**Estimated scope:** XS

### Checkpoint: Complete
- [ ] All blocker and important findings resolved.
- [ ] All nice-to-have items either resolved or documented as deferred.
- [ ] `dotnet build InfraGate.slnx --configuration Release` — 0 errors, 0 warnings.
- [ ] `dotnet test InfraGate.slnx --configuration Release --filter "Category!=Keycloak&Category!=SafetyE2E"` — all pass.
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate` — valid.
- [ ] `verify-readme-docs` skill passes for all touched modules.
- [ ] SnapshotExecutor branch-stop behavior matches DecideExecutor pattern.

## Parallelization Opportunities

- **Tasks 1.1, 1.2, 1.3, 1.4** can all proceed in parallel — they touch independent subsystems (Observer, AgentGuardrails core, AGT adapter, host composition).
- **All Phase 2 documentation tasks (2.1-2.6)** can proceed in parallel.
- **All Phase 3 test/polish tasks (3.1-3.4)** can proceed in parallel.
- **Phase 2 and Phase 3 are independent** and can proceed concurrently after Phase 1 completes.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Tightening `IModelVisibleContentAudit` breaks downstream implementations | Low | Only `CompositeModelVisibleContentGuard` implements audit; no external implementations exist |
| SnapshotExecutor branch-stop changes workflow fan-in behavior | Medium | Emit a bounded placeholder ChatMessage so fan-in still completes; test full workflow |
| DI refactor (Task 1.4) breaks existing Observer/Planner startup | Medium | Add as a new extension method alongside existing one; Observer/Planner switch to new API |
| MaximumInputCharacters enforcement rejects legitimate large snapshots | Low | Default 100,000 chars is generous; configurable per environment |

## Open Questions

- [ ] For `IModelVisibleContentAudit` hardening: should the digest be SHA-256 only, or should the decision also include a `TruncatedText` field (first N chars for forensic triage)?
- [ ] For SnapshotExecutor branch-stop: should the placeholder message include the namespace name (for observability) or be completely generic?
- [ ] Should `UnavailableBehavior` config be removed until Phase D, or kept as dead config with a comment explaining it's reserved for the deferred semantic classifier?

## Final Verification Checklist

- [ ] `SnapshotExecutor` checks `BlockModelIngestion` before sending ChatMessage.
- [ ] `IModelVisibleContentAudit` receives bounded metadata only.
- [ ] `MaximumInputCharacters` enforced at runtime.
- [ ] Observer/Planner no longer import `InfraGate.AgentGuardrails.AgentGovernanceToolkit`.
- [ ] All 5 READMEs, CONTEXT.md, configuration.md, and run-profiles.yaml are up to date.
- [ ] All test gaps closed.
- [ ] Code hygiene issues fixed (pragma comment, bare catch).
- [ ] `dotnet build InfraGate.slnx --configuration Release`
- [ ] `dotnet test InfraGate.slnx --configuration Release --filter "Category!=Keycloak&Category!=SafetyE2E"`
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate`
