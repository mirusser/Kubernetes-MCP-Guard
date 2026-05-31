---
name: jetbrains-refactor
description: Refactor code using JetBrains Rider MCP tools (reformat, lint, rename, build). Use after making code changes when user asks to reformat, lint, clean up, or apply code conventions via the IDE. Always chain with the project's code-standards skill before refactoring.
---

# JetBrains Refactor

Use JetBrains Rider MCP tools to refactor, reformat, and lint code in any project. Always load the project's code-standards skill first.

## Prerequisites

JetBrains Rider must be running with the MCP server. The default port is `64342`:

```bash
ss -tlnp | grep 64342 || echo "Rider MCP not listening"
```

If the IDE backend is still indexing (CPU > 30%), JetBrains MCP calls time out. Wait until CPU drops, or increase the MCP server timeout in `~/.config/opencode/opencode.json`. Use `ps -p <backend_pid> -o %cpu` to check.

## Workflow

1. **Load the project's code-standards.** If a `.agents/skills/code-standards/SKILL.md` exists, load it to learn naming, formatting, and structural conventions. If not, infer conventions from surrounding code.

2. **Reformat all changed files** with `jetbrains-ide_reformat_file` — one call per file (batch in parallel). This applies the IDE's code style (indentation, spacing, braces) as configured in `.editorconfig` and Rider settings.

3. **Lint changed files** with `jetbrains-ide_lint_files` (batch up to ~8 files, `min_severity: warning`). For a stricter pass, use `min_severity: error`.

4. **Fix violations.** For each warning found:
   - **Naming violations** (`does not match rule '...'`): use `jetbrains-ide_rename_refactoring` first. If it fails (common for local functions, lambdas), fall back to `jetbrains-ide_replace_text_in_file`, then reformat.
   - **Unused usings, dead code, redundant constructs**: use `jetbrains-ide_replace_text_in_file`, then reformat.
   - **Pre-existing warnings** in code you did not touch: leave them alone.

5. **Reformat again** after any text replacements to keep style consistent.

6. **Verify** with `jetbrains-ide_build_solution` and `jetbrains-ide_get_project_problems` (`severity: Error`).

## JetBrains Tool Reference

| Task | Tool | Notes |
|---|---|---|
| Reformat a file | `reformat_file` | Fast; always use first |
| Lint multiple files | `lint_files` | Batch up to ~8; `min_severity: warning` |
| Per-file error check | `get_file_problems` | `errorsOnly: true` for build errors only |
| Project-wide errors | `get_project_problems` | `severity: Error` |
| Rename a symbol | `rename_refactoring` | Fails on local functions — fall back to `replace_text_in_file` |
| Replace text in file | `replace_text_in_file` | `caseSensitive: true`; always reformat after |
| Find symbol location | `search_symbol` | Verify a new symbol is indexed after creation |
| Build via IDE | `build_solution` | Equivalent to CLI build, but via IDE backend |
| Inspect a symbol | `get_symbol_info` | Returns doc + metadata at file/line/col |
| Discover run configs | `get_run_configurations` | Project-level or per-file run points |
| Run a config | `execute_run_configuration` | Run tests or apps via IDE |
| Explore project tree | `list_directory_tree` | IDE-aware file tree (skips ignored/binary) |

## Known Limitations

- **Timeout during indexing.** Rider backend at high CPU makes all MCP calls time out. Wait for CPU to drop below 20%.
- **`rename_refactoring`** cannot find local functions or closure variables — use `replace_text_in_file`.
- **`findTests`** is heavy and often times out on large solutions — use the CLI test runner instead.
- **`lint_files`** reports ReSharper/Rider analyzer warnings which are a superset of compiler warnings. Some may be project-specific suppressions.
- **Large batches.** `lint_files` with >10 files often times out. Split into smaller batches.
