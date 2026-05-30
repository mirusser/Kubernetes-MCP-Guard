# Remediation Plan: Phase 3 Post-Verification Fixes

## Overview

Address findings from the Phase 3 implementation verification report. Covers plan deviations (FIFO→LRU, broad Exception catch), code conventions (DTOs, `var`, primary constructors, magic strings), and test coverage gaps (10+ untested code paths). Every task is small (S or M) per `planning-and-task-breakdown`.

## Architecture Decisions

- None — all fixes are within the existing `BatchProcessor`, `PlannerDedupeStore`, and test files. No new seams or abstractions.
- The FIFO→LRU fix in Task 2 is the only behavioural change; all other tasks are purely structural or additive (tests).

## Task List

### Phase 1: Correct Plan Deviations

#### Task 1: Narrow `Exception` catch in `ProposePlanAsync`

**Description:** In `BatchProcessor.ProposePlanAsync` (line 308), the broad `catch (Exception ex) when (ex is not OperationCanceledException)` swallows all failure modes. Replace with specific exceptions that `CallToolAsync` can produce: `HttpRequestException`, `TaskCanceledException`, `JsonException`, and `InvalidOperationException` (for missing PlanId extraction). The remaining edge cases (non-recoverable infrastructure errors) can still flow up to the batch-level catch in `ExecuteAsync`.

**Acceptance criteria:**
- [ ] `catch (Exception ex)` replaced with enumerated specific exception types for `CallToolAsync` failure.
- [ ] `TryExtractPlanId` returning false is already handled (line 299-306) and is not part of the catch block.
- [ ] Equivalent fault-tolerance preserved — any specific exception still increments `proposeFailedCounter` and logs.
- [ ] Tests: existing `ProposePlanFails` test changed to assert the specific exception type instead of generic `HttpRequestException`.

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/` green.

**Dependencies:** None.

**Files likely touched:**
- `src/InfraGate.Planner/Cycle/BatchProcessor.cs` (line 308)
- `tests/InfraGate.Planner.Tests/UnitTests/BatchProcessorTests.cs`

**Estimated scope:** Small (1 source file, 1 test update).

---

#### Task 2: Fix dedupe eviction from FIFO to LRU (or document deviation)

**Description:** Plan §1.9.4 specifies LRU eviction at capacity 1000. Current implementation (`PlannerDedupeStore.cs:23`) evicts the oldest by `ProposedAt` (FIFO). Two options:

**Option A (preferred — simpler):** Add a `lastAccessed` timestamp updated on every `HasActivePlan` call, and evict by `MinBy(lastAccessed)`. This adds a write to every read but is true LRU.

**Option B:** Add an `EvictionStrategy` enum (`Fifo` / `Lru`) and document the choice in `PlannerDedupeStore`. Default to `Lru`. This preserves the original code but acknowledges the deviation.

**Acceptance criteria:**
- [ ] `ActivePlanState` gains a `LastAccessedAt` field (DateTime, updated on `HasActivePlan` hit) OR an `EvictionStrategy` property is added.
- [ ] `PlannerDedupeStore.TrackActivePlan` evicts by LRU (or documents FIFO as intentional).
- [ ] Existing dedupe tests updated to cover the new eviction strategy.
- [ ] A new test exercises eviction under either strategy at capacity boundary.

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/` — specifically `DuplicateActiveAnomaly` and a new capacity-eviction test.

**Dependencies:** None.

**Files likely touched:**
- `src/InfraGate.Planner/Dedupe/ActivePlanState.cs`
- `src/InfraGate.Planner/Dedupe/PlannerDedupeStore.cs`
- `tests/InfraGate.Planner.Tests/UnitTests/BatchProcessorTests.cs`

**Estimated scope:** Small.

---

#### Task 3: Convert JSON DTOs to `record class` + fix `var` on primitive

**Description:** Seven JSON deserialization DTOs across two files use `class { get; set; }` instead of `record class`. One `var` used for a primitive `int`.

**Affected DTOs:**
- `BatchProcessor.cs`: `LlmDecisionOutput` (line 488), `LlmToolCall` (line 495)
- `AnthropicChatClient.cs`: `AnthropicRequestBody`, `AnthropicApiMessage`, `AnthropicResponseBody`, `AnthropicContentBlock`, `AnthropicUsageInfo` (lines 148-180)

**Affected `var`:**
- `AnthropicChatClient.cs:135`: `var maxTokens` → `int maxTokens`

**Acceptance criteria:**
- [ ] All 7 DTOs converted to `sealed record class`.
- [ ] `var maxTokens` → `int maxTokens`.
- [ ] `#pragma warning disable S1144, S3459` comments preserved (SonarQube false positive on records used only for deserialization).
- [ ] `dotnet build` passes with zero warnings.

**Verification:** `dotnet build src/InfraGate.Planner/InfraGate.Planner.csproj` clean; `dotnet test tests/InfraGate.Planner.Tests/` green.

**Dependencies:** None (pure refactor).

**Files likely touched:**
- `src/InfraGate.Planner/Cycle/BatchProcessor.cs`
- `src/InfraGate.Planner/Llm/AnthropicChatClient.cs`

**Estimated scope:** Small.

---

### Checkpoint: Plan Deviations Corrected

- [ ] `dotnet build InfraGate.slnx` clean, zero new warnings.
- [ ] `dotnet test tests/InfraGate.Planner.Tests/` green.
- [ ] Verify no behavioural regression: dedupe still rejects same AnomalyId, broad exception still caught but with narrower types.

---

### Phase 2: Code Convention Fixes

#### Task 4: Use primary constructors where applicable

**Description:** Two classes inject simple DI dependencies with direct field assignments and no other constructor logic — archetypal primary constructor candidates:

- `ChatClientFactory.cs:12-16` (IOptions + Meter)
- `LoggingRemediationProposalSink.cs:10-13` (ILogger)

**Acceptance criteria:**
- [ ] Both classes converted to primary constructor syntax.
- [ ] Nested `this.logger` / `this.options` / `this.meter` fields removed.
- [ ] `dotnet build` passes.

**Verification:** `dotnet build src/InfraGate.Planner/`.

**Dependencies:** None.

**Files likely touched:**
- `src/InfraGate.Planner/Llm/ChatClientFactory.cs`
- `src/InfraGate.Planner/Handoff/LoggingRemediationProposalSink.cs`

**Estimated scope:** XS.

---

#### Task 5: Move magic strings to `PlannerConventions`

**Description:** Two string constants in `BatchProcessor.cs` that follow the pattern of living in conventions classes:

- `PromptResourceName = "InfraGate.Planner.Prompts.PlannerSystemPrompt.md"` (line 16)
- `ToolCallPrefix = "TOOL_CALL:"` (line 359)

Move both to `PlannerConventions` as `public const string` fields in appropriate nested classes (e.g., `Prompts.SystemPromptResourceName`, `Llm.ToolCallPrefix`).

Also remove the plan-phase reference comment at `ChatClientFactory.cs:26`:
> `// Roadmap Phase 3.1 intentionally keeps provider arms visible for future wiring.`

**Acceptance criteria:**
- [ ] `PromptResourceName` is a `PlannerConventions` constant (e.g., `PlannerConventions.Prompts.SystemPromptResourceName`).
- [ ] `ToolCallPrefix` is a `PlannerConventions` constant (e.g., `PlannerConventions.Llm.ToolCallPrefix`).
- [ ] Plan-phase comment removed from `ChatClientFactory.cs`.
- [ ] `dotnet build` passes.

**Verification:** `dotnet build src/InfraGate.Planner/`.

**Dependencies:** None.

**Files likely touched:**
- `src/InfraGate.Planner/PlannerConventions.cs`
- `src/InfraGate.Planner/Cycle/BatchProcessor.cs`
- `src/InfraGate.Planner/Llm/ChatClientFactory.cs`

**Estimated scope:** XS.

---

#### Task 6: Fix dedupe capacity boundary + timeout test config

**Description:** Two minor fixes:

1. `PlannerDedupeStore.cs:18`: `if (activePlans.Count <= Capacity)` → `if (activePlans.Count >= Capacity)`. Current code allows 1001 entries before evicting.
2. `BatchProcessorTests.cs:148`: `AnomalyWallClockCapSeconds = 0` is outside the validated range (min=5). Change to use a pre-cancelled `CancellationTokenSource` pattern instead of relying on a sub-minimum config value.

**Acceptance criteria:**
- [ ] Capacity check uses `>= Capacity` or `> Capacity` to evict at 1000.
- [ ] Timeout test uses a pre-cancelled CTS rather than invalid config.
- [ ] Existing tests pass.

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/`.

**Dependencies:** None.

**Files likely touched:**
- `src/InfraGate.Planner/Dedupe/PlannerDedupeStore.cs`
- `tests/InfraGate.Planner.Tests/UnitTests/BatchProcessorTests.cs`

**Estimated scope:** XS.

---

#### Task 7: Add `scale_deployment` example to `PlannerSystemPrompt.md`

**Description:** The prompt's example JSON only shows `restart_deployment`. Add a second example for `scale_deployment` showing the `replicas` argument.

**Acceptance criteria:**
- [ ] Prompt contains examples for both operation types.
- [ ] Example for `scale_deployment` includes `"replicas": 3` (or similar non-zero value).
- [ ] No behavioural change — examples are for LLM guidance only; validation is code-level.

**Verification:** Manual review of prompt file.

**Dependencies:** None.

**Files likely touched:**
- `src/InfraGate.Planner/Prompts/PlannerSystemPrompt.md`

**Estimated scope:** XS.

---

### Checkpoint: Conventions Clean

- [ ] `dotnet build InfraGate.slnx` clean.
- [ ] `dotnet test tests/InfraGate.Planner.Tests/` green.
- [ ] All magic strings consolidated in `PlannerConventions`.
- [ ] Primary constructors used where trivially applicable.

---

### Phase 3: Test Coverage

#### Task 8: Add negative-path and edge-case unit tests for `BatchProcessor`

**Description:** 13 specific test gaps identified in the verification report. Each is a focused `[Fact]` or `[Theory]` in `BatchProcessorTests.cs`.

**Test scenarios to add:**

| # | Test | Severity | Description |
|---|------|----------|-------------|
| 8a | **Empty batch** | Important | `AnomalyHandoffBatch` with `Reports = []` — assert no MCP call, no sink publish. |
| 8b | **Unsupported AnomalyKind** | Important | Kind outside `{PodUnhealthy, DeploymentUnavailable, ServiceNoEndpoints, WarningEvent}` — assert filtered, no propose, no counter. |
| 8c | **Mixed-status batch** | Important | One `Resolved` + one `Active` in same batch — assert only active processed. |
| 8d | **Multi-anomaly batch** | Important | Two distinct active anomalies — assert both proposed and both in output batch. |
| 8e | **Missing planId response** | Important | MCP returns valid JSON without `planId` — assert `ProposeFailedCounter` incremented, no publish. |
| 8f | **LLM returns empty/unparseable response** | Important | Empty string, missing JSON, malformed JSON — assert no propose, correct counter. |
| 8g | **LLM non-readonly tool call** | Important | LLM returns `TOOL_CALL: { "tool": "propose_plan", ... }` — assert tool rejected with error message, loop continues. |
| 8h | **Tool iteration exhaustion** | Important | MaxToolIterations=2, LLM returns tool calls 3 times — assert loop terminates after 2 iterations. |
| 8i | **Shutdown cancellation mid-batch** | Important | Pass a cancelled `shutdownToken` — assert `ProcessBatchAsync` throws `OperationCanceledException` or propagates shutdown. |
| 8j | **Dedupe capacity eviction** | Nice-to-have | Fill `PlannerDedupeStore` past capacity — assert oldest/LRU entry evicted. |
| 8k | **Multiple tool calls then decision** | Nice-to-have | LLM does two read-only tool calls before emitting decision — assert both tools called, then propose_plan. |
| 8l | **`scale_deployment` with negative replicas** | Nice-to-have | Argument `replicas` = -1 — assert invalid arguments counter. |
| 8m | **Nested planId response format** | Nice-to-have | MCP returns `{"Content":[{"Text":"{\\"planId\\":\\"plan-x\\"}"}]}` — assert nested extraction works. |

**Acceptance criteria:**
- [ ] Tests 8a–8i (Important) implemented and passing.
- [ ] Tests 8j–8m (Nice-to-have) implemented and passing.
- [ ] All new tests use `FixtureChatClient` and `CapturingLogger` / `MeterListener` probes.
- [ ] All assertions on structural properties only (operation type, arguments, planId, counters, log properties).

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/` green, all new tests present.

**Dependencies:** Tasks 1, 2, 3 (source changes that may affect test assertions).

**Files likely touched:**
- `tests/InfraGate.Planner.Tests/UnitTests/BatchProcessorTests.cs`

**Estimated scope:** Medium (many tests, but all in one file, same patterns).

---

#### Task 9: Add boundary-value tests to `PlannerOptionsTests`

**Description:** Add tests for in-range boundary values (exactly `Min`, exactly `Max`) and default values for all three range-validated properties (`AnomalyWallClockCapSeconds`, `BatchWallClockCapSeconds`, `MaxToolIterations`).

**Acceptance criteria:**
- [ ] `[InlineData(PlannerConventions.MinAnomalyWallClockCapSeconds)]` passes `Validate()`.
- [ ] `[InlineData(PlannerConventions.MaxAnomalyWallClockCapSeconds)]` passes `Validate()`.
- [ ] Same for BatchWallClockCapSeconds and MaxToolIterations.
- [ ] Test for null/whitespace `LlmProvider` in `ChatClientFactoryTests`.

**Verification:** `dotnet test tests/InfraGate.Planner.Tests/`.

**Dependencies:** None.

**Files likely touched:**
- `tests/InfraGate.Planner.Tests/UnitTests/PlannerOptionsTests.cs`
- `tests/InfraGate.Planner.Tests/UnitTests/ChatClientFactoryTests.cs`

**Estimated scope:** Small.

---

### Checkpoint: Full Test Coverage

- [ ] All 20+ tests in `BatchProcessorTests.cs` green.
- [ ] `dotnet test tests/InfraGate.Planner.Tests/` green — zero failures.
- [ ] Coverage: every `ShouldProcess` branch, every `ParseDecision` branch, every `ProposePlanAsync` path, shutdown + cap + timeout paths.

---

### Phase 4: Verification

#### Task 10: Run full build and test suite

**Description:** Execute `dotnet build InfraGate.slnx` and `dotnet test tests/InfraGate.Planner.Tests/` to confirm all changes are clean. Also run `dotnet test tests/InfraGate.McpGateway.Tests/` (for any cross-project impact from shared conventions changes).

**Acceptance criteria:**
- [ ] `dotnet build InfraGate.slnx` — zero warnings.
- [ ] `dotnet test tests/InfraGate.Planner.Tests/` — all 20+ tests green.
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/` — green (no regression from conventions changes).

**Verification:** Command output.

**Dependencies:** Tasks 1–9.

**Files likely touched:** None.

**Estimated scope:** XS.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| LRU eviction adds contention to `HasActivePlan` (write on every read) | Low — ConcurrentDictionary ops are cheap; capacity 1000 limits total writes | Document that LRU trades read-side write for precise eviction; Option A is preferred over Option B |
| Narrowing `Exception` catch misses an unforeseen exception type | Low — the batch-level catch in `ExecuteAsync` (line 134) remains full `Exception` as safety net | Test with a fake throwing a non-standard exception type |
| 13 new tests introduce flakiness | Low — all use `FixtureChatClient` (deterministic), no timers in assertions | Review each test for race conditions during PR |

## Execution Order

1. **Phase 1** (Tasks 1→2→3) — Correct plan deviations first (these change behaviour or structure that tests depend on).
2. **Phase 2** (Tasks 4→5→6→7) — Conventions; all independent of Phase 1.
3. **Phase 3** (Tasks 8→9) — Test coverage; depends on source changes in Phases 1-2.
4. **Phase 4** (Task 10) — Final verification.

Phases 1 and 2 can be parallelised across two sessions. Phase 3 runs after both.
