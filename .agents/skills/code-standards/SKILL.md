---
name: code-standards
description: Apply this repository's coding standards when Codex makes, reviews, or refactors code in k8s-toolkit. Use for code edits, cleanup, reviews, and convention work, especially to avoid repeated or unexplained magic strings by introducing appropriately scoped constants, enums, or convention helpers.
---

# Code Standards

## Overview

Apply these standards alongside the repo's `AGENTS.md` instructions. Keep changes simple, surgical, and easy to verify.

## Magic Strings

Avoid introducing repeated or unexplained string literals in code.

Prefer named conventions when a string is:

- Repeated in multiple places.
- Part of an external contract, such as MCP tool names, JSON keys, environment variable names, HTTP paths, Kubernetes apiVersion/kind values, audit event names, file extensions, or persisted operation names.
- Used both for declaration and invocation, such as an attribute name and the downstream call using the same tool name.
- Easy to mistype and hard to catch with the compiler.

Choose the smallest suitable shape:

- Use `const string` for compile-time values, especially values used in attributes.
- Use nested static classes for related names when the project already groups conventions that way.
- Use enums only when serialization, persistence, and display text are either not involved or explicitly handled.
- Keep one-off user-facing sentences inline unless extracting them makes the code clearer.

When changing existing literals, preserve behavior and public contracts. Do not rename external values just to make the constant name prettier.

## Scope

Keep conventions local to the project unless there is already a shared project or the same contract is intentionally shared across projects. Avoid creating shared abstractions only to remove a small amount of duplication.

After replacing magic strings, run the narrowest useful build or tests for the touched project.

## Field Naming

Use lower camel case for private fields. Do not prefix private fields with `_`.

Example:

```csharp
private readonly JsonSerializerOptions jsonOptions;
```

Do not introduce:

```csharp
private readonly JsonSerializerOptions _jsonOptions;
```

When touching existing code, prefer aligning fields you edit with this convention. Do not churn unrelated fields just to rename them.

## Type Organization

Prefer one meaningful top-level type per file. If a file contains multiple classes, handlers, helpers, records, or stores that are not tightly coupled, split them into separate files with matching type names.

Keeping multiple types in one file is acceptable when the extra types are tiny implementation details that are tightly bound to the primary type and are not useful elsewhere. Avoid broad "grab bag" files that collect loosely related auth, storage, transport, or validation types together.

## .NET Norms

- Async methods should end in `Async`.
- Pass `CancellationToken` through async I/O and external calls.
- Keep public surface minimal; prefer `internal` unless cross-project use is intentional.
- Use nullable reference types honestly; avoid `!` unless there is a clear invariant.
- Prefer constructor injection for dependencies and options records for configuration.
- Keep DTOs/contracts separate from behavior-heavy services when they grow.
- Name tests by behavior using `Method_State_ExpectedResult`.
