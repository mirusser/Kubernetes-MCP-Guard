## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

## Skills

Reusable skill definitions live in [.agents/skills/](.agents/skills/). Load the relevant `SKILL.md` before starting work that matches its scope:

- [code-standards](.agents/skills/code-standards/SKILL.md): coding conventions for this repo — apply when making, reviewing, or refactoring code.
- [planning-and-task-breakdown](.agents/skills/planning-and-task-breakdown/SKILL.md): use for broad, vague, multi-step, or parallelizable work that needs ordered tasks with dependencies, acceptance criteria, and verification steps.
- [writing-tests](.agents/skills/writing-tests/SKILL.md): adding or modifying tests — naming, project structure, InternalsVisibleTo setup for internal types, and verification.
- [verify-readme-docs](.agents/skills/verify-readme-docs/SKILL.md): audit and minimally fix README files against actual code and tests.
- [infragate-mcp-gateway](.agents/skills/infragate-mcp-gateway/SKILL.md): use the local InfraGate MCP gateway for Kubernetes inspection and guarded changes.

## Solution Map

Start with the root [README.md](README.md) for project intent and high-level architecture. Use [devs-readme.md](docs/devs-readme.md) for setup, local runs, tool contracts, and verification. For project-level context, use these guides:

- Runtime projects:
  - [InfraGate.McpServer](src/InfraGate.McpServer/README.md): stdio MCP server, Kubernetes validation, approval plans, and plan application.
  - [InfraGate.McpGateway](src/InfraGate.McpGateway/README.md): HTTP MCP gateway, downstream stdio client, guardrails, sanitization, and audit logging.
  - [InfraGate.McpGateway.Auth](src/InfraGate.McpGateway.Auth/README.md): OAuth JWT auth, MCP protected-resource metadata, and audit identity resolution.
  - [InfraGate.DevIssuer](src/InfraGate.DevIssuer/README.md): localhost-only OAuth/OIDC-style issuer for development and Codex login testing.
- Test projects:
  - [InfraGate.McpServer.Tests](tests/InfraGate.McpServer.Tests/README.md): server unit tests and opt-in Kubernetes integration coverage.
  - [InfraGate.McpGateway.Tests](tests/InfraGate.McpGateway.Tests/README.md): gateway auth, guardrail, sanitization, audit, and forwarding tests.
  - [InfraGate.DevIssuer.Tests](tests/InfraGate.DevIssuer.Tests/README.md): dev issuer and gateway OAuth compatibility tests.
