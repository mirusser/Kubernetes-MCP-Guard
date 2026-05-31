# Implementation Plan: Prompt Libraries Remediation

## Overview
This plan remediates the incomplete and non-compliant implementation of the Prompt Libraries standardization (Roadmap §2) - 2026-05-30-agent-framework-point2-prompt-libraries.md - It addresses critical blockers including the missing `ResponseFormat` wiring, the unauthorized use of the `NSubstitute` mock framework, and missing architectural documentation (Phase 5). It also resolves code standard violations around magic strings, `var` keyword usage for primitives, and `InternalsVisibleTo` setup.

## Architecture Decisions
- Replace `NSubstitute` mock usage with concrete Fakes (`FakeChatClientFactory`, `FakeWorkflowContext`, `FakePlannerAuditOutbox`), adhering strictly to the `writing-tests` skill which forbids mock frameworks.
- Define a `ResponseFormat` object wrapping the required JSON schemas and pass them to the `ToolCallingAgentFactory` instances to fulfill the output contract requirement without breaking existing JSON-parsing behavior.
- Fulfill Phase 5 (Documentation) by ensuring the architectural rationale for encapsulating Semantic Kernel behind `IPromptLibrary` is documented in an ADR, the glossary, and a dedicated README.

## Resources you should use
- agent-framework - local clone here: ~/OtherRepos/agent-framework/

## Task List

### Phase 1: Blockers Remediation
- [ ] **Task 1: Wire `ResponseFormat` on Agent Seam**
  **Description:** Pass the appropriate JSON-schema `ResponseFormat` into `ToolCallingAgentFactory.Create` in both `ObservationCycleRunner.cs` and `DecideExecutor.cs`.
  **Acceptance criteria:**
  - `ObservationCycleRunner.cs` passes a `ChatResponseFormat` (using the object-wrapper DTO `{ "anomalies": [...] }` schema).
  - `DecideExecutor.cs` passes a `ChatResponseFormat` for the decision object schema.
  **Verification:**
  - [ ] Tests pass: `./scripts/run-tests.sh`
  **Dependencies:** None
  **Files likely touched:**
  - `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs`
  - `src/InfraGate.Planner/Cycle/Workflow/DecideExecutor.cs`
  **Estimated scope:** Small

- [ ] **Task 2: Eradicate Mock Framework (`NSubstitute`)**
  **Description:** Remove all usage of `NSubstitute` and its NuGet package references. Replace them with hand-rolled Fakes or Stubs in unit tests.
  **Acceptance criteria:**
  - `NSubstitute` package references removed from `InfraGate.AgentLlm.Tests.csproj` and `InfraGate.Planner.Tests.csproj`.
  - `Substitute.For<T>()` removed from `ToolCallingAgentFactoryTests.cs` and `WorkflowExecutorTests.cs`.
  - Tests use custom Fake implementations for dependencies (e.g., `FakeChatClientFactory`, `FakeWorkflowContext`).
  **Verification:**
  - [ ] Tests pass: `./scripts/run-tests.sh`
  **Dependencies:** None
  **Files likely touched:**
  - `tests/InfraGate.AgentLlm.Tests/InfraGate.AgentLlm.Tests.csproj`
  - `tests/InfraGate.Planner.Tests/InfraGate.Planner.Tests.csproj`
  - `tests/InfraGate.AgentLlm.Tests/UnitTests/ToolCallingAgentFactoryTests.cs`
  - `tests/InfraGate.Planner.Tests/UnitTests/WorkflowExecutorTests.cs`
  **Estimated scope:** Medium

### Checkpoint: Blockers Remediation
- [ ] All tests pass
- [ ] Application builds without errors
- [ ] No `NSubstitute` references remain in the codebase

### Phase 2: Code Standards & Hygiene
- [ ] **Task 3: Fix Magic Strings**
  **Description:** Replace the raw string literals `"namespace"` and `"maxToolIterations"` with centralized constants from `ObserverConventions.Prompts`.
  **Acceptance criteria:**
  - `Observer/Program.cs` and `ObservationCycleRunner.cs` use `ObserverConventions.Prompts.NamespaceArgumentName` (or similar) instead of raw strings.
  **Verification:**
  - [ ] Build succeeds: `dotnet build src/InfraGate.Observer`
  **Dependencies:** None
  **Files likely touched:**
  - `src/InfraGate.Observer/Program.cs`
  - `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs`
  **Estimated scope:** Small

- [ ] **Task 4: Fix Primitive `var` Usage and Record Behavior**
  **Description:** Replace `var` with explicit primitive types (`string`, `int`, `bool`) across affected files. Convert `RegisteredPrompt` from `record class` to `class` to properly encapsulate the `ValidateRequired` behavior.
  **Acceptance criteria:**
  - No `var` usage for `string`, `int`, or `bool` in the affected files.
  - `RegisteredPrompt` is defined as a `class` (and implements equality/Deconstruct if needed, or simply acts as a standard class).
  **Verification:**
  - [ ] Build succeeds
  **Dependencies:** None
  **Files likely touched:**
  - `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs`
  - `src/InfraGate.Planner/Cycle/BatchProcessor.cs`
  - `src/InfraGate.Observer/Program.cs`
  - `src/InfraGate.Planner/Program.cs`
  - `src/InfraGate.Prompts/RegisteredPrompt.cs`
  - `src/InfraGate.AgentLlm/RateLimitRetryingChatClient.cs`
  **Estimated scope:** Medium

- [ ] **Task 5: Fix `InternalsVisibleTo` and Naming/Hygiene**
  **Description:** Replace the `AssemblyAttribute` for `InternalsVisibleTo` in `InfraGate.AgentLlm.csproj` with the `<ItemGroup><InternalsVisibleTo Include="..."/></ItemGroup>` standard. Update private static readonly fields to use `camelCase`. Pass `CancellationToken` to `LoadEmbeddedResourceAsync`.
  **Acceptance criteria:**
  - `InfraGate.AgentLlm.csproj` uses standard MSBuild item group for internals visibility.
  - Static fields in `PromptLibraryBuilder`, `SemanticKernelPromptLibrary`, and `BatchProcessor` use `camelCase`.
  - `LoadEmbeddedResourceAsync` takes and awaits a `CancellationToken`.
  **Verification:**
  - [ ] Build succeeds
  **Dependencies:** None
  **Files likely touched:**
  - `src/InfraGate.AgentLlm/InfraGate.AgentLlm.csproj`
  - `src/InfraGate.Prompts/PromptLibraryBuilder.cs`
  - `src/InfraGate.Prompts/SemanticKernelPromptLibrary.cs`
  - `src/InfraGate.Planner/Cycle/BatchProcessor.cs`
  - `src/InfraGate.Observer/Program.cs`
  - `src/InfraGate.Planner/Program.cs`
  **Estimated scope:** Small

### Checkpoint: Code Standards & Hygiene
- [ ] `code-standards` violations resolved.
- [ ] `writing-tests` violations (InternalsVisibleTo) resolved.

### Phase 3: Documentation & ADR
- [ ] **Task 6: Create Missing ADR and Module README**
  **Description:** Add an ADR documenting the decision to use Semantic Kernel purely as a template renderer (Decision D2). Create the `src/InfraGate.Prompts/README.md` file describing the module's purpose.
  **Acceptance criteria:**
  - `docs/adr/00XX-use-semantic-kernel-for-prompt-templates.md` is created.
  - `src/InfraGate.Prompts/README.md` exists and is populated.
  **Verification:**
  - [ ] Manual check of file presence and content.
  **Dependencies:** None
  **Files likely touched:**
  - `docs/adr/00XX-use-semantic-kernel-for-prompt-templates.md`
  - `src/InfraGate.Prompts/README.md`
  **Estimated scope:** Small

- [ ] **Task 7: Update Glossary, Onboarding, and Roadmap**
  **Description:** Add "Prompt Library" and "Prompt Template" to `CONTEXT.md`. Add `InfraGate.Prompts` to the `AGENTS.md` Solution Map and the `repo-onboarding` skill. Update the Observer README and the migration roadmap to reflect the SK-renderer decision.
  **Acceptance criteria:**
  - All requested documentation updates from Phase 5 of the original plan are fulfilled.
  **Verification:**
  - [ ] Run `verify-readme-docs` skill agent locally or manual check.
  **Dependencies:** Task 6
  **Files likely touched:**
  - `CONTEXT.md`
  - `AGENTS.md`
  - `.agents/skills/repo-onboarding/SKILL.md`
  - `src/InfraGate.Observer/README.md`
  - `.agents/Plans/Roadmap/2026-05-29-agent-framework-migration-roadmap.md`
  **Estimated scope:** Small

### Checkpoint: Complete
- [ ] All blockers addressed.
- [ ] Code meets style standards.
- [ ] Documentation accurately reflects architecture.
- [ ] Ready for review.

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| JSON-schema generation failures during runtime (Task 1) | High | Ensure proper object wrapping and test execution using the exact schema generation calls required by `M.E.AI`. |
| Hand-rolled Fakes miss assertions that `NSubstitute` provided | Med | Explicitly track state (e.g., call counts, passed arguments) in the Fakes to ensure the unit tests maintain identical assertion strength. |

## Open Questions
- Is there an existing base `FakeChatClient` in `AgentLlm.Tests` we can extend, or do we need to author `FakeChatClientFactory` entirely from scratch?
