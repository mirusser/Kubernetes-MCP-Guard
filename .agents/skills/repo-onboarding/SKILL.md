---
name: repo-onboarding
description: Orient agents in the repository before broad investigations, repo navigation, or unfamiliar work. Use this skill to read repo guidance, inspect local skills, choose relevant README docs, and avoid .agents/Plans unless historical planning context is explicitly requested.
---

# Repo Onboarding

Use this skill when you need to get oriented in `k8s-toolkit`, start a broad investigation, or find the right project context before working. Do not use it for narrow command-only questions or tiny edits that already have clear local context.

## Workflow

1. Read `AGENTS.md` first. Follow its rules for surfacing assumptions, keeping changes simple, making surgical edits, and verifying the result.
2. Inspect `.agents/skills/` and load any relevant repo-local skills:
   - `code-standards` for code edits, reviews, refactors, and convention work.
   - `infragate-mcp-gateway` for Kubernetes or local MCP gateway inspection and guarded changes.
   - `verify-readme-docs` for README audits or documentation refreshes.
3. Read `README.md` for project purpose, architecture, quick starts, capabilities, and the project map.
4. Read `docs/devs-readme.md` for local setup, run commands, MCP tool contracts, and verification.
5. Selectively read project READMEs based on the task:
   - MCP server, Kubernetes tools, validation, or approval plans: `src/InfraGate.McpServer/README.md`
   - HTTP gateway, forwarding, guardrails, sanitization, or audit logging: `src/InfraGate.McpGateway/README.md`
   - Gateway auth, bearer tokens, OAuth JWTs, protected-resource metadata, or audit identity: `src/InfraGate.McpGateway.Auth/README.md`
   - Development OAuth issuer or Codex login testing: `src/InfraGate.DevIssuer/README.md`
   - Test work: the matching `tests/*/README.md`
   - Demo manifests: `examples/failing-deployment/README.md`

## Discovery Guardrails

Exclude `.agents/Plans/**` during normal onboarding and discovery. That directory contains planning history, not current source-of-truth documentation. Read it only when the user explicitly asks for plans, roadmap details, or historical context.

When listing README docs, prefer a command that prunes planning history:

```bash
find . -path './.agents/Plans' -prune -o -iname '*readme*.md' -print | sort
```
