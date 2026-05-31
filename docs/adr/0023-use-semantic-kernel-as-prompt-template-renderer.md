# ADR 0023: Use Semantic Kernel as Prompt-Template Renderer

**Status:** Accepted  
**Date:** 2026-05-30

## Context

The Observer and Planner agents require parameterised system prompts — the namespace name and maximum tool-iteration count vary per agent instance and must be injected at runtime. Previously each service either embedded an `ISystemPromptProvider` that loaded an embedded resource and performed string replacement, or duplicated the logic inline.

The team needed a single, testable abstraction that:

1. Loads prompt templates from embedded Markdown resources.
2. Renders Handlebars-style `{{variable}}` placeholders with typed values.
3. Enforces required-variable contracts at call time so missing arguments surface clearly.
4. Is replaceable behind an interface without exposing the underlying template engine to callers.

## Decision

Introduce **`InfraGate.Prompts`** as a thin project containing:

- `IPromptLibrary` — the public seam that callers (`ObservationCycleRunner`, `BatchProcessor`) depend on.
- `PromptLibraryBuilder` — fluent builder for registering templates with optional required-variable lists.
- `SemanticKernelPromptLibrary` — the single internal implementation backed by Semantic Kernel's Handlebars renderer.
- `RegisteredPrompt` — internal value type pairing a compiled template with its required-variable list.
- `PromptLibraryServiceCollectionExtensions` — DI wiring.

Semantic Kernel is used **as a renderer only**: no SK kernel services, plugins, functions, or memory are registered or used. The `Kernel` instance is a stateless empty object (`Kernel.CreateBuilder().Build()`) held as a static field and reused for every render.

## Consequences

**Positive:**
- Callers depend only on `IPromptLibrary` — a five-line interface with no SK types visible at the boundary.
- Replacing the renderer (e.g. switching to Scriban) requires changing only `SemanticKernelPromptLibrary` and its private builder call.
- Required-variable validation surfaces at `RenderAsync` call time, keeping template configuration errors catchable in tests.
- The Observer and Planner both gain structured JSON `ChatResponseFormat` constraints (via `ToolCallingAgentFactory.Create`) to improve LLM output reliability.

**Negative / trade-offs:**
- Adds a Semantic Kernel dependency to `InfraGate.Prompts`. If SK changes its Handlebars interface, a version bump may require updating `PromptLibraryBuilder` and `SemanticKernelPromptLibrary`.
- Two projects (`InfraGate.Observer`, `InfraGate.Planner`) now share a Prompts project; care must be taken not to let Planner-specific template logic leak into the shared project.

## Alternatives Considered

- **Simple string interpolation / `string.Format`** — rejected; Handlebars is already tested by SK, handles nested syntax, and keeping parity with the prompt files (which use `{{var}}`) is desirable.
- **Promote SK types directly into Observer/Planner** — rejected; creates tight coupling between agent logic and SK version upgrades.
- **Custom minimal template engine** — rejected; not worth maintaining for what amounts to variable substitution in Markdown files.
