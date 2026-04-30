---
name: verify-readme-docs
description: Verify repository README/readme documentation against the actual code, tests, project layout, tool contracts, and local run instructions. Use when Codex is asked to audit, refresh, or minimally fix README files in this repo, especially after implementation changes that may have made docs stale.
---

# Verify README Docs

Use this skill to keep README-style docs factual without turning a docs check into a rewrite.

## Workflow

1. Find the doc set with:

   ```bash
   rg --files | rg -i 'readme\.md$' | rg -v '^\.agents/' | rg -v '/(bin|obj)/' | sort
   ```

2. Treat code and tests as the source of truth. Check the relevant implementation before editing docs:

   ```bash
   rg 'ToolNames|McpServerTool|EnvironmentVariables|Default|Map(Post|Get)|Fact|Theory' src tests -g '*.cs'
   rg 'TargetFramework|PackageReference|ProjectReference' src tests -g '*.csproj'
   ```

3. Compare README claims against:

   - MCP tool names, arguments, defaults, bounds, and safety constraints.
   - Environment variable names and defaults.
   - Source/test project names and target frameworks.
   - Current test coverage descriptions and opt-in integration behavior.
   - Existing scripts, deploy files, ports, endpoint paths, and generated directories.

4. Patch only real drift. Keep wording local to the stale claim, preserve the doc's existing style, and avoid broad cleanup.

5. Verify with `git diff --check`. Run focused tests only when the docs change depends on behavior that was uncertain or recently edited.

## Guardrails

- Do not update non-README docs unless the user asks or the README directly depends on them.
- Do not make aspirational claims sound implemented.
- Do not rewrite voice, formatting, diagrams, or marketing copy just because it could be better.
- Mention stale non-README docs in the final response instead of editing them when they are outside scope.
