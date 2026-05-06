---
name: verify-readme-docs
description: Verify repository README/readme and configuration documentation against the actual code, tests, project layout, tool contracts, environment variables, and local run instructions. Use when Codex is asked to audit, refresh, or minimally fix README files or configuration docs in this repo, especially after implementation changes that may have made docs stale.
---

# Verify README Docs

Use this skill to keep README-style docs and the canonical configuration reference factual without turning a docs check into a rewrite.

## Workflow

1. Find the doc set with:

   ```bash
   rg --files | rg -i 'readme\.md$' | rg -v '^\.agents/' | rg -v '/(bin|obj)/' | sort
   ```

   If configuration, environment variables, local runs, CI/CD settings, or release scripts are in scope, include `docs/configuration.md` explicitly. That file is the canonical source for env-var names, defaults, examples, and production guidance.

2. Treat code and tests as the source of truth. Check the relevant implementation before editing docs:

   ```bash
   rg 'ToolNames|McpServerTool|EnvironmentVariables|Default|Map(Post|Get)|Fact|Theory' src tests -g '*.cs'
   rg 'TargetFramework|PackageReference|ProjectReference' src tests -g '*.csproj'
   ```

   For configuration claims, also check option/convention classes, workflows, compose files, and scripts:

   ```bash
   rg 'EnvironmentVariables|FromEnvironment|GetEnvironmentVariable|Default|ASPNETCORE_URLS' src tests -g '*.cs'
   rg -n 'INFRA_GATE|K8S_MCP|KUBECONFIG|ASPNETCORE_URLS|DOCKERHUB|SONAR|TAG|KUBECONFIG_PATH' deploy .github scripts docs README.md
   ```

3. Compare README claims against:

   - MCP tool names, arguments, defaults, bounds, and safety constraints.
   - Environment variable names, defaults, examples, and production guidance in `docs/configuration.md`.
   - Source/test project names and target frameworks.
   - Current test coverage descriptions and opt-in integration behavior.
   - Existing scripts, deploy files, ports, endpoint paths, and generated directories.
   - CI/CD variables, workflow inputs, repository secrets, and release-script overrides documented in `docs/configuration.md`.

4. Patch only real drift. Keep wording local to the stale claim, preserve the doc's existing style, and avoid broad cleanup. If an env-var reference is duplicated outside `docs/configuration.md`, prefer linking to the configuration reference unless the surrounding doc needs the variable in a runnable command or contextual warning.

5. Verify with `git diff --check`. Run focused tests only when the docs change depends on behavior that was uncertain or recently edited. For configuration-doc updates, also verify links and duplicate reference sections:

   ```bash
   rg -n 'configuration.md' README.md docs src/*/README.md
   rg -n 'Environment Variable Reference|## Configuration' docs src/*/README.md
   ```

## Guardrails

- Do not update non-README docs unless the user asks, the README directly depends on them, or configuration claims require updating `docs/configuration.md`.
- Do not make aspirational claims sound implemented.
- Do not rewrite voice, formatting, diagrams, or marketing copy just because it could be better.
- Mention stale non-README docs in the final response instead of editing them when they are outside scope.
