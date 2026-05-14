## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them — don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

Carve-out: for clearly-scoped bug reports with a reproduction, don't ask permission — diagnose, fix, and show the fix passing.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you wrote 200 lines and it could be 50, rewrite it.

Test: "Would a senior engineer say this is overcomplicated?" If yes, simplify. 

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it — don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

Test: every changed line should trace directly to the user's request.

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

## 5. Don't Claim Done Without Proof

**"It should work" is not done. Show evidence.**

Before marking a task complete:
- Paste the test output, command output, or log line that proves it.
- If you can't show it, you haven't done it — keep working.
- Ask yourself: "Would a staff engineer approve this on PR review?"

Anti-rationalization: don't say "I'll add tests later" — write them now or say out loud you're not going to.

## 6. Learn From Corrections

**Every correction is a rule waiting to be written.**

When I correct you:
- Append a one-line rule to [.agents/lessons.md](.agents/lessons.md).
- Format: `[area] Don't X — do Y instead. (cause: <one phrase>)`
- Read `.agents/lessons.md` at the start of every session, before planning.
- If the same rule fires three times, promote it into the relevant SKILL.md.

## 7. Keep Context Clean

**Context is finite. Spend it on the task, not on "just in case".**

- For research-heavy steps (reading large files, exploring unfamiliar code), use a subagent and return only the conclusion.
- One task per subagent. Don't bundle.
- Don't fetch files "just in case" — fetch on demand.

## Skills

Reusable skill definitions live in [.agents/skills/](.agents/skills/). Load the relevant `SKILL.md` before starting work that matches its scope:

- [code-standards](.agents/skills/code-standards/SKILL.md): coding conventions for this repo — apply when making, reviewing, or refactoring code.
- [planning-and-task-breakdown](.agents/skills/planning-and-task-breakdown/SKILL.md): use for broad, vague, multi-step, or parallelizable work that needs ordered tasks with dependencies, acceptance criteria, and verification steps.
- [writing-tests](.agents/skills/writing-tests/SKILL.md): adding or modifying tests — naming, project structure, InternalsVisibleTo setup for internal types, and verification.
- [verify-readme-docs](.agents/skills/verify-readme-docs/SKILL.md): audit and minimally fix README files against actual code and tests.
- [infragate-mcp-gateway](.agents/skills/infragate-mcp-gateway/SKILL.md): use the local InfraGate MCP gateway for Kubernetes inspection and guarded changes.
- [review-mutation-approval-flow](.agents/skills/review-mutation-approval-flow/SKILL.md): review the MCP mutation-approval glossary, flow diagrams, relationship table, profile sketch, and related ADRs for consistency.

## Solution Map

Start with [README.md](README.md) for intent and architecture. Use [devs-readme.md](docs/devs-readme.md) for setup, local runs, tool contracts, and verification.

Load only the project README you need:

- Runtime projects:
  - [InfraGate.McpServer](src/InfraGate.McpServer/README.md): stdio MCP server, Kubernetes validation, approval plans, and plan application.
  - [InfraGate.McpGateway](src/InfraGate.McpGateway/README.md): HTTP MCP gateway, downstream stdio client, guardrails, sanitization, and audit logging.
  - [InfraGate.McpGateway.Auth](src/InfraGate.McpGateway.Auth/README.md): OAuth JWT auth, MCP protected-resource metadata, and audit identity resolution.
  - [InfraGate.Approvals](src/InfraGate.Approvals/README.md): shared approval storage, challenge lifecycle, audit event conventions, and typed audit payloads.
  - [InfraGate.DevIssuer](src/InfraGate.DevIssuer/README.md): localhost-only OAuth/OIDC-style issuer for development and Codex login testing.
- Test projects:
  - [InfraGate.McpServer.Tests](tests/InfraGate.McpServer.Tests/README.md): server unit tests and opt-in Kubernetes integration coverage.
  - [InfraGate.McpGateway.Tests](tests/InfraGate.McpGateway.Tests/README.md): gateway auth, guardrail, sanitization, audit, and forwarding tests.
  - [InfraGate.McpGateway.KeycloakTests](tests/InfraGate.McpGateway.KeycloakTests/README.md): opt-in Keycloak Testcontainers integration tests covering real OIDC discovery, JWKS validation, and token acquisition through the gateway's JWT bearer pipeline.
  - [InfraGate.DevIssuer.Tests](tests/InfraGate.DevIssuer.Tests/README.md): dev issuer and gateway OAuth compatibility tests.
  - [InfraGate.Safety.E2E.Tests](tests/InfraGate.Safety.E2E.Tests/README.md): opt-in end-to-end tests proving the seven approval-flow safety properties through real OAuth (Keycloak in a container), gateway TestHost, McpServer subprocess, and a developer-provided Kubernetes cluster.
