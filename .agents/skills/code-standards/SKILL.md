---
name: code-standards
description: Apply this repository's coding standards when Codex makes, reviews, or refactors code in k8s-toolkit. Use for code edits, cleanup, reviews, and convention work, especially to avoid repeated or unexplained magic strings by introducing appropriately scoped constants, enums, or convention helpers.
---

# Code Standards

## Overview

Apply these standards alongside the repo's `AGENTS.md` instructions. Keep changes simple, surgical, and easy to verify.

## Magic Strings

Avoid repeated or unexplained string literals. Prefer named conventions when a string is repeated, part of an external contract (MCP tool names, JSON keys, env vars, HTTP paths, Kubernetes apiVersion/kind, audit event names), used for both declaration and invocation, or easy to mistype.

Avoid introducing repeated or unexplained string literals in code.

Choose the smallest suitable shape:

- Use `const string` for compile-time values, especially values used in attributes.
- Use nested static classes for related names when the project already groups conventions that way.
- Use enums only when serialization, persistence, and display text are either not involved or explicitly handled.
- Keep one-off user-facing sentences inline unless extracting them makes the code clearer.
- When changing existing literals, preserve behavior and public contracts.

## Scope

Keep conventions local to the project unless there is already a shared project or the same contract is intentionally shared across projects. Avoid creating shared abstractions only to remove a small amount of duplication.

## Field Naming

Lower camel case for private fields, no `_` prefix: `private readonly JsonSerializerOptions jsonOptions;`
Align fields you edit with this convention. Do not churn unrelated fields.

## Type Organization

One meaningful top-level type per file. Split files when types are not tightly coupled. Multiple types in one file are acceptable only for tiny implementation details bound to the primary type. No broad "grab bag" files. No `#region` — if a file needs them, split it instead.

## Formatting

Match surrounding code exactly. No column-aligned spacing.

## .NET Norms

- Async methods end in `Async`.
- Pass `CancellationToken` through all async I/O and external calls.
- Keep public surface minimal; prefer `internal` unless cross-project use is intentional.
- Use nullable reference types honestly; avoid `!` without a clear invariant.
- Constructor injection for dependencies; options records for configuration.
- Keep DTOs/contracts separate from behavior-heavy services when they grow.
- Use primary constructors where applicable.
- File-scoped namespaces (`namespace Foo.Bar;`) in all new files.
- `global using` directives go in a single `GlobalUsings.cs` per project.
- Call `ConfigureAwait(false)` on all awaited tasks in library/tool code.
- Use `using var` declarations unless early disposal is needed.
- Prefer `IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>` on public and internal API surfaces.
- Mark classes `sealed` by default; only leave them open when subclassing is intentional.

## var

Use `var` when the type is obvious from the right-hand side (e.g. `new`, casts, named factories). Use explicit types when the initializer doesn't make the type clear. Never use `var` for primitives (`int`, `bool`, `string`).

## Boolean Members

Name booleans as questions: `IsReady`, `HasFailed`, `CanRetry`. Avoid negated names like `IsNotReady` or `NoCache`.

## Exception Handling

Catch specific exceptions, not `Exception`, except at top-level boundaries. Do not swallow exceptions silently. Use static guard helpers: `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrEmpty`, `ArgumentOutOfRangeException.ThrowIfNegativeOrZero`. Do not use exceptions for control flow.

## Pattern Matching

Prefer `is` patterns over `as`-casts with null checks. Prefer `switch` expressions for exhaustive dispatch. Avoid hard `(T)obj` casts without a clear invariant.

## Records

Use `record` or `record struct` for immutable value objects, DTOs, and configuration snapshots. Prefer positional records for small types. Avoid adding behavior beyond simple accessors — if a type needs methods, use a `class`.

## Structured Logging

Use `ILogger<T>` with message templates. Never use string interpolation in log calls. For high-frequency paths, use the `[LoggerMessage]` source generator.

```csharp
// correct
logger.LogInformation("Evicted pod {PodName} from {Namespace}", pod.Name, pod.Namespace);

// avoid
logger.LogInformation($"Evicted pod {pod.Name} from {pod.Namespace}");
```

## Analyzer and Build Hygiene

Respect existing `.editorconfig`, analyzer, nullable, and warning settings. Do not silence analyzer warnings.

Do not introduce broad `NoWarn`, disabled nullable contexts, or project-wide analyzer changes as part of a local code edit.

## Tests

- One test class per production class, named `{TypeUnderTest}Tests`. Use `[Theory]` with `[InlineData]` or `[MemberData]` over duplicated `[Fact]` tests. No shared mutable state between test cases. Assert on observable outputs, not implementation details. - - Name tests `Method_State_ExpectedResult`.
