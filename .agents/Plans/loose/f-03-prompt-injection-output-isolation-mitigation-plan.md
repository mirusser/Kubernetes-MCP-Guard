# Implementation Plan: F-03 Prompt Injection Output Isolation Mitigation

## Overview

Mitigate F-03 from `.agents/Plans/loose/security-audit.md` by changing prompt-injection handling from "regex sanitizer as prevention" to layered model-visible output isolation. Kubernetes-derived tool output should be treated as hostile by default, returned to model-facing clients in a strict envelope, evaluated through the existing model-visible content guard seam, and backed by adversarial regression tests. Existing regex/base64 scanning remains useful as detection and redaction, but it must no longer be the primary security claim.

## Current State Observed

- Gateway read-only downstream results currently return a plain `TextContentBlock` from `GatewayToolDispatcher.HandleReadOnlyAsync` after `GuardedToolRunner.CallAsync`.
- `PromptInjectionGuard` performs rule/regex scanning and redaction, including base64 decode-and-scan, but still has the F-03 gaps for split-field, Unicode, homoglyph, and indirect instructions.
- `InfraGate.AgentGuardrails` already has the right deep seam: `IModelVisibleContentGuard`, `ModelVisibleContentDecision`, quarantine/block actions, metrics, and digest-based forensic audit.
- Observer and Planner already evaluate some model-visible content through the guard, but MCP tool-result isolation is not yet an end-to-end contract at the Gateway response boundary.
- `.agents/Plans/loose/2026-06-03-local-semantic-classifier-research-plan.md` exists and should be treated as the research gate for any semantic classifier sidecar work.

## Assumptions

- This plan is read-only planning work; implementation should start only after human review.
- F-03 mitigation should not weaken existing scope checks, approval gates, or read/write tool separation.
- Regex scanning stays as a detection signal and compatibility fallback, not as the claimed prevention boundary.
- A secondary semantic classifier is useful only if the existing research plan selects an operationally acceptable local/offline candidate; do not add a cloud LLM judge as part of this mitigation.

## Success Criteria

- All Kubernetes-derived read-only tool results returned through the Gateway use a documented model-visible envelope instead of ad-hoc plain text.
- The envelope makes untrusted payload data structurally separate from gateway metadata and contains no model-directed instructions inside attacker-controlled fields.
- Observer and Planner tool-result ingestion evaluates the envelope through `IModelVisibleContentGuard` before any result text reaches the LLM.
- Regression tests cover encoded, split-field, Unicode/zero-width, homoglyph, indirect, and benign Kubernetes samples.
- Guardrail audit remains a detection/forensics signal and records enough metadata to investigate quarantined or blocked content without logging raw credentials.
- Documentation and the security-audit implementation notes are updated only after tests prove the new boundary.

## Architecture Decisions

- **Envelope first, scanner second.** The primary mitigation is schema-enforced output isolation; pattern scanning remains defense-in-depth.
- **Use existing guardrail seam.** Extend `InfraGate.AgentGuardrails` / agent ingestion rather than creating a second unrelated guardrail pipeline.
- **Do not create a broad shared “junk drawer” project.** If a tiny shared envelope contract is needed across projects, use the repo's existing boundary guidance: either keep ownership local and duplicate constants deliberately, or file-link a single source file with explicit Dockerfile updates.
- **Fail closed for autonomous model ingestion in Production.** If model-visible content guarding is unavailable, Observer/Planner should quarantine/block rather than silently passing hostile output.
- **Semantic classifier is gated.** Implement local semantic classification only after the existing classifier research plan passes maintenance, licensing, provenance, and repo-specific measurement gates.

## Dependency Graph

```text
Adversarial corpus + envelope contract
    |
    +-- Gateway read-only output envelope
    |       |
    |       +-- Gateway tests for plain-text removal and metadata/audit behavior
    |
    +-- Agent MCP ingestion contract
    |       |
    |       +-- Observer/Planner model-visible guard tests
    |
    +-- Detection hardening
            |
            +-- Optional local semantic classifier gate
            |
            +-- Documentation and audit status update
```

## Task List

### Phase 1: Contract and Regression Foundation

- [ ] Task 1: Build the F-03 adversarial corpus and benign Kubernetes fixtures
- [ ] Task 2: Define the model-visible tool-result envelope contract

## Task 1: Build the F-03 adversarial corpus and benign Kubernetes fixtures

**Description:** Add a focused test corpus that captures the bypass classes named in F-03 before changing production code. Include hostile Kubernetes-like objects and clean samples so false positives are visible.

**Acceptance criteria:**

- [ ] Corpus includes base64/encoded payloads, split-field payloads, zero-width characters, Unicode homoglyphs, non-Latin instruction variants, indirect instruction patterns, and clean Kubernetes status/log samples.
- [ ] Each hostile sample has a tag explaining the bypass class it represents.
- [ ] Clean samples include realistic pod status, events, labels, annotations, and logs that should remain usable.

**Verification:**

- [ ] Tests or snapshot fixtures are added without changing production behavior.
- [ ] Initial tests document which samples currently pass through as residual risk.

**Dependencies:** None

**Files likely touched:**

- `tests/InfraGate.McpGateway.Tests/UnitTests/PromptInjectionGuardTests.cs`
- `tests/InfraGate.AgentGuardrails.Tests/UnitTests/**`
- `tests/TestData/guardrails/f03/**`

**Estimated scope:** Medium: 3-5 files

## Task 2: Define the model-visible tool-result envelope contract

**Description:** Specify a small, versioned JSON envelope for Gateway tool results. The envelope should separate trusted gateway metadata from untrusted tool data and provide stable fields that agent prompts and guards can reason about.

**Acceptance criteria:**

- [ ] Envelope has a version marker, tool name, source classification, generated timestamp, guardrail decision metadata, and an `untrusted` payload field.
- [ ] The untrusted payload is always JSON-escaped data, never interpolated into surrounding prose or Markdown.
- [ ] Error/result status is explicit and not inferred from free-form text.
- [ ] Contract tests prove serialized output is valid JSON and contains no unescaped attacker text outside the untrusted field.

**Verification:**

- [ ] Contract tests pass for representative success, warning/redaction, and error outputs.
- [ ] Human review confirms the contract wording does not itself introduce model instructions inside untrusted data.

**Dependencies:** Task 1

**Files likely touched:**

- `src/InfraGate.McpGateway/Guardrails/**`
- `tests/InfraGate.McpGateway.Tests/UnitTests/**`
- Optional contract note under `docs/**`

**Estimated scope:** Medium: 3-5 files

## Checkpoint: Foundation

- [ ] F-03 corpus exists and current gaps are visible.
- [ ] Envelope contract is reviewed before Gateway dispatch behavior changes.
- [ ] No production code ships with only partial contract support.

### Phase 2: Gateway Output Isolation

- [ ] Task 3: Wrap read-only downstream results in the envelope at the Gateway boundary
- [ ] Task 4: Harden detection as audit-only defense-in-depth

## Task 3: Wrap read-only downstream results in the envelope at the Gateway boundary

**Description:** Change the Gateway read-only dispatch path so downstream Kubernetes output is returned as the strict envelope from Task 2. Preserve existing scope enforcement and downstream destructive-tool blocking.

**Acceptance criteria:**

- [ ] `GatewayToolDispatcher.HandleReadOnlyAsync` returns the envelope for read-only downstream tools instead of raw plain text.
- [ ] `GuardedToolRunner` still scans and audits request/response findings, but the returned model-visible shape is always the envelope.
- [ ] Guardrail warnings are represented as structured metadata, not prepended prose that can be confused with tool data.
- [ ] Existing approval-plan status and execution-result contracts are reviewed and either excluded with rationale or wrapped consistently if model-visible.

**Verification:**

- [ ] Gateway dispatcher unit tests prove read-only results are enveloped.
- [ ] Existing gateway tests pass: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`

**Dependencies:** Task 2

**Files likely touched:**

- `src/InfraGate.McpGateway/McpTransport/GatewayToolDispatcher.cs`
- `src/InfraGate.McpGateway/Guardrails/GuardedToolRunner.cs`
- `src/InfraGate.McpGateway/McpGatewayMessages.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayToolDispatcherTests.cs`

**Estimated scope:** Medium: 4 files

## Task 4: Harden detection as audit-only defense-in-depth

**Description:** Improve deterministic detection for the bypass classes that are cheap and stable to catch, while documenting that these checks are not the primary prevention boundary.

**Acceptance criteria:**

- [ ] Scanner normalizes text with a documented Unicode normalization strategy before matching.
- [ ] Zero-width/control-format characters are detected or normalized in a way covered by tests.
- [ ] Split-field corpus cases produce at least an audit finding when fields are evaluated as a combined payload for one tool response.
- [ ] Findings remain warnings/quarantine signals and do not replace the envelope boundary.

**Verification:**

- [ ] F-03 corpus tests pass for deterministic scanner coverage where expected.
- [ ] Clean Kubernetes fixtures do not regress into broad false positives.

**Dependencies:** Task 3

**Files likely touched:**

- `src/InfraGate.McpGateway/Guardrails/Scanning/PromptInjectionGuard*.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/PromptInjectionGuardTests.cs`

**Estimated scope:** Medium: 2-3 files

## Checkpoint: Gateway Boundary

- [ ] Gateway read-only tool output no longer returns raw Kubernetes text as top-level model-visible prose.
- [ ] Audit and telemetry still record guardrail findings.
- [ ] No auth, scope, or approval-path behavior changed except intentional result shaping.

### Phase 3: Agent Ingestion Enforcement

- [ ] Task 5: Enforce envelope-aware model-visible guard evaluation in Agent MCP ingestion
- [ ] Task 6: Update Observer and Planner prompts/docs to treat tool data as opaque

## Task 5: Enforce envelope-aware model-visible guard evaluation in Agent MCP ingestion

**Description:** Make Observer and Planner ingestion treat MCP tool results as model-visible content that must pass through `IModelVisibleContentGuard`. The guard should evaluate the serialized envelope and be able to quarantine/block suspicious untrusted payloads without losing trusted metadata.

**Acceptance criteria:**

- [ ] Agent tool-result middleware recognizes the envelope and preserves its trusted metadata for logs/metrics.
- [ ] Quarantine/block decisions replace only the model-visible payload with bounded placeholders and forensic digests.
- [ ] Production configuration does not silently fall back to `AllowAllModelVisibleContentGuard` when content guarding is expected.
- [ ] Tests prove hostile tool results do not reach the LLM as actionable instructions.

**Verification:**

- [ ] Agent guardrail tests pass: `dotnet test tests/InfraGate.AgentGuardrails.Tests/InfraGate.AgentGuardrails.Tests.csproj`
- [ ] Observer/Planner unit tests covering tool-result ingestion pass if touched.

**Dependencies:** Task 3

**Files likely touched:**

- `src/InfraGate.AgentGuardrails/ModelVisibleContentGuardExtensions.cs`
- `src/InfraGate.AgentMcp/AgentMcpToolset.cs`
- `src/InfraGate.Observer/**`
- `src/InfraGate.Planner/**`
- `tests/InfraGate.AgentGuardrails.Tests/UnitTests/**`

**Estimated scope:** Medium: 3-5 files

## Task 6: Update Observer and Planner prompts/docs to treat tool data as opaque

**Description:** Align the agent-facing instructions with the envelope contract. The model should be told that `untrusted` fields are observations only, not commands, policies, or higher-priority instructions.

**Acceptance criteria:**

- [ ] Observer and Planner prompt templates include concise tool-result boundary rules.
- [ ] Rules are phrased as operating constraints, not long defensive prose that bloats prompts.
- [ ] README/configuration docs describe the envelope and guard behavior for maintainers.

**Verification:**

- [ ] Prompt rendering tests pass if prompt templates have snapshot/contract tests.
- [ ] Documentation review confirms F-03 status is not overstated before all tests pass.

**Dependencies:** Task 5

**Files likely touched:**

- `src/InfraGate.Observer/**/prompts/**` or prompt assets used by Observer
- `src/InfraGate.Planner/**/prompts/**` or prompt assets used by Planner
- `src/InfraGate.AgentGuardrails/README.md`
- `src/InfraGate.McpGateway/README.md`
- `docs/configuration.md`

**Estimated scope:** Small-Medium: 3-5 files

## Checkpoint: Agent Boundary

- [ ] Controlled agents receive enveloped tool results and guard decisions before model ingestion.
- [ ] Prompt/library docs match the implemented behavior.
- [ ] F-03 adversarial samples are covered by automated tests.

### Phase 4: Optional Semantic Classifier Gate and Final Audit Update

- [ ] Task 7: Complete or explicitly defer the local semantic classifier research gate
- [ ] Task 8: Update security audit status with evidence

## Task 7: Complete or explicitly defer the local semantic classifier research gate

**Description:** Use `.agents/Plans/loose/2026-06-03-local-semantic-classifier-research-plan.md` to decide whether a local/offline semantic classifier should be added behind `IModelVisibleContentGuard`. Do not implement a classifier unless candidate maintenance, licensing, provenance, and measurement gates pass.

**Acceptance criteria:**

- [ ] The research plan records selected/rejected candidates and repo-specific corpus measurements.
- [ ] If a classifier is selected, implementation is behind `IModelVisibleContentGuard` with timeout, size bound, fail-closed production behavior, metrics, and tests.
- [ ] If no classifier is selected, F-03 is documented as mitigated by envelope isolation plus deterministic guardrails, with residual risk explicitly stated.

**Verification:**

- [ ] Human review of classifier selection/defer decision.
- [ ] If implemented, classifier adapter tests and sidecar smoke tests pass.

**Dependencies:** Tasks 1-6

**Files likely touched:**

- `.agents/Plans/loose/2026-06-03-local-semantic-classifier-research-plan.md`
- Optional `src/InfraGate.AgentGuardrails.LocalClassifier/**`
- Optional `tests/InfraGate.AgentGuardrails.LocalClassifier.Tests/**`
- Optional deploy/run-profile files for sidecar wiring

**Estimated scope:** Small if deferred; Medium if implemented after research approval

## Task 8: Update security audit status with evidence

**Description:** Update F-03 in the security audit only after the implementation and tests prove the new model-visible boundary. Record exact controls and residual risks without claiming regex scanning is complete.

**Acceptance criteria:**

- [ ] F-03 implementation notes cite the envelope boundary, guard enforcement, corpus tests, and any classifier decision.
- [ ] Summary table resolution is updated to either mitigated or partial with a clear rationale.
- [ ] README references stay consistent with the audit language.

**Verification:**

- [ ] Relevant tests from prior tasks pass in one final run.
- [ ] Markdown links and file paths in the updated audit are valid.

**Dependencies:** Task 7

**Files likely touched:**

- `.agents/Plans/loose/security-audit.md`
- `src/InfraGate.McpGateway/README.md`
- `src/InfraGate.AgentGuardrails/README.md`

**Estimated scope:** Small: 1-3 files

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---:|---|
| Envelope is treated as decorative text by clients | High | Add agent prompt rules, middleware tests, and contract tests proving raw top-level prose is gone. |
| False positives hide useful Kubernetes diagnostics | Medium | Keep benign corpus fixtures and require clean-sample regression tests. |
| Semantic classifier adds stale or risky dependencies | High | Use the existing research gate; reject candidates with weak maintenance, licensing, or provenance. |
| Shared contract creates boundary leakage | Medium | Keep the contract narrow; avoid a broad shared project; use deliberate duplication or file-linking if needed. |
| Existing external MCP clients expect plain text | Medium | Version the envelope and document compatibility; for human-facing clients, provide display-friendly trusted metadata without exposing untrusted payload as instructions. |
| Guardrail audit logs sensitive raw output | High | Persist digests/metadata by default for quarantined content; avoid raw credentials and bearer tokens in audit entries. |

## Open Questions

- Should the Gateway envelope be returned as JSON text for broad MCP client compatibility, or can current SDK/server support structured content strongly enough to use typed content fields?
- Which Gateway-owned tool results besides downstream read-only tools are model-visible enough to require the same envelope?
- What minimum semantic-classifier recall/false-positive threshold is acceptable if Task 7 selects a local classifier?
- Should external human MCP clients receive the same envelope by default, or should a separate human-display rendering be offered outside the model-facing path?

## Suggested Verification Commands

```bash
rtk dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj
rtk dotnet test tests/InfraGate.AgentGuardrails.Tests/InfraGate.AgentGuardrails.Tests.csproj
rtk dotnet test tests/InfraGate.Observer.Tests/InfraGate.Observer.Tests.csproj
rtk dotnet test tests/InfraGate.Planner.Tests/InfraGate.Planner.Tests.csproj
```
