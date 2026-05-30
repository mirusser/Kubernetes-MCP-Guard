# Implementation Plan: Standardizing the Prompt Libraries (Roadmap §2)

**Date:** 2026-05-30
**Roadmap item:** `.agents/Plans/Roadmap/2026-05-29-agent-framework-migration-roadmap.md` §2 — *"Developing and maintaining structured prompt libraries for AI agents supporting tasks across the SDLC."*
**Depends on:** §1 (managed agent workflows) — **Done 2026-05-29**. Both agents already build `ChatClientAgent`s via the shared `ToolCallingAgentFactory`.

## Overview

Replace the two divergent, ad-hoc prompt loaders in `InfraGate.Observer` and `InfraGate.Planner` with a single **Prompt Library** — a deep module exposing one interface (`IPromptLibrary`) that loads named, versionable prompt-template assets and renders them with **typed, validated arguments**. Semantic Kernel's template engine sits *behind* that interface as a pure string renderer (no LLM, no `Kernel` services); the existing Agent Framework agents are untouched and just receive the rendered string as before.

In the same effort, formalize the agents' **output contract**: extend `ToolCallingAgentFactory` so each agent can pass a JSON-schema `ResponseFormat` to its `ChatClientAgent` — a *separate* seam from input templating — applied defensively (existing parsers and the JSON-shape prose stay in place; tightened in §5).

## Background — current state (verified)

| | Observer | Planner |
|---|---|---|
| Loader | `Prompts/SystemPromptProvider.cs` (`ISystemPromptProvider`, DI singleton) | `Cycle/BatchProcessor.cs::LoadSystemPrompt()` (private static + `Lazy<string>`) |
| Asset | embedded `Prompts/ObserverSystemPrompt.md` | embedded `Prompts/PlannerSystemPrompt.md` |
| Templating | `.Replace("{NAMESPACE}", …)`, `.Replace("{MAX_TOOL_ITERATIONS}", …)` — **unvalidated** (a typo'd token passes through silently) | **none** (static string) |
| Hand-off to agent | rendered string → `ToolCallingAgentFactory.Create(name, instructions, tools, maxIters)` | identical |
| Per-run payload | `SnapshotDocument` → JSON → `ChatMessage(User, …)` in `SnapshotExecutor` | `AnomalyReport` → JSON → `agent.RunAsync(json)` in `DecideExecutor` |

**Framework reality (confirmed against the local clone `~/OtherRepos/agent-framework`):** the Microsoft Agent Framework .NET SDK has **no** Semantic-Kernel-style prompt templates (no `IPromptTemplate` / Handlebars / Liquid / Prompty). Its only "prompt-as-config" story is **Declarative agents** (`Microsoft.Agents.AI.Declarative`: YAML + Power Fx over `IConfiguration`), which is config-expression-oriented, pulls in `Microsoft.Agents.ObjectModel` + `Microsoft.PowerFx.Interpreter`, and ships an experimental surface (`<NoWarn>MEAI001</NoWarn>`). The roadmap's "adopt the framework's structured templates (similar to SK semantic functions)" therefore does not map to an existing .NET capability — hence the decision below.

## Architecture decisions

- **D1 — Build a Prompt Library seam, don't adopt Declarative.** Introduce a new shared module `InfraGate.Prompts` exposing `IPromptLibrary`. Rationale: deep module (one tiny interface hiding load + render + validate), keeps conventions local, no Power Fx / ObjectModel / experimental surface. *Deletion test:* removing `IPromptLibrary` re-scatters load/render/validate logic back into both services → complexity reappears across two callers → the seam earns its keep. **Two adapters = real seam** (Observer + Planner).
- **D2 — Semantic Kernel as the render engine, behind the seam.** Use SK purely as a template renderer: `HandlebarsPromptTemplateFactory.Create(PromptTemplateConfig).RenderAsync(emptyKernel, KernelArguments) → string`. The `Kernel` is an empty `Kernel.CreateBuilder().Build()` (no services, no API key — variable substitution needs none). SK is contained entirely inside `InfraGate.Prompts`; `InfraGate.AgentLlm` stays SK-free. The seam makes SK swappable for an in-repo renderer later with zero churn in Observer/Planner. *Accepted cost:* SK is the **predecessor** of Agent Framework (§1 migrated off raw M.E.AI onto Agent Framework); re-introducing it — even just the template package — is mildly counter-current, justified by a mature engine with validation + injection controls and no Agent Framework equivalent. **Offer an ADR** recording this so future reviews don't re-litigate.
- **D3 — Template format: Handlebars.** `{{namespace}}` / `{{maxToolIterations}}`. Handlebars supports loops/conditionals (useful if user-message templating is added later) and declared input variables give fail-fast validation. The prompt bodies' JSON examples use single braces and are unaffected by `{{…}}` tokens. Packages: `Microsoft.SemanticKernel.Core` + `Microsoft.SemanticKernel.PromptTemplates.Handlebars` (Abstractions comes transitively). *Fallback if the extra package is unwanted:* SK's default `{{$var}}` factory (`KernelPromptTemplateFactory`, Core only).
- **D4 — Prompt content ownership stays local.** Each agent keeps its own prompt asset as an `EmbeddedResource` in its own project and registers it with the library at startup (name + template text + format + declared input variables). The shared module owns the *machinery*, not the *content*.
- **D5 — Scope: system prompts + output contract.** Move both **system** prompts behind `IPromptLibrary`. Per-run user-message payloads (`SnapshotDocument`, `AnomalyReport`) **stay as raw-JSON user messages** as today. Additionally add `ResponseFormat` to the agents (separate seam). User-message templating is explicitly out of scope (future enhancement).
- **D6 — Output contract is additive and defensive.** Keep the existing JSON-shape prose and the `AnomalyParseExecutor` / `DecideExecutor` parsers. `ResponseFormat` is reinforcement where the configured OpenRouter model honors it; it is not the sole contract. Hard tightening (removing prose, failing closed on non-JSON) is deferred to §5.

## Target shape (sketch — finalize during implementation)

```csharp
// InfraGate.Prompts — the only surface Observer/Planner depend on
public interface IPromptLibrary
{
    Task<string> RenderAsync(string templateName, IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);
}
```

```csharp
// InfraGate.Prompts — SK hidden inside; empty Kernel; required-arg validation
internal sealed class SemanticKernelPromptLibrary(IReadOnlyDictionary<string, RegisteredPrompt> templates)
    : IPromptLibrary
{
    private static readonly Kernel EmptyKernel = Kernel.CreateBuilder().Build();

    public async Task<string> RenderAsync(string templateName, IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!templates.TryGetValue(templateName, out var prompt))
            throw new KeyNotFoundException($"Unknown prompt template '{templateName}'.");
        prompt.ValidateRequired(arguments);                 // fail-fast on missing required args
        var ka = new KernelArguments();
        foreach (var (k, v) in arguments) ka[k] = v;
        return await prompt.Template.RenderAsync(EmptyKernel, ka, cancellationToken).ConfigureAwait(false);
    }
}
```

```csharp
// InfraGate.AgentLlm/ToolCallingAgentFactory.cs — additive, optional param (existing callers unchanged)
public (ChatClientAgent Agent, Func<int> GetToolCallCount) Create(
    string name, string instructions, IReadOnlyList<AITool> tools, int maxToolIterations,
    ChatResponseFormat? responseFormat = null)
{
    // …existing counting-tool + UseFunctionInvocation pipeline…
    var options = new ChatClientAgentOptions
    {
        Name = name,
        ChatOptions = new ChatOptions
        {
            Instructions = instructions,
            Tools = countedTools,
            ResponseFormat = responseFormat,   // null ⇒ identical to today
        },
    };
    var agent = new ChatClientAgent(chatClient, options);
    return (agent, () => Volatile.Read(ref count));
}
```

*(Verified: `ChatClientAgent(IChatClient, ChatClientAgentOptions, …)` exists and the convenience ctor already routes `instructions`/`tools` through `ChatOptions`, so this is behavior-preserving when `responseFormat` is null.)*

---

## Task list

### Phase 1: Foundation — the `InfraGate.Prompts` module

#### Task 1: Scaffold `InfraGate.Prompts` project + `IPromptLibrary` seam
**Description:** Create the shared library project with the public `IPromptLibrary` interface, the `RegisteredPrompt` record (name, `IPromptTemplate`, required-variable names), the internal `SemanticKernelPromptLibrary`, and a builder/registration type. No agent wiring yet.
**Acceptance criteria:**
- [ ] `IPromptLibrary.RenderAsync(name, args, ct)` renders a Handlebars template to a string using an empty `Kernel`.
- [ ] Unknown template name throws; missing **required** argument throws (fail-fast) before rendering.
- [ ] SK is referenced only here; surface is `internal` except `IPromptLibrary`, the registration builder, and the DI extension (`sealed`, file-scoped namespace, primary ctors, `ConfigureAwait(false)`).
**Verification:**
- [ ] `dotnet build src/InfraGate.Prompts/InfraGate.Prompts.csproj`
- [ ] New project added to the solution; restore resolves SK packages on net10.0.
**Dependencies:** None
**Files likely touched:**
- `src/InfraGate.Prompts/InfraGate.Prompts.csproj` (refs `Microsoft.SemanticKernel.Core`, `Microsoft.SemanticKernel.PromptTemplates.Handlebars`; `InternalsVisibleTo` `InfraGate.Prompts.Tests`)
- `src/InfraGate.Prompts/IPromptLibrary.cs`
- `src/InfraGate.Prompts/SemanticKernelPromptLibrary.cs`
- `src/InfraGate.Prompts/RegisteredPrompt.cs`, `src/InfraGate.Prompts/PromptLibraryBuilder.cs`
- `src/InfraGate.Prompts/GlobalUsings.cs`, `k8s-toolkit.sln`
**Estimated scope:** Medium

#### Task 2: DI registration extension + `InfraGate.Prompts.Tests`
**Description:** Add `AddInfraGatePromptLibrary(this IServiceCollection, Action<PromptLibraryBuilder>)` registering `IPromptLibrary` as a singleton. Create the test project and unit-test the renderer.
**Acceptance criteria:**
- [ ] Registration builds templates once (cached `IPromptTemplate`), library resolves as singleton.
- [ ] Tests: `RenderAsync_AllArgsProvided_SubstitutesTokens`, `RenderAsync_MissingRequiredArg_Throws`, `RenderAsync_UnknownTemplate_Throws`, `RenderAsync_NoVariables_ReturnsTemplateVerbatim`.
- [ ] **No mock frameworks** — use the real `SemanticKernelPromptLibrary` (deterministic, no I/O), per repo rule.
**Verification:**
- [ ] `dotnet test tests/InfraGate.Prompts.Tests/InfraGate.Prompts.Tests.csproj`
**Dependencies:** Task 1
**Files likely touched:**
- `src/InfraGate.Prompts/PromptLibraryServiceCollectionExtensions.cs`
- `tests/InfraGate.Prompts.Tests/InfraGate.Prompts.Tests.csproj`
- `tests/InfraGate.Prompts.Tests/UnitTests/SemanticKernelPromptLibraryTests.cs`
**Estimated scope:** Medium

### Checkpoint: Foundation
- [ ] `InfraGate.Prompts` builds; unit tests pass; no SK leakage outside the module; full solution still builds.

---

### Phase 2: Observer slice — render the system prompt via the library

#### Task 3: Convert `ObserverSystemPrompt.md` to a Handlebars asset
**Description:** Replace `{NAMESPACE}` → `{{namespace}}` and `{MAX_TOOL_ITERATIONS}` → `{{maxToolIterations}}`. Keep it an `EmbeddedResource` in the Observer project. Confirm the JSON example block (single braces) renders unchanged.
**Acceptance criteria:**
- [ ] Rendering with `{ ["namespace"]="default", ["maxToolIterations"]=8 }` reproduces today's output byte-for-byte (modulo intended token swap).
**Verification:** covered by Task 4 tests.
**Dependencies:** Task 1
**Files likely touched:** `src/InfraGate.Observer/Prompts/ObserverSystemPrompt.md` (+ `.csproj` resource entry if renamed)
**Estimated scope:** Small

#### Task 4: Swap Observer to `IPromptLibrary`; delete `SystemPromptProvider`
**Description:** Register the Observer template at startup. In `ObservationCycleRunner`, pre-render the per-namespace system prompts in `RunAsync` (async) and pass the rendered map into `BuildWorkflow` (which becomes pure — no prompt I/O). Replace the `ISystemPromptProvider` dependency with `IPromptLibrary`. Delete `ISystemPromptProvider` + `SystemPromptProvider` and the Observer DI line at `Program.cs:89`.
**Acceptance criteria:**
- [ ] `ObservationCycleRunner` no longer references `ISystemPromptProvider`; `BuildWorkflow` takes `IReadOnlyDictionary<string,string> renderedPrompts`.
- [ ] Observer registers its prompt via `AddInfraGatePromptLibrary` in `Program.cs`; `ProjectReference` to `InfraGate.Prompts` added.
- [ ] `ObservationCycleRunnerTests` updated to supply a **real** `IPromptLibrary` (not a substitute); all existing Observer unit/integration tests pass.
**Verification:**
- [ ] `dotnet test tests/InfraGate.Observer.Tests/InfraGate.Observer.Tests.csproj`
**Dependencies:** Tasks 2, 3
**Files likely touched:**
- `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs`, `src/InfraGate.Observer/Program.cs`, `src/InfraGate.Observer/InfraGate.Observer.csproj`
- delete `src/InfraGate.Observer/Prompts/SystemPromptProvider.cs`, `…/ISystemPromptProvider.cs`
- `tests/InfraGate.Observer.Tests/UnitTests/ObservationCycleRunnerTests.cs`
**Estimated scope:** Medium

### Checkpoint: Observer
- [ ] Observer builds and all its tests pass; the `.md`→`.cs` deletions leave no dangling references (build is the proof).

---

### Phase 3: Planner slice — render the system prompt via the library

#### Task 5: Replace `BatchProcessor.LoadSystemPrompt` with `IPromptLibrary`
**Description:** Register `PlannerSystemPrompt.md` with the library (no template vars initially — renders verbatim, but standardized and ready for future args). Inject `IPromptLibrary` into `BatchProcessor`; render once in `ProcessBatchAsync` (async) and pass the string into `DecideExecutor` exactly as today. Remove the `Lazy<string> systemPrompt` field, `LoadSystemPrompt()`, and the `System.Reflection`/`GetManifestResourceStream` usage. Keep `PlannerConventions.Prompts` as the template-name constant.
**Acceptance criteria:**
- [ ] `BatchProcessor` no longer reads embedded resources directly; `DecideExecutor`'s `string systemPrompt` contract is unchanged (its tests untouched).
- [ ] Planner registers its prompt + `ProjectReference` to `InfraGate.Prompts`; `Program.cs` DI wired.
- [ ] All existing Planner unit/integration tests pass.
**Verification:**
- [ ] `dotnet test tests/InfraGate.Planner.Tests/InfraGate.Planner.Tests.csproj`
**Dependencies:** Task 2
**Files likely touched:**
- `src/InfraGate.Planner/Cycle/BatchProcessor.cs`, `src/InfraGate.Planner/Program.cs`, `src/InfraGate.Planner/InfraGate.Planner.csproj`
- `src/InfraGate.Planner/Prompts/PlannerSystemPrompt.md` (format pass; vars optional)
**Estimated scope:** Medium

### Checkpoint: Planner
- [ ] Planner builds and all its tests pass; both agents now share one prompt seam; two ad-hoc loaders are gone.

---

### Phase 4: Output contract — `ResponseFormat` on both agents

#### Task 6: Add optional `ResponseFormat` to `ToolCallingAgentFactory.Create`
**Description:** Extend `Create` with `ChatResponseFormat? responseFormat = null` and route it through `ChatClientAgentOptions.ChatOptions.ResponseFormat` (see sketch). Additive — existing call sites compile unchanged. Add an `AgentLlm` unit test asserting null ⇒ no `ResponseFormat`, non-null ⇒ propagated.
**Acceptance criteria:**
- [ ] `Create(...)` behavior is byte-identical when `responseFormat` is null.
- [ ] When supplied, the agent's `ChatOptions.ResponseFormat` is set (asserted via the fixture chat client's captured options).
**Verification:**
- [ ] `dotnet test tests/InfraGate.AgentLlm.Tests/InfraGate.AgentLlm.Tests.csproj`
**Dependencies:** None (parallelizable with Phases 2–3)
**Files likely touched:** `src/InfraGate.AgentLlm/ToolCallingAgentFactory.cs`, `tests/InfraGate.AgentLlm.Tests/UnitTests/ToolCallingAgentFactoryTests.cs`
**Estimated scope:** Small

#### Task 7: Define + pass output schemas (Observer array, Planner decision object)
**Description:** Build a JSON-schema `ChatResponseFormat` for each agent from its output DTO using `AIJsonUtilities` (in `Microsoft.Extensions.AI.Abstractions`, already referenced — **no new deps**). Observer: anomaly-report list (use an **object-wrapper** DTO, e.g. `{ "anomalies": [...] }`, since several providers reject a top-level array root for structured outputs — see Risks). Planner: the decision object (`operationType` / `arguments` / `reasoning`). Pass each into `ToolCallingAgentFactory.Create`. **Keep** the existing prose and parsers; if a wrapper is introduced, the Observer parser must accept both shapes.
**Acceptance criteria:**
- [ ] Both agents pass a non-null `ResponseFormat`; existing parse paths still succeed on representative fixtures.
- [ ] No regression in `AnomalyParseExecutor` / `DecideExecutor` parsing tests.
**Verification:**
- [ ] `dotnet test tests/InfraGate.Observer.Tests/…` and `tests/InfraGate.Planner.Tests/…`
- [ ] Exact `ChatResponseFormat.ForJsonSchema` / `AIJsonUtilities.CreateJsonSchema` signatures confirmed against M.E.AI 10.6.0 during implementation.
**Dependencies:** Tasks 4, 5, 6
**Files likely touched:**
- `src/InfraGate.Observer/Cycle/ObservationCycleRunner.cs` (+ output DTO/schema), `AnomalyParseExecutor.cs` if wrapper-aware
- `src/InfraGate.Planner/Cycle/Workflow/DecideExecutor.cs` (+ output schema)
**Estimated scope:** Medium

### Checkpoint: Output contract
- [ ] Both agents send `ResponseFormat`; parsing remains green; no provider was assumed to honor the schema (defensive). Full `dotnet test` of the affected projects passes.

---

### Phase 5: Docs, glossary, roadmap

#### Task 8: Module README + glossary + roadmap correction
**Description:** Add `src/InfraGate.Prompts/README.md` (purpose, `IPromptLibrary`, registration, SK-as-renderer rationale, swappability). Add **Prompt Library** / **Prompt Template** to `CONTEXT.md` (verify absent first). Add `InfraGate.Prompts` to the `AGENTS.md` Solution Map and the `repo-onboarding` README table. Update Observer/Planner READMEs where they describe prompt loading. Correct roadmap §2 wording to reflect the SK-renderer decision (framework has no SK-style templates for .NET).
**Acceptance criteria:**
- [ ] New README accurate to the code; glossary terms added; roadmap §2 no longer implies a non-existent framework capability.
**Verification:**
- [ ] Manual read-through; links resolve.
**Dependencies:** Tasks 4, 5, 7
**Files likely touched:** `src/InfraGate.Prompts/README.md`, `CONTEXT.md`, `AGENTS.md`, `.agents/skills/repo-onboarding/SKILL.md`, `src/InfraGate.Observer/README.md`, `src/InfraGate.Planner/README.md`, `.agents/Plans/Roadmap/2026-05-29-agent-framework-migration-roadmap.md`
**Estimated scope:** Medium

### Checkpoint: Complete
- [ ] `dotnet build` + `dotnet test` (Prompts, AgentLlm, Observer, Planner) all green.
- [ ] Two ad-hoc loaders deleted; one `IPromptLibrary` seam; both agents send a defensive `ResponseFormat`.
- [ ] ADR offered/recorded for the SK dependency (D2). Ready for review.

---

## Parallelization

- **Sequential foundation:** Tasks 1→2 first.
- **Then parallel:** Phase 2 (Observer), Phase 3 (Planner), and Task 6 (`ToolCallingAgentFactory`) are independent once Task 2 lands.
- **Converge:** Task 7 needs the agent slices + Task 6. Docs (Task 8) last.

## Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| SK is the *predecessor* of Agent Framework; re-adding it feels counter-current | Med | Contain SK entirely behind `IPromptLibrary` in `InfraGate.Prompts`; record ADR (D2); seam keeps swap-to-in-repo-renderer cheap |
| Structured outputs reject a **top-level array** root (Observer emits an array) | Med | Use an object-wrapper DTO (`{ "anomalies": [...] }`); keep prose + make parser accept both shapes; treat `ResponseFormat` as best-effort |
| OpenRouter model ignores `response_format` | Med | Defensive by design — existing parsers and JSON-shape prose remain authoritative; hard enforcement deferred to §5 |
| Handlebars default HTML-encodes `{{var}}` | Low | System-prompt vars are a namespace string + int — safe; if raw injection is ever needed use `{{{var}}}` / SK `AllowUnsafeContent` (only relevant if user-message templating is later added) |
| SK package version vs net10.0 / M.E.AI 10.6.0 alignment | Low | Pin current stable SK 1.x in `InfraGate.Prompts.csproj` only (repo has no central package management); validate restore in Task 1 |
| Deleting `ISystemPromptProvider` breaks a hidden caller | Low | `codegraph_impact` shows it is Observer-only; the build is the proof |

## Open questions

- **SK version to pin?** Choose the latest stable `Microsoft.SemanticKernel.*` 1.x that restores cleanly on net10.0 with M.E.AI 10.6.0 (confirm at Task 1).
- **Prompty upgrade?** Keep prompt bodies as Handlebars embedded resources now; optionally migrate to single-file `.prompty` assets (model + inputs + body) in a later pass — not in §2.
- **ADR now or later?** Recommend recording the SK-dependency decision (D2) as an ADR during Phase 5 so future architecture reviews don't re-suggest Declarative or removing SK.
