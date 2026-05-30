# InfraGate.Prompts

Prompt-template library for InfraGate agents. Provides a thin `IPromptLibrary` seam over a Semantic Kernel Handlebars renderer so callers never depend on SK types directly.

## Purpose

Agent projects (`InfraGate.Observer`, `InfraGate.Planner`) need parameterised system prompts — namespace names, iteration caps, etc. — rendered at runtime from embedded Markdown templates. This project encapsulates that concern behind a single interface.

## Key Types

| Type | Visibility | Role |
|---|---|---|
| `IPromptLibrary` | public | Call-site contract: `RenderAsync(name, args, ct)` |
| `PromptLibraryBuilder` | public | Fluent builder: `AddTemplate(name, text, requiredVars?)` |
| `PromptLibraryServiceCollectionExtensions` | public | DI wiring: `services.AddInfraGatePromptLibrary(b => ...)` |
| `SemanticKernelPromptLibrary` | internal | SK Handlebars renderer behind the `IPromptLibrary` seam |
| `RegisteredPrompt` | internal | Compiled template + required-variable list |

## Usage

```csharp
// Registration (Program.cs or test)
services.AddInfraGatePromptLibrary(b => b.AddTemplate(
    "my-prompt",
    templateText,
    ["namespace", "maxToolIterations"]));

// Rendering
var prompt = await library.RenderAsync(
    "my-prompt",
    new Dictionary<string, object?> { ["namespace"] = "default", ["maxToolIterations"] = 8 },
    cancellationToken);
```

Template syntax is Handlebars (`{{variable}}`). Templates are plain Markdown strings loaded from embedded resources by the consuming project.

## Design decisions

See [ADR 0023](../../docs/adr/0023-use-semantic-kernel-as-prompt-template-renderer.md) — Semantic Kernel is used as a renderer only; no SK kernel services, plugins, or memory are registered.
